using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Reserves Alt (and Ctrl+Alt) for the mod's drink-serving shortcuts. While Alt is held, the
    /// game must NOT change the hotbar ("uso rápido") selection: Alt+digit was both serving a drink
    /// AND switching the hotbar slot at the same time (user: "não deve interagir com o uso rápido
    /// quando usar Alt"). Blocks ActionBarInventory.SetCurrentSlotSelected while Alt is down.
    /// (Walking is blocked separately in MovementAxisPatch.)
    /// </summary>
    public static class HotbarBlockPatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ActionBarInventory), "SetCurrentSlotSelected", new[] { typeof(int) });
            if (target != null)
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(HotbarBlockPatch), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            // return false = skip the original (don't change the selected slot) while Alt is held
            return !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
        }
    }
}
