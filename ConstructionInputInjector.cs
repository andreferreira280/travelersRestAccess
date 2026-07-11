using System;
using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    // Drives the game's OWN construction paint flow by briefly making the game believe the
    // "Interact" (paint) button is pressed/held/released, while moving the cursor with the
    // game's own CursorManager.SetCursorPositionFromWorld. This lets the game paint zones/walls
    // with its REAL internal editor state (editorSquares, selectingArea, area calc) — the thing
    // that was missing when we called the zone functions directly (which crashed).
    //
    // The paint state machine in TavernConstructionManager reads:
    //   - GetButtonDown(paint)  -> start selecting area          (PlayerInputs.JCMOPOMLPLL string)
    //   - GetButton(Interact)   -> keep painting the area        (PlayerInputs.OEFHJIAHLPC ActionType)
    //   - GetButtonUp(paint)    -> apply (ApplyEditorChanges)     (PlayerInputs.KFAFNEJNDDL string)
    // We force those to true for the Interact action only, and only during our sequence.
    public static class ConstructionInputInjector
    {
        public static bool InjectDown;
        public static bool InjectHeld;
        public static bool InjectUp;
        // True for the whole paint sequence. Used to suppress the game's "pointer over UI" block
        // (Utils.DLOMIGFOOPD) that otherwise stops the paint before it starts.
        public static bool Painting;

        private static int _phase;   // 0 = idle
        private static int _frames;
        private static Vector3 _start, _end;
        private static Action<bool> _onDone;
        private static string _interactStr;

        public static bool Busy => _phase != 0;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var held = AccessTools.Method(typeof(PlayerInputs), "OEFHJIAHLPC");   // GetButton(ActionType)
            if (held != null) harmony.Patch(held, postfix: new HarmonyMethod(typeof(ConstructionInputInjector), nameof(Post_Held)));

            // The REAL construction Update (TavernConstructionManager.Update, line 850) uses the
            // STRING overloads with the literal "Interact": GetButtonDown("Interact") /
            // GetButtonUp("Interact"). (The earlier JCMOPOMLPLL/KFAFNEJNDDL were the wrong methods.)
            var down = AccessTools.Method(typeof(PlayerInputs), "GetButtonDown", new[] { typeof(string) });
            if (down != null) harmony.Patch(down, postfix: new HarmonyMethod(typeof(ConstructionInputInjector), nameof(Post_Down)));

            var up = AccessTools.Method(typeof(PlayerInputs), "GetButtonUp", new[] { typeof(string) });
            if (up != null) harmony.Patch(up, postfix: new HarmonyMethod(typeof(ConstructionInputInjector), nameof(Post_Up)));

            // Utils.DLOMIGFOOPD(playerNum) = "pointer is over UI" -> the construction paint is gated
            // on !DLOMIGFOOPD, so while a tutorial panel is under the pointer the paint never starts.
            // Suppress it during our controlled paint only.
            var uiBlock = AccessTools.Method(typeof(Utils), "DLOMIGFOOPD");
            if (uiBlock != null) harmony.Patch(uiBlock, postfix: new HarmonyMethod(typeof(ConstructionInputInjector), nameof(Post_UiBlock)));

            // Diagnostic: log once per paint whether the construction Update actually runs and what
            // its gate values are. (Targets the REAL Update at line 850, not PHLLBPLOLFO.)
            var cUpdate = AccessTools.Method(typeof(TavernConstructionManager), "Update");
            if (cUpdate != null) harmony.Patch(cUpdate, postfix: new HarmonyMethod(typeof(ConstructionInputInjector), nameof(Post_CUpdate)));
        }

        static void Post_UiBlock(ref bool __result)
        {
            if (Painting) __result = false;
        }

        private static bool _loggedThisPaint;

        static void Post_CUpdate()
        {
            if (!Painting || _loggedThisPaint) return;
            _loggedThisPaint = true;
            int panel = -1; bool uiOver = true; bool sel = false; bool deco = false;
            try { panel = ConstructionActionBarUI.currentPanel; } catch { }
            try { uiOver = Utils.DLOMIGFOOPD(1); } catch { }
            try { sel = (bool)AccessTools.Field(typeof(TavernConstructionManager), "selectingArea").GetValue(TavernConstructionManager.GGFJGHHHEJC); } catch { }
            try { deco = DecorationMode.GetPlayer(1).DMBFKFLDDLH; } catch { }
            DebugLogger.LogState($"[INJ] cUpdate ran: panel={panel} uiOver(patched)={uiOver} selectingArea={sel} decoBuildMode={deco}");
        }

        private static string InteractStr()
        {
            if (_interactStr == null)
            {
                try { PlayerInputs.actionsString.TryGetValue(ActionType.Interact, out _interactStr); } catch { }
            }
            return _interactStr;
        }

        static void Post_Held(ActionType JKJJKBAFNMO, ref bool __result)
        {
            if (InjectHeld && JKJJKBAFNMO == ActionType.Interact) __result = true;
        }

        static bool IsInteract(string s) => s != null && (s == "Interact" || s == InteractStr());

        static void Post_Down(string JKJJKBAFNMO, ref bool __result)
        {
            if (InjectDown && IsInteract(JKJJKBAFNMO)) __result = true;
        }

        static void Post_Up(string JKJJKBAFNMO, ref bool __result)
        {
            if (InjectUp && IsInteract(JKJJKBAFNMO)) __result = true;
        }

        // Paints a rectangular area from start to end using the game's real flow. onDone(true)
        // when the release/commit has been sent. Does nothing if already running.
        public static bool PaintArea(Vector3 start, Vector3 end, Action<bool> onDone)
        {
            if (_phase != 0) { onDone?.Invoke(false); return false; }
            _start = start; _end = end; _onDone = onDone; _phase = 1; _frames = 0;
            _loggedThisPaint = false;
            return true;
        }

        // Called once per frame from ConstructionTableHandler.Update.
        public static void Tick()
        {
            if (_phase == 0) return;
            _frames++;
            switch (_phase)
            {
                case 1: // press the paint button at the start tile
                    Painting = true;
                    try { CursorManager.SetCursorPositionFromWorld(1, _start); } catch { }
                    InjectDown = true; InjectHeld = true;
                    DebugLogger.LogState($"[INJ] phase1 press start={_start} interactStr=\"{InteractStr()}\"");
                    _phase = 2; _frames = 0;
                    break;
                case 2: // hold the down for a couple frames (overlaps the game's Update regardless of
                        // call order), then clear the down edge but keep held
                    try { CursorManager.SetCursorPositionFromWorld(1, _start); } catch { }
                    InjectDown = true; InjectHeld = true;
                    if (_frames >= 2)
                    {
                        InjectDown = false;
                        bool sel = false;
                        try { sel = (bool)AccessTools.Field(typeof(TavernConstructionManager), "selectingArea").GetValue(TavernConstructionManager.GGFJGHHHEJC); } catch { }
                        DebugLogger.LogState($"[INJ] afterDown selectingArea={sel}");
                        _phase = 3; _frames = 0;
                    }
                    break;
                case 3: // move cursor to the end tile while holding (game paints the rectangle)
                    try { CursorManager.SetCursorPositionFromWorld(1, _end); } catch { }
                    InjectHeld = true;
                    if (_frames >= 4) { _phase = 4; _frames = 0; }
                    break;
                case 4: // release -> game applies (ApplyEditorChanges) with real state. Hold the up
                        // edge a couple frames too (timing).
                    InjectHeld = false; InjectUp = true;
                    if (_frames == 1) DebugLogger.LogState($"[INJ] phase4 release end={_end}");
                    if (_frames >= 2) { _phase = 5; _frames = 0; }
                    break;
                case 5: // clear up edge, finish
                    InjectUp = false; Painting = false;
                    _phase = 0;
                    var cb = _onDone; _onDone = null;
                    cb?.Invoke(true);
                    break;
            }
        }

        // Safety: clear all injection if construction UI closes mid-sequence.
        public static void Abort()
        {
            InjectDown = InjectHeld = InjectUp = Painting = false;
            _phase = 0; _onDone = null;
        }
    }
}
