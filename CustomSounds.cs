using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Networking;

namespace TravellersRestAccess
{
    /// <summary>
    /// Plays the user's own WAV files (dropped in the project root, copied next to the
    /// built DLL by the .csproj) for cases where the game's own audio systems turned out
    /// unreliable from our mod (confirmed: Footsteps/MultiAudioManager never produced
    /// audible sound for us across 3 separate attempts, despite firing correctly). Uses a
    /// throwaway AudioSource instead of the game's Sound/MultiAudioManager classes -
    /// independent of whatever made those silent.
    /// </summary>
    public static class CustomSounds
    {
        private static AudioClip _wallClip;
        private static AudioClip _itemClip;
        private static AudioClip _standClip;
        private static AudioClip _wallDownClip;
        private static AudioClip _wallUpClip;
        private static AudioClip _wallLeftClip;
        private static AudioClip _wallRightClip;
        private static AudioClip _itemBumpClip;
        private static bool _loadStarted;

        // User's explicit request: a distinct sound per item type, named after the item
        // ("baú.wav", "cama.wav", etc.) - loaded into a lookup keyed by filename (lowercase,
        // no extension) instead of one field per item, since the user can keep adding more
        // without needing a code change for each one.
        private static readonly Dictionary<string, AudioClip> _itemClips = new Dictionary<string, AudioClip>();
        private static readonly string[] KnownItemSoundNames = { "baú", "cama", "mesa", "torneira" };

        // User's explicit request: all custom sounds were a bit too loud - 60%.
        private const float Volume = 0.6f;

        public static void EnsureLoaded()
        {
            DebugLogger.LogState($"CustomSounds: EnsureLoaded called, _loadStarted={_loadStarted}");
            if (_loadStarted) return;
            _loadStarted = true;
            MelonCoroutines.Start(LoadAll());
        }

        private static IEnumerator LoadAll()
        {
            DebugLogger.LogState("CustomSounds: LoadAll coroutine started");
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            yield return LoadClip(Path.Combine(baseDir, "parede.wav"), clip => _wallClip = clip);
            yield return LoadClip(Path.Combine(baseDir, "itens.wav"), clip => _itemClip = clip);
            yield return LoadClip(Path.Combine(baseDir, "stand.wav"), clip => _standClip = clip);
            // Directional wall-proximity feature: one distinct clip per direction
            // (originally cima/direita/esquerda shared one file - user later provided 4
            // separate ones, named to match each direction).
            yield return LoadClip(Path.Combine(baseDir, "baixo.wav"), clip => _wallDownClip = clip);
            yield return LoadClip(Path.Combine(baseDir, "cima.wav"), clip => _wallUpClip = clip);
            yield return LoadClip(Path.Combine(baseDir, "esquerda.wav"), clip => _wallLeftClip = clip);
            yield return LoadClip(Path.Combine(baseDir, "direita.wav"), clip => _wallRightClip = clip);

            // User's explicit request: distinct sound per item type, and a distinct
            // "bumping into a non-wall obstacle" sound (closed doors, furniture - anything
            // blocking the path that isn't a wall).
            foreach (var itemName in KnownItemSoundNames)
            {
                yield return LoadClip(Path.Combine(baseDir, itemName + ".wav"), clip => _itemClips[itemName] = clip);
            }
            yield return LoadClip(Path.Combine(baseDir, "batendo em item.wav"), clip => _itemBumpClip = clip);
        }

        private static IEnumerator LoadClip(string path, System.Action<AudioClip> onLoaded)
        {
            if (!File.Exists(path))
            {
                DebugLogger.LogState($"CustomSounds: file not found: {path}");
                yield break;
            }

            using (var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.WAV))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    DebugLogger.LogState($"CustomSounds: failed to load {path}: {request.error}");
                    yield break;
                }

                onLoaded(DownloadHandlerAudioClip.GetContent(request));
                DebugLogger.LogState($"CustomSounds: loaded {Path.GetFileName(path)}");
            }
        }

        // User's explicit request: if the nearby item has its own named clip (e.g.
        // "Baú pequeno" matching "baú.wav"), play that instead of the generic itens.wav -
        // matched by substring (item names are descriptive, e.g. "Baú pequeno", not just
        // "Baú"). Pitch/pan encode the item's direction relative to the player: vertical
        // dominant -> pitch (higher = in front/cima, lower = behind/baixo), horizontal
        // dominant -> normal pitch, panned left/right instead.
        public static bool HasItemClip(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            string lower = itemName.ToLowerInvariant();
            foreach (var entry in _itemClips)
            {
                if (entry.Value != null && lower.Contains(entry.Key)) return true;
            }
            return false;
        }

        // User's explicit request: per-item volume tweaks (cama +25%, mesa -40%) without
        // touching the shared 60% base volume used by everything else.
        private static readonly Dictionary<string, float> _itemVolumeMultipliers = new Dictionary<string, float>
        {
            { "cama", 1.25f },
            { "mesa", 0.6f },
        };

        public static void PlayItemNearby(string itemName, float pitch = 1f, float pan = 0f)
        {
            AudioClip clip = _itemClip;
            float volumeMultiplier = 1f;
            if (!string.IsNullOrEmpty(itemName))
            {
                string lower = itemName.ToLowerInvariant();
                foreach (var entry in _itemClips)
                {
                    if (lower.Contains(entry.Key) && entry.Value != null)
                    {
                        clip = entry.Value;
                        _itemVolumeMultipliers.TryGetValue(entry.Key, out volumeMultiplier);
                        if (volumeMultiplier <= 0f) volumeMultiplier = 1f;
                        break;
                    }
                }
            }

            PlayOneShot(clip, pan, pitch, volumeMultiplier);
        }

        // User's explicit request: a single quick tap into a wall should still produce a
        // sound, not just sustained holding - separate one-shot, not the loop below. Also
        // asked for a minimum duration since the wav itself is shorter than felt right;
        // 1s -> 0.5s -> 0.3s -> 0.2s -> 0.26s across rounds per request.
        public static void PlayWallBumpOnce()
        {
            if (_wallClip == null) return;

            var go = new GameObject("TravellersRestAccess_WallTapAudio");
            var source = go.AddComponent<AudioSource>();
            source.clip = _wallClip;
            source.spatialBlend = 0f;
            source.volume = Volume;
            source.loop = true;
            source.Play();
            Object.Destroy(go, 0.26f);
        }

        // User's explicit request: a different sound for bumping into something that isn't
        // a wall (closed door, furniture, anything blocking the path) - same one-shot
        // pattern as PlayWallBumpOnce, distinct clip.
        public static void PlayItemBumpOnce()
        {
            if (_itemBumpClip == null) return;

            var go = new GameObject("TravellersRestAccess_ItemBumpAudio");
            var source = go.AddComponent<AudioSource>();
            source.clip = _itemBumpClip;
            source.spatialBlend = 0f;
            source.volume = Volume;
            source.loop = true;
            source.Play();
            Object.Destroy(go, 0.26f);
        }

        // User's request: hear stand.wav whenever the character turns to face a new
        // direction (it turns before actually moving), panned to match - right turn pans
        // right, left turn pans left, up/down play centered.
        public static void PlayDirectionChange(float pan) => PlayOneShot(_standClip, pan);

        private static void PlayOneShot(AudioClip clip, float pan = 0f, float pitch = 1f, float volumeMultiplier = 1f)
        {
            if (clip == null) return;

            var go = new GameObject("TravellersRestAccess_OneShotAudio");
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 0f;
            source.volume = Volume * volumeMultiplier;
            source.panStereo = pan;
            source.pitch = pitch;
            source.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.01f, pitch));
        }

        // User's explicit request: instead of one-shot retriggers every cooldown, keep the
        // wall sound looping continuously for as long as the player stays stuck, and stop
        // the instant they're not stuck anymore.
        private static AudioSource _wallLoopSource;

        public static void StartWallBumpLoop()
        {
            if (_wallLoopSource != null || _wallClip == null) return;

            var go = new GameObject("TravellersRestAccess_WallLoopAudio");
            _wallLoopSource = go.AddComponent<AudioSource>();
            _wallLoopSource.clip = _wallClip;
            _wallLoopSource.spatialBlend = 0f;
            _wallLoopSource.volume = Volume;
            _wallLoopSource.loop = true;
            _wallLoopSource.Play();
        }

        public static void StopWallBumpLoop()
        {
            if (_wallLoopSource == null) return;
            Object.Destroy(_wallLoopSource.gameObject);
            _wallLoopSource = null;
        }

        // Same sustained-stuck pattern as the wall loop above, but for getting stuck
        // against a non-wall obstacle (closed door, furniture) instead.
        private static AudioSource _itemBumpLoopSource;

        public static void StartItemBumpLoop()
        {
            if (_itemBumpLoopSource != null || _itemBumpClip == null) return;

            var go = new GameObject("TravellersRestAccess_ItemBumpLoopAudio");
            _itemBumpLoopSource = go.AddComponent<AudioSource>();
            _itemBumpLoopSource.clip = _itemBumpClip;
            _itemBumpLoopSource.spatialBlend = 0f;
            _itemBumpLoopSource.volume = Volume;
            _itemBumpLoopSource.loop = true;
            _itemBumpLoopSource.Play();
        }

        public static void StopItemBumpLoop()
        {
            if (_itemBumpLoopSource == null) return;
            Object.Destroy(_itemBumpLoopSource.gameObject);
            _itemBumpLoopSource = null;
        }

        // User's explicit request: a continuous, directional sense of nearby walls (not
        // just reactive bumping) - cima/baixo/esquerda/direita are tracked independently so
        // more than one can play at once (e.g. standing in a corner). Each direction has its
        // own distinct clip now (originally cima/esquerda/direita shared one file).
        private static readonly System.Collections.Generic.Dictionary<string, AudioSource> _directionalWallSources =
            new System.Collections.Generic.Dictionary<string, AudioSource>();

        public static void SetDirectionalWallSound(string direction, bool active)
        {
            bool playing = _directionalWallSources.TryGetValue(direction, out var existing) && existing != null;
            if (active == playing) return;

            if (!active)
            {
                Object.Destroy(existing.gameObject);
                _directionalWallSources.Remove(direction);
                return;
            }

            AudioClip clip = direction switch
            {
                "baixo" => _wallDownClip,
                "cima" => _wallUpClip,
                "esquerda" => _wallLeftClip,
                "direita" => _wallRightClip,
                _ => null,
            };
            if (clip == null) return;

            float pan = direction == "esquerda" ? -1f : direction == "direita" ? 1f : 0f;
            var go = new GameObject($"TravellersRestAccess_DirectionalWall_{direction}");
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = 0f;
            source.volume = Volume;
            source.panStereo = pan;
            source.loop = true;
            source.Play();
            _directionalWallSources[direction] = source;
        }

        public static void StopAllDirectionalWallSounds()
        {
            foreach (var source in _directionalWallSources.Values)
            {
                if (source != null) Object.Destroy(source.gameObject);
            }
            _directionalWallSources.Clear();
        }
    }
}
