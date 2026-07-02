using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Tile-targeting tools (Hoe/Spade/WateringCan/Seed) aim at the MOUSE CURSOR in keyboard+mouse
    /// mode - confirmed in decompiled SpadeInstance.JHDFFNJCHFN / HoeInstance: the
    /// "!IsGamepadActive || GetControlCursorWithGamepad" branch reads
    /// CursorManager.GetCursorWorldPosition(). A blind player can't position the mouse, so these
    /// tools acted on a stale/invalid tile and "did nothing".
    ///
    /// The tool ACCEPTS a target only if it lands on a valid "blue square": SpadeInstance.cs:335
    /// gates on HBEBAFHEMAP(...), which returns gridController.GetBlueSquareAtPosition(cursor) != null
    /// (ToolInstance.cs:1362). The valid blue square is the tile ONE STEP AHEAD in the facing
    /// direction - the game's own gamepad branch targets exactly playerPos + Utils.NGFODNCHPHB(facing)
    /// * 0.5f (SpadeInstance.cs:354/363). The player's OWN tile is NOT a blue square, which is why
    /// aiming at the current tile did nothing. So this Postfix forces the cursor to that front tile.
    ///
    /// Scoped (no UI, only the listed ground tools) so it never disturbs the cursor for menus,
    /// decoration/placement, or anything else that legitimately uses the mouse. No playerNum gate -
    /// single-player has one CursorManager, and the gate was a likely reason the patch didn't fire.
    /// </summary>
    public static class ToolCursorAimPatch
    {
        private const float TileStep = 0.5f;
        private static float _lastLogTime;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(CursorManager), "GetCursorWorldPosition");
            if (target == null)
            {
                if (Main.DebugMode) DebugLogger.LogState("ToolCursorAimPatch: GetCursorWorldPosition NOT FOUND - patch not applied");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ToolCursorAimPatch), nameof(Postfix)));
            if (Main.DebugMode) DebugLogger.LogState("ToolCursorAimPatch: applied");
        }

        public static void Postfix(ref Vector3 __result)
        {
            try
            {
                if (MainUI.IsAnyUIOpen(1)) return;

                var item = PlayerInventory.GetPlayer(1)?.actionBarInventory?.GetSelectedItem();
                if (!(item is Hoe || item is Spade || item is WateringCan || item is Seed)) return;

                var player = PlayerController.GetPlayer(1);
                if (player == null) return;

                // Aim at the tile the GAME actually marks diggable (its "blue square" cluster around
                // the player), NOT blindly at the facing tile. LOG PROOF (round 146): the facing tile
                // often has NO blue square while the own cell / a neighbour does, so a fixed facing
                // aim just missed. ChosenToolTile picks the facing tile if diggable, else the nearest
                // diggable tile. This is what makes the tools finally act reliably.
                Vector3 aim = WorldNavigationHandler.ChosenToolTile();
                aim.z = 0f;
                __result = aim;

                if (Main.DebugMode && Time.unscaledTime - _lastLogTime > 0.5f)
                {
                    _lastLogTime = Time.unscaledTime;
                    DebugLogger.LogState($"ToolCursorAim: tool={item.GetType().Name} facing={player.characterAnimation.FCGBJEIIMBC} aim={aim}");
                }
            }
            catch { }
        }
    }
}
