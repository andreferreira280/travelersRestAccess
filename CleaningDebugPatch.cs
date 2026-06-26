using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Debug-only visibility into mop/cleaning mechanics (FloorDirt + Table), added because the
    /// user could not get table cleaning to work at all after multiple rounds of guessing keys,
    /// and reported zero floor-dirt announcements despite walking around. Reading the decompiled
    /// source alone wasn't conclusive enough (Table.MouseHold/FloorDirt.Clean both gate on several
    /// preconditions - mop selected, minimum hold time, available clean position, proximity focus
    /// - that are easier to confirm from a live log than from more static reading).
    ///
    /// Pure observation - no gameplay behavior is changed by this patch.
    /// </summary>
    public static class CleaningDebugPatch
    {
        private static InputByProximity _lastFocused;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(
                AccessTools.Method(typeof(Table), "MouseHold"),
                postfix: new HarmonyMethod(typeof(CleaningDebugPatch), nameof(TableMouseHoldPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(FloorDirt), "Clean"),
                postfix: new HarmonyMethod(typeof(CleaningDebugPatch), nameof(FloorDirtCleanPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(FloorDirt), "DestroyFloorDirt"),
                postfix: new HarmonyMethod(typeof(CleaningDebugPatch), nameof(FloorDirtDestroyedPostfix)));

            harmony.Patch(
                AccessTools.Method(typeof(Table), "SetDirtiness"),
                postfix: new HarmonyMethod(typeof(CleaningDebugPatch), nameof(TableDirtinessPostfix)));
        }

        public static void TableMouseHoldPostfix(Table __instance, int __0, bool __1, bool __result, DoWork ___doWork, float ___dirtiness)
        {
            if (!Main.DebugMode) return;
            float holdTime = PlayerInputs.GetPlayer(__0).GHKOCEOEKGK;
            DebugLogger.LogState($"CleaningDebug: Table.MouseHold player={__0} forceStart={__1} result={__result} useHoldTime={holdTime:F2} dirtiness={___dirtiness:F1} working={___doWork.JCMBHAEKLKK} workDone={___doWork.OJLICLKJJOF:F1}/{___doWork.EFNODBKFDBD:F1} table=\"{__instance.gameObject.name}\"");
        }

        public static void FloorDirtCleanPostfix(FloorDirt __instance, int __0, float __1, bool __result, DoWork ___doWork)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"CleaningDebug: FloorDirt.Clean player={__0} speed={__1:F2} result={__result} working={___doWork.JCMBHAEKLKK} workDone={___doWork.OJLICLKJJOF:F1}/{___doWork.EFNODBKFDBD:F1} floorDirt=\"{__instance.gameObject.name}\"");
        }

        public static void FloorDirtDestroyedPostfix(FloorDirt __instance)
        {
            // User's explicit request: a sound when a floor stain is cleaned, even though
            // the game has none of its own here (DestroyFloorDirt just deactivates the
            // GameObject, no audio call at all) - always-on, not gated by DebugMode like the
            // diagnostic logging below.
            CustomSounds.PlayObjectiveCompleted();

            if (!Main.DebugMode) return;
            DebugLogger.LogState($"CleaningDebug: FloorDirt DESTROYED (cleaned) \"{__instance.gameObject.name}\"");
        }

        public static void TableDirtinessPostfix(Table __instance, float __0)
        {
            if (!Main.DebugMode) return;
            DebugLogger.LogState($"CleaningDebug: Table.SetDirtiness table=\"{__instance.gameObject.name}\" newDirtiness={__0:F1}");
        }

        /// <summary>
        /// Polled once per frame from Main.UpdateHandlers (not event-driven - no event exists
        /// for this). Logs only on change, to confirm whether/when the game's own proximity
        /// system ever focuses on a FloorDirt or Table at all while the player walks around.
        /// </summary>
        public static void PollFocus()
        {
            if (!Main.DebugMode) return;

            var manager = InputByProximityManager.GetPlayer(1);
            if (manager == null) return;

            InputByProximity current = manager.GetCurrentFocusedInputElement();
            if (current == _lastFocused) return;
            _lastFocused = current;

            if (current == null)
            {
                DebugLogger.LogState("CleaningDebug: proximity focus -> none");
                return;
            }

            var go = current.mainGameObject;
            string tag = go != null ? go.tag : "?";
            string name = go != null ? go.name : "?";
            bool isFloorDirt = go != null && go.GetComponent<FloorDirt>() != null;
            bool isTable = go != null && go.GetComponent<Table>() != null;
            DebugLogger.LogState($"CleaningDebug: proximity focus -> \"{name}\" tag={tag} isFloorDirt={isFloorDirt} isTable={isTable}");
        }
    }
}
