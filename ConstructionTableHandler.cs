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
            try
            {
                var mgr = TavernConstructionManager.GGFJGHHHEJC;
                if (mgr == null) { ScreenReader.Say("Mesa de construção não ativa.", interrupt: true); return false; }

                if (mgr.CHFHMMNELGP != EditorAction.AddFloor || ConstructionActionBarUI.currentPanel != 0)
                {
                    ScreenReader.Say("Selecione 'Adicionar piso' primeiro.", interrupt: true);
                    return false;
                }

                // Collect available (green) tile positions — copy before modifying
                var disponible = new List<Vector3>();
                foreach (var kv in EditorTileMaps.editorTiles)
                {
                    if (kv.Value.editorAction == EditorAction.AddFloorDisponible)
                        disponible.Add(kv.Key);
                }

                if (disponible.Count == 0)
                {
                    ScreenReader.Say("Nenhuma área disponível para construir.", interrupt: true);
                    return false;
                }

                // Ensure _decorationTile is set
                var decorField = AccessTools.Field(typeof(TavernConstructionManager), "_decorationTile");
                var decorTile = decorField?.GetValue(mgr) as DecorationTile;
                if (decorTile == null)
                {
                    decorTile = Utils.KMJAGBLPODO(ConstructionFloors.GGFJGHHHEJC?.ODFBDBLCFOM ?? TavernFloor.FirstFloor);
                    decorField?.SetValue(mgr, decorTile);
                }

                // Convert each disponible position to AddFloor with 4 sub-tile entries
                var subOffsets = new Vector3[] {
                    Vector3.zero,
                    new Vector3(0.5f, 0f, 0f),
                    new Vector3(0f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f)
                };
                foreach (var pos in disponible)
                    foreach (var off in subOffsets)
                    {
                        var sub = pos + off;
                        EditorGrid.KICMMMBCPNF(sub, EditorAction.AddFloor, decorTile);
                        _placedFloorTiles.Add(new Vector2(sub.x, sub.y));
                    }

                // floorEditorTiles = number of real tiles (not sub-tiles)
                var floorField = AccessTools.Field(typeof(TavernConstructionManager), "floorEditorTiles");
                floorField?.SetValue(mgr, disponible.Count);

                mgr.ApplyEditorChanges();

                // Refresh button interactability immediately; normally only updated on panel changes.
                TavernConstructionUI.BBHJJDPJKFH();

                ScreenReader.Say($"{disponible.Count} pisos colocados.", interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                ScreenReader.Say("Erro ao colocar piso.", interrupt: true);
                MelonLoader.MelonLogger.Error($"AutoPlaceAll: {ex}");
                return false;
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
                        // Real construction: place the highlighted floor tiles.
                        if (AutoPlaceAll())
                            BuildingTutorialManager.GoalCompleted(idx);
                        // AutoPlaceAll already announces success/why-not.
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

                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in _placedFloorTiles)
                {
                    if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                }
                var start = new Vector3(minX, minY, 0f);
                var end = new Vector3(maxX, maxY, 0f);

                // Select the crafting-zone action first (last test showed action=None). Setting the
                // manager's property is the game's own selection path (fires OnEditorActionChanged).
                try { TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP = EditorAction.CraftingZone; } catch { }
                EditorAction action = EditorAction.None;
                try { action = TavernConstructionManager.GGFJGHHHEJC.CHFHMMNELGP; } catch { }
                int panel = -1; try { panel = ConstructionActionBarUI.currentPanel; } catch { }
                bool tutOpen = false; try { tutOpen = BuildingTutorialManager.IsOpen(); } catch { }
                DebugLogger.LogState($"[CTH] TestPaintInjection action={action} panel={panel} tutorialOpen={tutOpen} start={start} end={end} tiles={_placedFloorTiles.Count}");
                ScreenReader.Say("Testando pintura por input simulado.", interrupt: true);
                ConstructionInputInjector.PaintArea(start, end, ok =>
                {
                    if (!ok) { ScreenReader.Say("Injetor ocupado.", interrupt: false); return; }
                    // Verify the crafting zone actually landed on the floor tiles.
                    bool created = false;
                    foreach (var p in _placedFloorTiles)
                    {
                        try { if ((WorldGrid.AGKGGAFFFGM(p) & ZoneType.CraftingRoom) != 0) { created = true; break; } }
                        catch { }
                    }
                    // Diagnostics: did the paint create editor zone tiles, and are we at the zone limit?
                    int zoneTiles = 0, dispTiles = 0;
                    try { foreach (var kv in EditorTileMaps.editorTiles) { if (kv.Value.editorAction == EditorAction.CraftingZone) zoneTiles++; else if (kv.Value.editorAction == EditorAction.ZoneDisponible) dispTiles++; } } catch { }
                    int cur = -1, max = -1;
                    try { cur = TavernZonesManager.GGFJGHHHEJC.GetCurrentNumberOfZones(ZoneType.CraftingRoom); } catch { }
                    try { max = ReputationDBAccessor.GetMaxNumOfZones(ZoneType.CraftingRoom); } catch { }
                    DebugLogger.LogState($"[CTH] paint result created={created} editorCraftTiles={zoneTiles} editorDispTiles={dispTiles} curZones={cur} maxZones={max}");
                    ScreenReader.Say(created ? "Zona de produção criada!" : "Pintura enviada, mas a zona não foi criada ainda.", interrupt: true);
                });
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

            // ] : cycle panels
            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                int next = (ConstructionActionBarUI.currentPanel + 1) % 4;
                try { ConstructionActionBarUI.FocusMainPanel(next); } catch { }
            }

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

            CheckCursorPosition();
        }
    }
}
