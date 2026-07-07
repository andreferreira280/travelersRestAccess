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
                // Play the clip that MATCHES the held tool - the old code played the "shovel" (dig)
                // clip for EVERY tool, so the mop sounded like digging (user: "esfregão está fazendo
                // som de cavar"). Each tool now uses its own game clip.
                var item = ActionBarInventory.GetPlayer(1)?.GetSelectedItem();
                AudioClip[] arr = ClipForTool(sound, item);
                if (arr == null || arr.Length == 0) return;
                CustomSounds.PlayGameClip(arr[Random.Range(0, arr.Length)], 0.6f);
            }
            catch { }
        }

        private static AudioClip[] ClipForTool(Sound s, Item item)
        {
            if (item is Mop) return s.mopHit;
            if (item is Hoe) return s.hoe;
            if (item is Spade) return s.shovel;
            if (item is WateringCan) return s.waterSplash;
            // Sickle / Ax / Pick have NO fitting AudioClip[] in Sound. workingRummaging sounded like a
            // chest/junk rummage (user: "foice tocando som de baú"), which is worse than nothing - so
            // play no added swing sound for these (the game's own sickle/axe/pick sound still plays).
            return null;
        }
    }
}
