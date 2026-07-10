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

            // Step 2: report the seating state. "bloqueado" now means ONLY a truly-blocked EMPTY slot
            // (empty + no floor, e.g. against a wall) - NOT an occupied slot. CountBlockedSeatSlots
            // counted every occupied slot as blocked (a bench makes the tile read "no floor"), which
            // falsely reported "2 mesas com vagas bloqueadas" when everything was actually seated.
            int tableCount = 0, blockedSlotTables = 0, totalSlots = 0, blockedSlots = 0, occupiedSlots = 0, freeSlots = 0;
            foreach (var table in tables)
            {
                if (table == null) continue;
                tableCount++;
                var (total, tOccupied, tFreeUsable, tFreeBlocked) = WorldNavigationHandler.CountSlotStates(table, seats);
                totalSlots += total;
                blockedSlots += tFreeBlocked;
                occupiedSlots += tOccupied;
                freeSlots += tFreeUsable;
                if (tFreeBlocked > 0) blockedSlotTables++;

                // Info-gathering (user: "vá juntando infos" pra montar a avaliação inteligente e o
                // guia manual): distance to the NEAREST other table - tells us how crowded/spread the
                // layout is, which is exactly what drives table-spreading + manual positioning advice.
                float nearestTableGap = float.MaxValue; Table nearestOther = null;
                foreach (var o in tables)
                {
                    if (o == null || o == table) continue;
                    float d = Vector3.Distance(table.transform.position, o.transform.position);
                    if (d < nearestTableGap) { nearestTableGap = d; nearestOther = o; }
                }
                string gapStr = nearestOther != null ? $"{nearestTableGap:F2} (mesa {WorldNavigationHandler.GetTableNumber(nearestOther)})" : "n/a";

                MelonLoader.MelonLogger.Msg(
                    $"TableArrange: mesa {WorldNavigationHandler.GetTableNumber(table)} pos={table.transform.position} " +
                    $"slots={total} ocupados={tOccupied} livres={tFreeUsable} bloqueadosVazios={tFreeBlocked} " +
                    $"valida={SafeValid(table)} vizinhaMaisPerto={gapStr}");
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
                $"slotsOcupados={occupiedSlots} slotsBloqueadosVazios={blockedSlots} mesasComVagaBloqueada={blockedSlotTables}");

            string msg;
            if (looseBenches == 0 && blockedSlots == 0)
            {
                // Everything seated and no wall-blocked empty slot - the good state.
                msg = $"Tudo organizado. {tableCount} mesas, {occupiedSlots} bancos assentados, {freeSlots} vagas livres.";
            }
            else
            {
                msg = $"{tableCount} mesas. {occupiedSlots} bancos assentados";
                if (associatedNow > 0) msg += $", {associatedNow} agora";
                if (looseBenches > 0) msg += $", {looseBenches} bancos soltos";
                msg += $", {freeSlots} vagas livres";
                if (blockedSlots > 0) msg += $", {blockedSlots} vagas bloqueadas por parede";
                msg += ".";
            }
            ScreenReader.Say(msg, interrupt: true);
        }

        private static string SafeValid(Table table)
        {
            try { return table.placeable != null ? table.placeable.IsObjectInValidLocation(false).ToString() : "?"; }
            catch { return "?"; }
        }
    }
}
