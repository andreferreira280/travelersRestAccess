using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Recomputes the "HorizontalMove"/"VerticalMove" axes from WASD only, ignoring whatever
    /// Rewired computed from arrow keys. Set permanently on in Main.cs (user's explicit
    /// request: arrows should never move the character, even outside menus - only WASD
    /// should). Walking keeps working exactly as before; arrow keys are entirely free for
    /// the mod's own use (menu navigation, dialogue re-read, etc.).
    /// </summary>
    public static class MovementAxisPatch
    {
        public static bool SuppressArrowMovement;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(PlayerInputs), "GetAxis", new[] { typeof(string) });
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(MovementAxisPatch), nameof(Prefix)));
        }

        public static bool Prefix(string JKJJKBAFNMO, ref float __result)
        {
            if (!SuppressArrowMovement) return true;

            if (JKJJKBAFNMO == "HorizontalMove")
            {
                __result = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                return false;
            }
            if (JKJJKBAFNMO == "VerticalMove")
            {
                __result = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                return false;
            }
            return true;
        }
    }
}
