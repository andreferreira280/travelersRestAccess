using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Blocks the game's own mouse-hover hotbar swap from firing during our Ctrl+1-10 /
    /// Shift+1-10 combos.
    ///
    /// Root cause of "esfregão no 1 desaparece em menos de 1 segundo, mesmo sem apertar mais
    /// nada": confirmed via diagnostic hash-code logging (InventoryTransferHandler) that the
    /// exact same Slot C# object was involved both at assignment and at the moment it read
    /// back empty - ruling out the array being rebuilt. Found the actual cause by reading
    /// decompiled ActionBarUI.Update():
    ///
    ///   if (IsOpen() && PlayerInputs.InputsEnabled(...) && GetAnyButtonDown()
    ///       && !PlayerInputs.IsGamepadActive(...))
    ///   {
    ///       SwapSlotsInput("ActionBar1", 0); ... SwapSlotsInput("ActionBar10", 9);
    ///   }
    ///
    /// This runs every frame, completely independent of our own InventoryTransferHandler,
    /// and does NOT check for Ctrl/Shift at all - "ActionBar1" is bound to the same physical
    /// "1" key our Ctrl+1 combo presses. SwapSlotsInput raycasts for whatever SlotUI is
    /// currently under the MOUSE CURSOR and swaps it into that hotbar slot via
    /// Slot.GHCDPAJHKOI - so every Ctrl+1/Shift+1 keypress was also firing this native,
    /// mouse-position-based swap on the same frame, undoing or corrupting whatever our own
    /// code had just done, depending on wherever the mouse cursor happened to be resting.
    ///
    /// Blind keyboard-only play never intentionally holds Ctrl/Shift while aiming the mouse
    /// at a specific slot, so suppressing this native swap specifically while either
    /// modifier is held is safe - sighted/mouse players keep the normal behavior untouched.
    /// </summary>
    public static class HotbarSwapPatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(ActionBarUI), "SwapSlotsInput");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(HotbarSwapPatch), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!ctrl && !shift) return true;

            if (Main.DebugMode) DebugLogger.LogState("HotbarSwapPatch: blocked native mouse-hover hotbar swap while Ctrl/Shift held");
            return false;
        }
    }
}
