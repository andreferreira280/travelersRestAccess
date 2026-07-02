using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// [72] User: tools (sickle/Sickle, axe, pickaxe, mop...) should make a sound EVERY swing,
    /// even when swinging in the air with no hit. The game DOES fire its tool sound on the
    /// swing animation event (CharacterAnimator.ToolHit, confirmed via decompiled research),
    /// but it goes through AlmenaraGames' MultiAudioManager which is inaudible from our mod's
    /// context (same root cause as footsteps). So we Postfix ToolHit() and play the game's own
    /// tool clip through our proven 2D AudioSource - one audible swing sound per swing.
    ///
    /// ToolHit() is the animation event fired on every swing regardless of whether anything was
    /// actually harvested/cut, which is exactly the "even in the air" behavior the user wants.
    /// </summary>
    public static class ToolSoundPatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(CharacterAnimator), "ToolHit");
            if (target != null)
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(ToolSoundPatch), nameof(Postfix)));
        }

        public static void Postfix()
        {
            try
            {
                var sound = Sound.GGFJGHHHEJC;
                if (sound == null) return;
                // Use the game's own "shovel" swing/impact clips as a generic tool swing sound
                // (a real game clip, plays every swing). Can be made per-tool later if needed.
                AudioClip[] arr = (sound.shovel != null && sound.shovel.Length > 0) ? sound.shovel
                    : (sound.hoe != null && sound.hoe.Length > 0) ? sound.hoe
                    : sound.workingRummaging;
                if (arr == null || arr.Length == 0) return;
                CustomSounds.PlayGameClip(arr[Random.Range(0, arr.Length)], 0.6f);
            }
            catch { }
        }
    }
}
