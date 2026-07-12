using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    public static class ConstructionTableHandler
    {
        private static bool _wasOpen;
        private static Action<int> _onPanelChanged;
        private static Action<TavernFloor> _onFloorChanged;

        // Cursor position tracking (0.5f tile steps, relative to opening position)
        private static Vector3 _originCursorTilePos = Vector3.positiveInfinity;
        private static Vector3 _lastCursorTilePos = Vector3.positiveInfinity;

        // Sub-tile positions of every floor placed via Shift+K/F8 this session. Used to apply a
        // zone over the built floor WITHOUT needing the cursor to hover each tile (the game only
        // highlights ZoneDisponible tiles where the cursor passes, so we track them ourselves).
        private static readonly List<Vector2> _placedFloorTiles = new List<Vector2>();

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var openUI = AccessTools.Method(typeof(TavernConstructionUI), "OpenUI");
            if (openUI != null)
                harmony.Patch(openUI, postfix: new HarmonyMethod(typeof(ConstructionTableHandler), nameof(AfterOpenUI)));

            var closeUI = AccessTools.Method(typeof(TavernConstructionUI), "CloseUI");
            if (closeUI != null)
                harmony.Patch(closeUI, postfix: new HarmonyMethod(typeof(ConstructionTableHandler), nameof(AfterCloseUI)));

            var updateSlot = AccessTools.Method(typeof(ConstructionActionInfoUI), "UpdateCurrentSlotInfo");
            if (updateSlot != null)
                harmony.Patch(updateSlot, postfix: new HarmonyMethod(typeof(ConstructionTableHandler), nameof(AfterUpdateCurrentSlotInfo)));
        }

        static void AfterOpenUI()
        {
            try
            {
                bool isOpen = TavernConstructionUI.IsWindowOpen();
                if (!isOpen || _wasOpen) return;
                _wasOpen = true;

                _onPanelChanged = (panel) => AnnouncePanel(panel);
                ConstructionActionBarUI.OnPanelChanged += _onPanelChanged;

                _onFloorChanged = (floor) => AnnounceFloor(floor);
                var floors = ConstructionFloors.GGFJGHHHEJC;
                if (floors != null) floors.OnFloorChanged += _onFloorChanged;

                _originCursorTilePos = Vector3.positiveInfinity;
                _lastCursorTilePos = Vector3.positiveInfinity;
                _placedFloorTiles.Clear();

                string floorName = FloorLabel(floors?.ODFBDBLCFOM ?? TavernFloor.FirstFloor);
                ScreenReader.Say(
                    $"Mesa de construção. {floorName}. Aba: Construção. " +
                    "WASD mover cursor, E colocar, ] próxima aba, [ andar. Setas pra slots. L área destacada, Shift+K colocar tudo.",
                    interrupt: true);

                try { AnnounceCurrentAction(ConstructionActionBarUI.GetEditorActionInfo()); } catch { }
            }
            catch { }
        }

        static void AfterCloseUI()
        {
            try
            {
                bool isOpen = TavernConstructionUI.IsWindowOpen();
                if (isOpen || !_wasOpen) return;
                _wasOpen = false;
                ConstructionInputInjector.Abort(); // never leave injected input hanging
                _originCursorTilePos = Vector3.positiveInfinity;
                _lastCursorTilePos = Vector3.positiveInfinity;

                if (_onPanelChanged != null)
                {
                    ConstructionActionBarUI.OnPanelChanged -= _onPanelChanged;
                    _onPanelChanged = null;
                }
                if (_onFloorChanged != null)
                {
                    var floors = ConstructionFloors.GGFJGHHHEJC;
                    if (floors != null) floors.OnFloorChanged -= _onFloorChanged;
                    _onFloorChanged = null;
                }
                ScreenReader.Say("Mesa de construção fechada.", interrupt: true);
            }
            catch { }
        }

        static void AfterUpdateCurrentSlotInfo(TavernConstructionAction BANNHBMOAFH)
        {
            try
            {
                if (!TavernConstructionUI.IsWindowOpen()) return;
                AnnounceCurrentAction(BANNHBMOAFH);
            }
            catch { }
        }

        private static void AnnouncePanel(int panel)
        {
            string name;
            switch (panel)
            {
                case 0: name = "Construção"; break;
                case 1: name = "Decoração"; break;
                case 2: name = "Zonas"; break;
                case 3: name = "Acesso"; break;
                default: name = $"Aba {panel}"; break;
            }
            ScreenReader.Say($"Aba: {name}", interrupt: true);
            try { AnnounceCurrentAction(ConstructionActionBarUI.GetEditorActionInfo()); } catch { }
        }

        private static void AnnounceFloor(TavernFloor floor)
        {
            ScreenReader.Say(FloorLabel(floor), interrupt: true);
        }

        private static void AnnounceCurrentAction(TavernConstructionAction action)
        {
            if (action == null)
            {
                ScreenReader.Say("Slot vazio", interrupt: false);
                return;
            }

            string name = null;
            try { name = action.IABAKHPEOAF(); } catch { }
            if (string.IsNullOrEmpty(name)) name = "Ação";

            string zoneSuffix = "";
            try
            {
                if (action.editorAction == EditorAction.DiningZone)
                    zoneSuffix = $" ({TavernZonesManager.GGFJGHHHEJC.GetCurrentNumberOfZones(ZoneType.DiningRoom)}/{ReputationDBAccessor.GetMaxNumOfZones(ZoneType.DiningRoom)})";
                else if (action.editorAction == EditorAction.CraftingZone)
                    zoneSuffix = $" ({TavernZonesManager.GGFJGHHHEJC.GetCurrentNumberOfZones(ZoneType.CraftingRoom)}/{ReputationDBAccessor.GetMaxNumOfZones(ZoneType.CraftingRoom)})";
                else if (action.editorAction == EditorAction.CreateRentedRoomDoor)
                    zoneSuffix = $" ({TavernZonesManager.GGFJGHHHEJC.GetCurrentNumberOfZones(ZoneType.RentedRoom)}/{ReputationDBAccessor.GetMaxNumOfZones(ZoneType.RentedRoom)})";
            }
            catch { }

            var parts = new List<string>();
            try
            {
                var cost = action.cost;
                if (cost.planks > 0) parts.Add($"{cost.planks} madeira");
                if (cost.nails > 0) parts.Add($"{cost.nails} pregos");
                if (cost.stones > 0) parts.Add($"{cost.stones} pedras");
                if (cost.mortar > 0) parts.Add($"{cost.mortar} argamassa");
                var price = cost.PFHGPBLBCDD();
                if (price.gold > 0) parts.Add($"{price.gold} ouro");
                if (price.silver > 0) parts.Add($"{price.silver} prata");
            }
            catch { }

            string costStr = parts.Count > 0 ? string.Join(", ", parts) : "grátis";

            string balance = "";
            try
            {
                var bal = Money.GetBalance();
                balance = $" Saldo: {bal.Gold} ouro, {bal.Silver} prata.";
            }
            catch { }

            ScreenReader.Say($"{name}{zoneSuffix}. {costStr}.{balance}", interrupt: false);
        }

        private static string FloorLabel(TavernFloor floor)
        {
            switch (floor)
            {
                case TavernFloor.Cellar: return "Andar: Porão";
                case TavernFloor.FirstFloor: return "Andar: Térreo";
                case TavernFloor.SecondFloor: return "Andar: Primeiro andar";
                default: return $"Andar: {floor}";
            }
        }

        // Returns the number of real actions in the current panel (no empty trailing slots).
        private static int GetPanelActionCount(ConstructionActionBarUI bar)
        {
            try
            {
                ConstructionActionBarUI.GetEditorActionInfo(); // ensures panelList is current
                var field = AccessTools.Field(typeof(ConstructionActionBarUI), "panelList");
                var list = field?.GetValue(bar) as System.Collections.IList;
                if (list != null && list.Count > 0) return list.Count;
            }
            catch { }
            return bar.uiSlots?.Length ?? 0;
        }

        private static void CheckCursorPosition()
        {
            try
            {
                var rawPos = CursorManager.GetPlayer(1).GetCursorWorldPosition();
                float sx = Mathf.Round(rawPos.x * 2f) / 2f;
                float sy = Mathf.Round(rawPos.y * 2f) / 2f;
                var snapped = new Vector3(sx, sy, rawPos.z);

                if (_originCursorTilePos.x == float.PositiveInfinity)
                    _originCursorTilePos = snapped;

                if (snapped.x == _lastCursorTilePos.x && snapped.y == _lastCursorTilePos.y) return;
                _lastCursorTilePos = snapped;

                int col = Mathf.RoundToInt((snapped.x - _originCursorTilePos.x) * 2f);
                int row = Mathf.RoundToInt((snapped.y - _originCursorTilePos.y) * 2f);

                string tileInfo = "";
                try
                {
                    var gt = WorldGrid.NCEHFMPBBAK(snapped);
                    tileInfo = gt == GroundType.Floor ? " piso" : " vazio";
                }
                catch { }

                ScreenReader.Say($"Coluna {col}, Linha {row},{tileInfo}", interrupt: true);
            }
            catch { }
        }

        private static void AnnounceHighlightedArea()
        {
            try
            {
                var tiles = EditorTileMaps.editorTiles;
                if (tiles == null || tiles.Count == 0)
                {
                    ScreenReader.Say("Nenhuma área destacada.", interrupt: true);
                    return;
                }

                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                int count = 0;
                foreach (var kv in tiles)
                {
                    if (kv.Value.editorAction != EditorAction.AddFloorDisponible) continue;
                    float x = kv.Key.x; float y = kv.Key.y;
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                    count++;
                }

                if (count == 0)
                {
                    ScreenReader.Say("Nenhum tile disponível para construir.", interrupt: true);
                    return;
                }

                if (_originCursorTilePos.x == float.PositiveInfinity)
                {
                    ScreenReader.Say($"{count} tiles disponíveis.", interrupt: true);
                    return;
                }

                int c1 = Mathf.RoundToInt(minX - _originCursorTilePos.x);
                int c2 = Mathf.RoundToInt(maxX - _originCursorTilePos.x);
                int r1 = Mathf.RoundToInt(minY - _originCursorTilePos.y);
                int r2 = Mathf.RoundToInt(maxY - _originCursorTilePos.y);

                ScreenReader.Say($"{count} tiles. Coluna {c1} a {c2}, Linha {r1} a {r2}.", interrupt: true);
            }
            catch { ScreenReader.Say("Não foi possível ler a área.", interrupt: true); }
        }

        // Returns true if at least one floor tile was actually placed.
        private static bool AutoPlaceAll()
        {
            int placed = PlaceCurrentDisponibleRing(out string err);
            if (err != null) { ScreenReader.Say(err, interrupt: true); return false; }
            if (placed <= 0) { ScreenReader.Say("Nenhuma área disponível para construir.", interrupt: true); return false; }
            try { TavernConstructionUI.BBHJJDPJKFH(); } catch { }
            ScreenReader.Say($"{placed} pisos colocados.", interrupt: true);
            return true;
        }

        // Places the current AddFloorDisponible (green) frontier as real floor and applies it. Returns
        // the number of full tiles placed this call (0 if none available). Hard failures go to err.
        private static int PlaceCurrentDisponibleRing(out string err)
        {
            err = null;
            try
            {
                var mgr = TavernConstructionManager.GGFJGHHHEJC;
                if (mgr == null) { err = "Mesa de construção não ativa."; return 0; }
                if (mgr.CHFHMMNELGP != EditorAction.AddFloor || ConstructionActionBarUI.currentPanel != 0)
                { err = "Selecione 'Adicionar piso' primeiro."; return 0; }

                var disponible = new List<Vector3>();
                foreach (var kv in EditorTileMaps.editorTiles)
                    if (kv.Value.editorAction == EditorAction.AddFloorDisponible) disponible.Add(kv.Key);
                if (disponible.Count == 0) return 0;

                var decorField = AccessTools.Field(typeof(TavernConstructionManager), "_decorationTile");
                var decorTile = decorField?.GetValue(mgr) as DecorationTile;
                if (decorTile == null)
                {
                    decorTile = Utils.KMJAGBLPODO(ConstructionFloors.GGFJGHHHEJC?.ODFBDBLCFOM ?? TavernFloor.FirstFloor);
                    decorField?.SetValue(mgr, decorTile);
                }
                var subOffsets = new Vector3[] { Vector3.zero, new Vector3(0.5f, 0f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                foreach (var pos in disponible)
                    foreach (var off in subOffsets)
                    {
                        var sub = pos + off;
                        EditorGrid.KICMMMBCPNF(sub, EditorAction.AddFloor, decorTile);
                        _placedFloorTiles.Add(new Vector2(sub.x, sub.y));
                    }
                var floorField = AccessTools.Field(typeof(TavernConstructionManager), "floorEditorTiles");
                floorField?.SetValue(mgr, _placedFloorTiles.Count / 4);
                mgr.ApplyEditorChanges();
                return disponible.Count;
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"PlaceCurrentDisponibleRing: {ex}");
                err = "Erro ao colocar piso.";
                return 0;
            }
        }

        // Multi-frame floor expander. The game recomputes the green "disponible" frontier on its OWN
        // Update after each ApplyEditorChanges, so a single press only got the current ring (~4 tiles).
        // This places ONE ring per frame and waits, growing the floor until it reaches targetTiles (or
        // the buildable area runs out). Fixes "só 4 telhas" — a crafting zone needs >= 9 (3x3).
        private static bool _expandActive;
        private static int _expandTarget;
        private static int _expandGoalIdx = -1;
        private static int _expandNoGrowth;

        private static void StartFloorExpansion(int targetTiles, int goalIdx)
        {
            if (_expandActive) return;
            _expandActive = true;
            _expandTarget = targetTiles;
            _expandGoalIdx = goalIdx;
            _expandNoGrowth = 0;
            ScreenReader.Say("Colocando piso...", interrupt: true);
        }

        private static void TickFloorExpansion()
        {
            if (!_expandActive) return;
            int placed = PlaceCurrentDisponibleRing(out string err);
            if (err != null) { _expandActive = false; ScreenReader.Say(err, interrupt: true); return; }

            if (placed <= 0) _expandNoGrowth++; else _expandNoGrowth = 0;
            int distinct = _placedFloorTiles.Count / 4;

            if (distinct >= _expandTarget || _expandNoGrowth >= 3)
            {
                _expandActive = false;
                try { TavernConstructionUI.BBHJJDPJKFH(); } catch { }
                ScreenReader.Say($"{distinct} telhas de piso colocadas.", interrupt: true);
                if (distinct > 0 && _expandGoalIdx >= 0)
                    try { BuildingTutorialManager.GoalCompleted(_expandGoalIdx); } catch { }
                _expandGoalIdx = -1;
            }
        }

        // NOTE: applying a zone by calling the game's zone functions (GCFACJDLJKN and even the
        // "safe" ChangeZone, plus the DKAECLABDNP linked-zone check) with our synthetic floor
        // positions CRASHES the game — the zone routines rely on internal editor state that our
        // data doesn't provide. Removed. Zone/wall/door automation would require simulating the
        // game's real cursor+paint input instead (see docs/modules/construction-table.md).

        // Portuguese label for each tutorial goal.
        private static string GoalName(BuildingTutorialGoals g)
        {
            switch (g)
            {
                case BuildingTutorialGoals.MoveWASD: return "Mover câmera com WASD";
                case BuildingTutorialGoals.PressSHIFT: return "Correr segurando Shift";
                case BuildingTutorialGoals.AddFloor: return "Adicionar piso";
                case BuildingTutorialGoals.ChangeTavernFloor: return "Mudar de andar";
                case BuildingTutorialGoals.ApplyCraftingZone: return "Aplicar zona de produção";
                case BuildingTutorialGoals.AssignWall: return "Atribuir parede";
                case BuildingTutorialGoals.AssignFloor: return "Atribuir piso decorativo";
                case BuildingTutorialGoals.CreateDoor: return "Criar porta";
                case BuildingTutorialGoals.CreateRentedRoomDoor: return "Criar porta de quarto alugado";
                case BuildingTutorialGoals.ChooseRoomName: return "Escolher nome do quarto";
                case BuildingTutorialGoals.ActivateBuildMode: return "Ativar modo de decoração";
                case BuildingTutorialGoals.PlaceBed: return "Colocar cama";
                case BuildingTutorialGoals.PlaceTable: return "Colocar mesa";
                case BuildingTutorialGoals.PlaceChair: return "Colocar cadeira";
                case BuildingTutorialGoals.PlaceLight: return "Colocar luz";
                case BuildingTutorialGoals.AcceptChanges: return "Aceitar mudanças";
                default: return g.ToString();
            }
        }

        // Finds the first not-yet-completed goal in the current tutorial popup.
        // Returns false if tutorial inactive or no pending goal.
        private static bool GetCurrentGoal(out int index, out BuildingTutorialGoals goal)
        {
            index = -1; goal = default;
            if (!BuildingTutorialManager.IKNOJDMCFOK) return false;
            var mgr = BuildingTutorialManager.instance;
            if (mgr == null) return false;
            var objs = AccessTools.Field(typeof(BuildingTutorialManager), "NDNOEEDFNFH")?.GetValue(mgr) as BuildingPopUp.Objective[];
            var done = BuildingTutorialManager.GetCompletedGoals();
            if (objs == null) return false;
            for (int i = 0; i < objs.Length; i++)
            {
                if (done != null && i < done.Count && done[i]) continue;
                index = i; goal = objs[i].goal; return true;
            }
            return false;
        }

        // F8: drive the building tutorial forward one step. Camera goals (which have
        // nothing to build) are marked complete; AddFloor does REAL floor placement.
        // Other construction goals are announced so we learn the real sequence before
        // implementing their real construction.
        private static void AdvanceTutorialStep()
        {
            try
            {
                if (!BuildingTutorialManager.IKNOJDMCFOK)
                {
                    ScreenReader.Say("O tutorial de construção não está ativo.", interrupt: true);
                    return;
                }
                if (!GetCurrentGoal(out int idx, out var goal))
                {
                    ScreenReader.Say("Objetivos deste passo já concluídos. Aguarde o tutorial avançar.", interrupt: true);
                    return;
                }

                string gname = GoalName(goal);
                DebugLogger.LogState($"[CTH] AdvanceTutorialStep: goal={goal} index={idx} popupOpen={BuildingTutorialManager.IsOpen()}");

                switch (goal)
                {
                    case BuildingTutorialGoals.MoveWASD:
                    case BuildingTutorialGoals.PressSHIFT:
                    case BuildingTutorialGoals.ChooseRoomName:
                    case BuildingTutorialGoals.ChangeTavernFloor:
                        // Camera/navigation goals — nothing physical to build.
                        BuildingTutorialManager.GoalCompleted(idx);
                        ScreenReader.Say($"{gname}: concluído.", interrupt: true);
                        break;

                    case BuildingTutorialGoals.AddFloor:
                        // Real construction: grow the floor over several frames up to ~4x4 (16 tiles),
                        // so a crafting zone (min 3x3) fits. Completes the goal when done.
                        StartFloorExpansion(16, idx);
                        break;

                    default:
                        // Not yet automated with real construction — report so we learn the sequence.
                        ScreenReader.Say($"Próximo objetivo: {gname}. Ainda vou implementar a construção real deste passo.", interrupt: true);
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"AdvanceTutorialStep: {ex}");
                ScreenReader.Say("Erro ao avançar o tutorial.", interrupt: true);
            }
        }

        // EXPERIMENT: paint the currently selected editor action over the placed floor's bounding
        // box using simulated Interact input (ConstructionInputInjector), driving the game's own
        // paint flow. For zones, select the crafting-zone action and dismiss the tutorial popup first.
        // --- Construction toolbar navigation by keyboard (user request round 263). ] / [ move between
        // the tabs; Left/Right arrows move between the options in the current tab. Everything is spoken. ---
        private static readonly string[] _panelNames = { "Construir", "Personalizar", "Zonas", "Portas" };
        private static readonly System.Reflection.FieldInfo _panelListField =
            AccessTools.Field(typeof(ConstructionActionBarUI), "panelList");
        // The tutorial's designated (paintable) tiles — the game blocks painting anywhere else while
        // the tutorial is active (EditorGrid.FBJHIJNIOGH:413 -> BPBIDHBIBHD -> HDENCBCBGCM.Contains).
        private static readonly System.Reflection.FieldInfo _tutorialTilesField =
            AccessTools.Field(typeof(EditorGrid), "HDENCBCBGCM");

        private static System.Collections.Generic.List<Vector2> TutorialAllowedTiles()
        {
            try { return _tutorialTilesField?.GetValue(null) as System.Collections.Generic.List<Vector2>; }
            catch { return null; }
        }
        private static readonly System.Reflection.FieldInfo _panelButtonsField =
            AccessTools.Field(typeof(ConstructionActionBarUI), "panelButtons");
        private static readonly System.Reflection.FieldInfo _panelEnabledField =
            AccessTools.Field(typeof(ConstructionActionBarUI), "JKMCDBDAICA");

        // Which tabs are currently ENABLED (the game gates them by state; a disabled tab can't be
        // focused - FocusMainPanel silently falls back to tab 0). Logged so we can see the truth.
        private static bool[] PanelEnabled()
        {
            try { return _panelEnabledField?.GetValue(ConstructionActionBarUI.GetInstance()) as bool[]; }
            catch { return null; }
        }

        private static string PanelName(int p) => (p >= 0 && p < _panelNames.Length) ? _panelNames[p] : $"guia {p}";

        private static string ActionNamePt(EditorAction a)
        {
            switch (a)
            {
                case EditorAction.None: return "nada selecionado";
                case EditorAction.AddFloor: return "Adicionar piso";
                case EditorAction.RemoveFloor: return "Remover piso";
                case EditorAction.DiningZone: return "Zona de refeição";
                case EditorAction.CraftingZone: return "Zona de produção";
                case EditorAction.RoomZone: return "Zona de quarto";
                case EditorAction.RemoveZone: return "Remover zona";
                case EditorAction.ChangeDecoFloor: return "Piso decorativo";
                case EditorAction.ChangeDecoWall: return "Parede decorativa";
                case EditorAction.ChangeDecoWallTrim: return "Acabamento de parede";
                case EditorAction.ChangeRoof: return "Telhado";
                case EditorAction.CreateDoor: return "Criar porta";
                case EditorAction.CreateRentedRoomDoor: return "Porta de quarto alugado";
                case EditorAction.CreateStairsUp: return "Escada para cima";
                case EditorAction.CreateStairsDown: return "Escada para baixo";
                case EditorAction.CreateCellarDoorDown: return "Porta de adega, descer";
                case EditorAction.CreateCellarDoorUp: return "Porta de adega, subir";
                case EditorAction.RemoveAccess: return "Remover acesso";
                case EditorAction.CreateBarn: return "Criar celeiro";
                case EditorAction.CreateChickenHouse: return "Criar galinheiro";
                default: return a.ToString();
            }
        }

        private static int CurrentPanelSlotCount()
        {
            try
            {
                try { ConstructionActionBarUI.OPPJACDGOAF(); } catch { }   // refreshes panelList to currentPanel
                var list = _panelListField?.GetValue(ConstructionActionBarUI.GetInstance()) as System.Collections.ICollection;
                return list?.Count ?? 0;
            }
            catch { return 0; }
        }

        private static void AnnounceToolbarSelection(bool withPanel)
        {
            int p = -1; try { p = ConstructionActionBarUI.currentPanel; } catch { }
            EditorAction a = EditorAction.None; try { a = TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP; } catch { }
            ScreenReader.Say(withPanel ? $"Guia {PanelName(p)}. {ActionNamePt(a)}" : ActionNamePt(a), interrupt: true);
        }

        private static void ChangeToolbarPanel(int dir)
        {
            try
            {
                int cur = ConstructionActionBarUI.currentPanel;
                var enabled = PanelEnabled();
                // Step to the next ENABLED tab in the chosen direction (skip disabled ones), so ]
                // actually reaches Zonas even if Personalizar between is off.
                int next = cur;
                for (int step = 0; step < 4; step++)
                {
                    next = ((next + dir) % 4 + 4) % 4;
                    if (enabled == null || (next < enabled.Length && enabled[next])) break;
                }

                if (Main.DebugMode)
                {
                    string en = enabled != null ? string.Join(",", System.Array.ConvertAll(enabled, b => b ? "1" : "0")) : "null";
                    DebugLogger.LogState($"[CTH] panel nav: cur={cur} -> next={next} enabled=[{en}]");
                }

                // Prefer a REAL button click (does the game's full switch: enable/rebuild slots). Fall
                // back to FocusMainPanel if the button isn't reachable.
                bool clicked = false;
                try
                {
                    var btns = _panelButtonsField?.GetValue(ConstructionActionBarUI.GetInstance()) as UnityEngine.UI.Button[];
                    if (btns != null && next >= 0 && next < btns.Length && btns[next] != null)
                    {
                        btns[next].onClick.Invoke();
                        clicked = true;
                    }
                }
                catch { }
                if (!clicked) { try { ConstructionActionBarUI.FocusMainPanel(next); } catch { } }

                // Land on the FIRST REAL option (skip a slot 0 that maps to None, which read as "nada
                // selecionado"). SetCurrentSlotSelected updates the manager action, so we can detect it.
                int cnt = CurrentPanelSlotCount();
                for (int s = 0; s < System.Math.Max(1, cnt); s++)
                {
                    try { ConstructionActionBarUI.SetCurrentSlotSelected(s); } catch { }
                    EditorAction a = EditorAction.None; try { a = TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP; } catch { }
                    if (a != EditorAction.None) break;
                }
                int now = -1; try { now = ConstructionActionBarUI.currentPanel; } catch { }
                if (Main.DebugMode) DebugLogger.LogState($"[CTH] panel nav result: currentPanel={now} clicked={clicked}");
                AnnounceToolbarSelection(true);
            }
            catch (Exception e) { MelonLoader.MelonLogger.Error($"ChangeToolbarPanel: {e}"); }
        }

        private static void ChangeToolbarSlot(int dir)
        {
            try
            {
                int cur = ConstructionActionBarUI.currentSlotSelected;
                int count = CurrentPanelSlotCount();
                if (count <= 0) count = 8;
                int next = cur + dir;
                if (next < 0) next = 0;
                if (next >= count) next = count - 1;
                ConstructionActionBarUI.SetCurrentSlotSelected(next);
                AnnounceToolbarSelection(false);
            }
            catch (Exception e) { MelonLoader.MelonLogger.Error($"ChangeToolbarSlot: {e}"); }
        }

        // Selects the ZONES panel (2) and the crafting-zone slot in the construction toolbar, the same
        // way the game's own keyboard/gamepad navigation does, so the area-paint (which reads
        // currentPanel + currentSlotSelected) paints a crafting zone. Guarded + logged so a failure is
        // visible without crashing. Returns true once the manager's action reads CraftingZone.
        private static bool SelectCraftingZoneInToolbar()
        {
            try
            {
                try { ConstructionActionBarUI.FocusMainPanel(2); } catch { }
                int panelNow = -1;
                try { panelNow = ConstructionActionBarUI.currentPanel; } catch { }
                if (panelNow != 2)
                {
                    DebugLogger.LogState($"[CTH] zones panel (2) not reachable; currentPanel={panelNow}");
                    return false;
                }
                for (int slot = 0; slot < 12; slot++)
                {
                    try { ConstructionActionBarUI.SetCurrentSlotSelected(slot); } catch { }
                    EditorAction a = EditorAction.None;
                    try { a = TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP; } catch { }
                    if (Main.DebugMode) DebugLogger.LogState($"[CTH] zones slot {slot} -> action {a}");
                    if (a == EditorAction.CraftingZone) return true;
                }
                return false;
            }
            catch (Exception e) { MelonLoader.MelonLogger.Error($"SelectCraftingZoneInToolbar: {e}"); return false; }
        }

        private static void TestPaintInjection()
        {
            try
            {
                if (!TavernConstructionUI.IsWindowOpen()) return;
                if (ConstructionInputInjector.Busy) { ScreenReader.Say("Aguarde, pintura em andamento.", interrupt: true); return; }
                if (_placedFloorTiles.Count == 0) { ScreenReader.Say("Coloque o piso primeiro (F8 em Adicionar piso).", interrupt: true); return; }
                if (BuildingTutorialManager.IKNOJDMCFOK && BuildingTutorialManager.IsOpen())
                {
                    ScreenReader.Say("Dispense o popup do tutorial primeiro com a seta cima, e tente de novo.", interrupt: true);
                    return;
                }

                // During the tutorial the game ONLY lets you paint on its DESIGNATED tiles
                // (EditorGrid.HDENCBCBGCM — confirmed at EditorGrid.FBJHIJNIOGH:413). Our floor expander
                // may have grown beyond that, so a zone painted over the whole floor gets rejected tile
                // by tile. When the tutorial is active, paint over the tutorial's allowed area instead.
                var area = _placedFloorTiles;
                bool tut = false; try { tut = BuildingTutorialManager.IKNOJDMCFOK; } catch { }
                var allowed = TutorialAllowedTiles();
                if (tut && allowed != null && allowed.Count > 0)
                {
                    area = allowed;
                    DebugLogger.LogState($"[CTH] tutorial active: painting zone over {allowed.Count} tutorial-allowed tiles (not the {_placedFloorTiles.Count} floor tiles)");
                }

                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in area)
                {
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                }
                var start = new Vector3(minX, minY, 0f);
                var end = new Vector3(maxX, maxY, 0f);

                // The game's area-paint reads the ACTION BAR (currentPanel + selected slot), NOT the
                // manager property — confirmed by the log (panel=0/floor while we only set the property,
                // so it painted nothing). Select the ZONES panel (2) + the crafting-zone slot the way
                // the game does, so the drag actually paints a crafting zone. Keep the property set too.
                bool zoneSelected = SelectCraftingZoneInToolbar();
                try { TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP = EditorAction.CraftingZone; } catch { }
                if (!zoneSelected) DebugLogger.LogState("[CTH] WARNING: could not select crafting-zone slot in the zones panel");
                EditorAction action = EditorAction.None;
                try { action = TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP; } catch { }
                int panel = -1; try { panel = ConstructionActionBarUI.currentPanel; } catch { }
                bool tutOpen = false; try { tutOpen = BuildingTutorialManager.IsOpen(); } catch { }
                DebugLogger.LogState($"[CTH] TestPaintInjection action={action} panel={panel} tutorialOpen={tutOpen} start={start} end={end} tiles={_placedFloorTiles.Count}");

                // Where is the existing crafting zone vs our floor? (adjacency decides if the paint
                // can LINK/extend instead of creating a new zone, which the 1/1 limit forbids.)
                try
                {
                    var zones = TavernZonesManager.GGFJGHHHEJC.GetTavernZonesOfType(ZoneType.CraftingRoom);
                    if (zones != null && zones.Count > 0 && zones[0].positions != null && zones[0].positions.Count > 0)
                        DebugLogger.LogState($"[CTH] existing craftZone: zones={zones.Count} tiles={zones[0].positions.Count} firstPos={zones[0].positions[0]}");
                    else
                        DebugLogger.LogState("[CTH] existing craftZone: none");
                    if (_placedFloorTiles.Count > 0)
                        DebugLogger.LogState($"[CTH] floorFirst={_placedFloorTiles[0]} floorLast={_placedFloorTiles[_placedFloorTiles.Count - 1]}");
                }
                catch { }
                // Use the game's OWN zone-creation function directly (user's hunch — "vai ser change
                // zone mesmo"): EditorTileMaps.ChangeZone(action, positions, true) computes the ZoneType,
                // checks linking/breaking, then CreateTavernZone + AddTileToExistingZone. This bypasses
                // the injection drag AND the editor-tile path (both gave 0), and the tutorial paint
                // restriction. maxZones=2 now, so a 2nd crafting zone is allowed.
                ScreenReader.Say("Aplicando zona de produção.", interrupt: true);
                bool zoneOk = false;
                try { zoneOk = EditorTileMaps.ChangeZone(EditorAction.CraftingZone, new System.Collections.Generic.List<Vector2>(area), true); }
                catch (Exception e) { MelonLoader.MelonLogger.Error($"ChangeZone: {e}"); }
                DebugLogger.LogState($"[CTH] ChangeZone returned {zoneOk} for {area.Count} tiles");

                bool created = false;
                foreach (var t in area)
                {
                    try { if ((WorldGrid.AGKGGAFFFGM(new Vector3(t.x, t.y, 0f)) & ZoneType.CraftingRoom) != 0) { created = true; break; } }
                    catch { }
                }
                int zoneTiles = 0, dispTiles = 0;
                try { foreach (var kv in EditorTileMaps.editorTiles) { if (kv.Value.editorAction == EditorAction.CraftingZone) zoneTiles++; else if (kv.Value.editorAction == EditorAction.ZoneDisponible) dispTiles++; } } catch { }
                int curZ = -1, maxZ = -1;
                try { curZ = TavernZonesManager.GGFJGHHHEJC.GetCurrentNumberOfZones(ZoneType.CraftingRoom); } catch { }
                try { maxZ = ReputationDBAccessor.GetMaxNumOfZones(ZoneType.CraftingRoom); } catch { }
                DebugLogger.LogState($"[CTH] ChangeZone result: created={created} editorCraftTiles={zoneTiles} editorDispTiles={dispTiles} curZones={curZ} maxZones={maxZ}");
                ScreenReader.Say(created ? "Zona de produção criada!" : "Ainda não aplicou. Vou ver o log.", interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"TestPaintInjection: {ex}");
                ScreenReader.Say("Erro no teste de pintura.", interrupt: true);
            }
        }

        public static void Update()
        {
            if (!TavernConstructionUI.IsWindowOpen()) return;

            var bar = ConstructionActionBarUI.GetInstance();
            if (bar == null) return;

            int slotCount = GetPanelActionCount(bar);

            if (slotCount > 0)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    int next = (ConstructionActionBarUI.currentSlotSelected + 1) % slotCount;
                    try { ConstructionActionBarUI.SetCurrentSlotSelected(next); } catch { }
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    int prev = (ConstructionActionBarUI.currentSlotSelected - 1 + slotCount) % slotCount;
                    try { ConstructionActionBarUI.SetCurrentSlotSelected(prev); } catch { }
                }
            }

            // Tab = announce the current tutorial objective (moved off Up arrow per user request).
            if (Input.GetKeyDown(KeyCode.Tab) && BuildingTutorialManager.IKNOJDMCFOK)
            {
                if (GetCurrentGoal(out int gi, out var g))
                    ScreenReader.Say($"Objetivo atual: {GoalName(g)}. Use F8 para avançar.", interrupt: true);
                else
                    ScreenReader.Say("Sem objetivo pendente. Aguarde o tutorial avançar.", interrupt: true);
            }

            // UpArrow = Aceitar (AcceptChanges) — ONLY outside the tutorial. During the tutorial Up
            // does nothing here (objective is on Tab; F8 advances).
            if (Input.GetKeyDown(KeyCode.UpArrow) && !BuildingTutorialManager.IKNOJDMCFOK)
            {
                try
                {
                    var ui = TavernConstructionUI.GetInstance();
                    if (ui != null)
                    {
                        TavernConstructionUI.BBHJJDPJKFH();
                        var acceptBtn = AccessTools.Field(typeof(TavernConstructionUI), "acceptButton")?.GetValue(ui) as UnityEngine.UI.Button;
                        var acceptText = AccessTools.Field(typeof(TavernConstructionUI), "acceptButtonText")?.GetValue(ui) as TMPro.TextMeshProUGUI;
                        string label = acceptText?.text ?? "Aceitar";
                        if (acceptBtn != null && acceptBtn.interactable)
                        {
                            ScreenReader.Say(label, interrupt: true);
                            ui.AcceptChanges();
                        }
                        else
                            ScreenReader.Say($"{label} não disponível", interrupt: true);
                    }
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Error($"CTH UpArrow: {ex}");
                }
            }

            // DownArrow = Cancelar (RevertModifications)
            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                try
                {
                    var ui = TavernConstructionUI.GetInstance();
                    if (ui != null)
                    {
                        // Cancel is intentionally gated by !IKNOJDMCFOK inside RevertModifications.
                        if (BuildingTutorialManager.IKNOJDMCFOK)
                        {
                            ScreenReader.Say("Cancelar bloqueado durante tutorial.", interrupt: true);
                        }
                        else
                        {
                            var cancelBtn = AccessTools.Field(typeof(TavernConstructionUI), "cancelButton")?.GetValue(ui) as UnityEngine.UI.Button;
                            if (cancelBtn != null && cancelBtn.gameObject.activeSelf)
                            {
                                ScreenReader.Say("Cancelar", interrupt: true);
                                ui.RevertModifications();
                            }
                            else
                                ScreenReader.Say("Cancelar não disponível", interrupt: true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLoader.MelonLogger.Error($"CTH DownArrow: {ex}");
                }
            }

            // [ : cycle floors
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                var floors = ConstructionFloors.GGFJGHHHEJC;
                if (floors != null)
                {
                    TavernFloor next;
                    switch (floors.ODFBDBLCFOM)
                    {
                        case TavernFloor.Cellar: next = TavernFloor.FirstFloor; break;
                        case TavernFloor.FirstFloor: next = TavernFloor.SecondFloor; break;
                        default: next = TavernFloor.Cellar; break;
                    }
                    try { floors.SetFloor((int)next); } catch { }
                }
            }

            // ] / [ : navigate construction TABS (guias: Construir, Personalizar, Zonas, Portas).
            // Left/Right arrows: options within the current tab. (User request round 263.)
            if (Input.GetKeyDown(KeyCode.RightBracket)) ChangeToolbarPanel(+1);
            if (Input.GetKeyDown(KeyCode.LeftBracket)) ChangeToolbarPanel(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) ChangeToolbarSlot(+1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) ChangeToolbarSlot(-1);

            // J : announce current column
            if (Input.GetKeyDown(KeyCode.J) && _lastCursorTilePos.x != float.PositiveInfinity)
            {
                int col = Mathf.RoundToInt((_lastCursorTilePos.x - _originCursorTilePos.x) * 2f);
                ScreenReader.Say($"Coluna {col}", interrupt: true);
            }

            // Shift+K : auto-place all highlighted tiles; K alone : announce current row
            if (Input.GetKeyDown(KeyCode.K))
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    AutoPlaceAll();
                else if (_lastCursorTilePos.y != float.PositiveInfinity)
                {
                    int row = Mathf.RoundToInt((_lastCursorTilePos.y - _originCursorTilePos.y) * 2f);
                    ScreenReader.Say($"Linha {row}", interrupt: true);
                }
            }

            // L : announce bounding box of highlighted area
            if (Input.GetKeyDown(KeyCode.L))
                AnnounceHighlightedArea();

            // F8 : advance the building tutorial one step (camera goals auto-complete,
            // AddFloor does real placement, other goals are announced).
            if (Input.GetKeyDown(KeyCode.F8))
                AdvanceTutorialStep();

            // F9 : EXPERIMENT — paint the currently selected action (e.g. crafting zone) over the
            // bounding box of the placed floor, by driving the game's OWN paint flow via simulated
            // Interact input. This uses the game's real editor state, so it should not crash like the
            // direct zone-function calls did. Requires the tutorial popup to be minimised.
            if (Input.GetKeyDown(KeyCode.F9))
                TestPaintInjection();

            // Advance the injection paint state machine (no-op when idle).
            ConstructionInputInjector.Tick();
            TickFloorExpansion();

            CheckCursorPosition();
        }
    }
}
