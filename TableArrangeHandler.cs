using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Alt+M: reorganize the tavern's tables and benches (user request: "organizar mesas e bancos,
    /// colocar mesas em lugares válidos, e associar bancos a elas").
    ///
    /// Phase 1 (this version) is deliberately NON-DESTRUCTIVE - it never moves any furniture:
    ///   1. For every loose bench (Seat.table == null) that is ALREADY sitting at a table, it runs
    ///      the game's OWN association (GetNeighbourTableAround / GetNeighbourTable) so the bench
    ///      counts as seated. This is exactly what the game does on placement - zero risk.
    ///   2. It reports the tavern's seating state (tables, valid/blocked seat slots, benches
    ///      associated vs still loose, free slots) both aloud and to the MelonLoader log.
    ///
    /// The report is written unconditionally (MelonLogger.Msg) so the NEXT phase - actually MOVING
    /// loose benches onto free slots and repositioning tables into valid spots - is built on the
    /// real scene layout instead of guesses. Moving furniture is destructive and the user is blind
    /// (can't visually undo), so that phase is added only after this pass is validated with a log.
    /// </summary>
    public class TableArrangeHandler
    {
        private static readonly System.Reflection.FieldInfo SeatingGroupsField =
            AccessTools.Field(typeof(Table), "seatingGroups");

        // Same tavern gate the drink handler uses (the player's own tavern).
        private static bool InTavern()
        {
            try { var p = PlayerController.GetPlayer(1); return p != null && p.LEOIMFNKFGA == Location.Tavern; }
            catch { return false; }
        }

        public void Update(bool anyUiOpen)
        {
            if (anyUiOpen) return;
            if (!InTavern()) return;
            // Inside decoration mode, Alt+M is the auto-arranger (moves a loose bench onto a slot,
            // handled by DecorationModeHandler). Outside it, Alt+M here just associates benches
            // already at tables + reports the layout. Never let both fire the same frame.
            try { var dm = DecorationMode.GetPlayer(1); if (dm != null && dm.DMBFKFLDDLH) return; } catch { }
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (alt && Input.GetKeyDown(KeyCode.M)) Arrange();
        }

        private void Arrange()
        {
            Table[] tables;
            Seat[] seats;
            try
            {
                tables = Object.FindObjectsByType<Table>(FindObjectsSortMode.None);
                seats = Object.FindObjectsByType<Seat>(FindObjectsSortMode.None);
            }
            catch
            {
                ScreenReader.Say("Não consegui ler as mesas.", interrupt: true);
                return;
            }

            // Step 1: associate every loose bench already sitting at a table (non-destructive).
            int associatedNow = 0;
            foreach (var seat in seats)
            {
                if (seat == null || seat.table != null) continue;
                bool before = seat.table != null;
                try { seat.GetNeighbourTableAround(); } catch { }
                try { if (seat.table == null) seat.GetNeighbourTable(); } catch { }
                if (!before && seat.table != null) associatedNow++;
            }

            // Step 2: report the seating state.
            int tableCount = 0, blockedSlotTables = 0, totalSlots = 0, blockedSlots = 0, occupiedSlots = 0, freeSlots = 0;
            foreach (var table in tables)
            {
                if (table == null) continue;
                tableCount++;
                var (total, blocked) = WorldNavigationHandler.CountBlockedSeatSlots(table);
                totalSlots += total;
                blockedSlots += blocked;
                if (blocked > 0) blockedSlotTables++;

                var groups = SeatingGroupsField.GetValue(table) as SeatingGroup[];
                int tOccupied = 0, tFree = 0;
                if (groups != null)
                {
                    foreach (var g in groups)
                    {
                        if (g == null || g.transform == null) continue;
                        bool occ = SlotOccupied(g, seats);
                        if (occ) tOccupied++;
                        else tFree++;
                    }
                }
                occupiedSlots += tOccupied;
                freeSlots += tFree;

                MelonLoader.MelonLogger.Msg(
                    $"TableArrange: mesa {WorldNavigationHandler.GetTableNumber(table)} pos={table.transform.position} " +
                    $"slots={total} bloqueados={blocked} ocupados={tOccupied} livres={tFree} " +
                    $"valida={SafeValid(table)}");
            }

            int looseBenches = 0;
            foreach (var seat in seats)
            {
                if (seat == null) continue;
                if (seat.table == null)
                {
                    looseBenches++;
                    Vector3 sp = seat.transform != null ? seat.transform.position : Vector3.zero;
                    MelonLoader.MelonLogger.Msg(
                        $"TableArrange: banco solto {WorldNavigationHandler.GetSeatNumber(seat)} pos={sp} " +
                        $"nome={(seat.gameObject != null ? seat.gameObject.name : "?")}");
                }
            }

            MelonLoader.MelonLogger.Msg(
                $"TableArrange: RESUMO mesas={tableCount} bancos={seats.Length} associadosAgora={associatedNow} " +
                $"bancosSoltos={looseBenches} slotsTotal={totalSlots} slotsLivres={freeSlots} " +
                $"slotsOcupados={occupiedSlots} slotsBloqueados={blockedSlots} mesasComVagaBloqueada={blockedSlotTables}");

            string msg = $"{tableCount} mesas, {seats.Length} bancos. ";
            if (associatedNow > 0) msg += $"{associatedNow} bancos associados agora. ";
            msg += $"{looseBenches} bancos soltos, {freeSlots} vagas livres.";
            if (blockedSlotTables > 0) msg += $" {blockedSlotTables} mesas com vagas bloqueadas.";
            ScreenReader.Say(msg, interrupt: true);
        }

        // A slot is occupied if a real (non-held) seat sits within 0.3u of it - the same test
        // FindNearestEmptySlot/GetEmptySeatSlots use (the game's `occupied` flag is never
        // maintained, confirmed in WorldNavigationHandler's notes).
        private static bool SlotOccupied(SeatingGroup slot, Seat[] seats)
        {
            GameObject heldNow = SelectObject.GetPlayer(1) != null ? SelectObject.GetPlayer(1).selectedGameObject : null;
            foreach (var seat in seats)
            {
                if (seat == null || seat.transform == null) continue;
                if (seat.placeable != null && seat.placeable.gameObject == heldNow) continue;
                if (Vector3.Distance(seat.transform.position, slot.transform.position) < 0.3f) return true;
            }
            return false;
        }

        private static string SafeValid(Table table)
        {
            try { return table.placeable != null ? table.placeable.IsObjectInValidLocation(false).ToString() : "?"; }
            catch { return "?"; }
        }
    }
}
