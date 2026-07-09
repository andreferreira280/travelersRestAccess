using System.Collections.Generic;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Keyboard drink-serving for BOTH the tavern and the banquet competition (user's design).
    ///   Alt + digit (1..9, 0)  once  -> announce that dispenser and the drink it pours
    ///   Alt + digit           twice  -> pour that drink onto the player's tray
    ///   X                            -> serve the LAST tray drink to the customer who wants it
    ///   F3                           -> read the order queue ("pedido 1: Lager, pedido 2: água...")
    ///
    /// The dispenser list is built DYNAMICALLY from whatever exists right now (up to 10, Alt 1..0):
    /// the tavern's beer taps (Bar.beerTaps[i].drinkDispenser) and the banquet barrels
    /// (BanquetDrinksManager.banquetBarrels) - so adding recipients or changing a drink is picked up
    /// automatically. Pour reuses DrinkDispenser.FinishPull (adds to PlayerController.trayHandler.tray);
    /// serve reuses Customer/BanquetCustomer.ServeCustomer, which takes the matching drink off the tray.
    /// </summary>
    public class DrinkServingHandler
    {
        private int _lastDigit = -1;
        private float _lastDigitTime = -999f;
        private const float DoubleTapWindow = 0.5f;

        // Only act while serving: in the player's tavern OR during the banquet competition. Outside
        // both, the Alt bar / X / F3 stay completely silent (user: "fora da taverna e da competição
        // não deve mencionar nada nem servir").
        private static bool InServingContext()
        {
            // BanquetDrinksManager is a PERSISTENT scene singleton (always non-null), so it can't
            // gate context. A banquet is really on only while BanquetOrdersManager.instance exists
            // (it's nulled when the banquet ends). Otherwise, serving happens in the tavern.
            try
            {
                if (BanquetOrdersManager.instance != null) return true;
                var p = PlayerController.GetPlayer(1);
                return p != null && p.LEOIMFNKFGA == Location.Tavern;
            }
            catch { return false; }
        }

        public void Update()
        {
            if (!InServingContext()) return;

            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (alt)
            {
                int digit = -1;
                for (int d = 0; d <= 9; d++)
                    if (Input.GetKeyDown(KeyCode.Alpha0 + d) || Input.GetKeyDown(KeyCode.Keypad0 + d)) { digit = d; break; }
                if (digit >= 0)
                {
                    int index = digit == 0 ? 9 : digit - 1;   // 1..9 -> 0..8, 0 -> 9
                    bool doubleTap = _lastDigit == digit && Time.unscaledTime - _lastDigitTime < DoubleTapWindow;
                    if (doubleTap) { _lastDigit = -1; _lastDigitTime = -999f; PourToTray(index, digit); }
                    else { _lastDigit = digit; _lastDigitTime = Time.unscaledTime; AnnounceDispenser(index, digit); }
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.X)) ServeLast();
            if (Input.GetKeyDown(KeyCode.F3)) AnnounceOrderQueue();
        }

        // ---- Dispensers (dynamic; tavern taps + banquet barrels; capped at 10) ----
        private class Disp { public string drink; public System.Func<bool> pour; }

        private static bool BanquetActive()
        {
            try { return BanquetOrdersManager.instance != null; } catch { return false; }
        }

        private List<Disp> BuildDispensers()
        {
            var list = new List<Disp>();

            // During a banquet, use ONLY the banquet barrels. Otherwise (tavern) use ONLY the bar's
            // serving taps. Before, both were merged - and BanquetDrinksManager is a persistent scene
            // singleton, so its barrels leaked into the tavern list ("recipientes que não tenho").
            if (BanquetActive())
            {
                try
                {
                    var bdm = BanquetDrinksManager.instance;
                    if (bdm != null && bdm.banquetBarrels != null)
                        foreach (var b in bdm.banquetBarrels)
                        {
                            if (b == null) continue;
                            string drink = SlotDrink(b.slots);
                            if (Main.DebugMode) DebugLogger.LogState($"DrinkServing barrel candidate: drink={drink}");
                            if (string.IsNullOrEmpty(drink)) continue;
                            var bb = b;
                            list.Add(new Disp { drink = drink, pour = () => { DrinkDispenser.FinishPull(1, bb.slots[0], bb.work, PFFAMHBDDMA: false); return true; } });
                            if (list.Count >= 10) return list;
                        }
                }
                catch { }
                return list;
            }

            try
            {
                var ddm = DrinkDispensersManager.GGFJGHHHEJC;
                if (ddm != null && ddm.allDrinkDispensers != null)
                    foreach (var dd in ddm.allDrinkDispensers)
                    {
                        if (dd == null) continue;
                        string cand = SlotDrink(dd.slots);
                        string pName = null;
                        try { pName = dd.placeable != null ? dd.placeable.gameObject.name : null; } catch { }
                        if (Main.DebugMode) DebugLogger.LogState($"DrinkServing tap candidate: isBeerTap={dd.isBeerTap} drink={cand} placeable={pName}");
                        if (!dd.isBeerTap) continue;                 // only the bar SERVING taps, not cellar/storage barrels
                        if (string.IsNullOrEmpty(cand)) continue;    // skip empty
                        var d = dd;
                        list.Add(new Disp { drink = cand, pour = () => { DrinkDispenser.FinishPull(1, d.slots[0], d.work, PFFAMHBDDMA: false, d); return true; } });
                        if (list.Count >= 10) return list;
                    }
            }
            catch { }
            return list;
        }

        private static string SlotDrink(Slot[] slots)
        {
            try
            {
                var inst = slots != null && slots.Length > 0 ? slots[0].itemInstance : null;
                var it = inst != null ? inst.LHBPOPOIFLE() : null;
                return it != null ? it.IABAKHPEOAF() : null;
            }
            catch { return null; }
        }

        private void AnnounceDispenser(int index, int digit)
        {
            var disps = BuildDispensers();
            if (index >= disps.Count) { ScreenReader.Say($"Não tem dispensador {digit} aqui", interrupt: true); return; }
            var d = disps[index];
            ScreenReader.Say($"Dispensador {digit}: {(string.IsNullOrEmpty(d.drink) ? "vazio" : d.drink)}. Alt {digit} de novo pra servir na bandeja.", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: announce disp {digit} drink={d.drink} (total={disps.Count})");
        }

        private void PourToTray(int index, int digit)
        {
            var disps = BuildDispensers();
            if (index >= disps.Count) { ScreenReader.Say($"Não tem dispensador {digit} aqui", interrupt: true); return; }
            var d = disps[index];
            int before = TrayCount();
            try { d.pour(); } catch (System.Exception e) { if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: pour error {e.Message}"); }
            int after = TrayCount();
            if (after > before) ScreenReader.Say($"{(string.IsNullOrEmpty(d.drink) ? "Bebida" : d.drink)} na bandeja. {after} na bandeja.", interrupt: true);
            else ScreenReader.Say("Não consegui pôr na bandeja. Bandeja cheia?", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: pour {digit} tray {before}->{after}");
        }

        // ---- Tray ----
        private static Tray GetTray() { try { return PlayerController.GetPlayer(1)?.trayHandler?.tray; } catch { return null; } }
        private static int TrayCount() { var t = GetTray(); return t != null && t.currentDrinks != null ? t.currentDrinks.Count : 0; }

        // ---- Order queue ----
        private static string ReqName(ItemInstance inst) { try { var it = inst != null ? inst.LHBPOPOIFLE() : null; return it != null ? it.IABAKHPEOAF() : "bebida"; } catch { return "bebida"; } }
        private static int ReqId(ItemInstance inst) { try { var it = inst != null ? inst.LHBPOPOIFLE() : null; return it != null ? it.JDJGFAACPFC() : -1; } catch { return -1; } }

        private void AnnounceOrderQueue()
        {
            var parts = new List<string>();
            int n = 0;
            try
            {
                var bar = Bar.instance;
                if (bar != null && bar.waitingAtBar != null)
                    foreach (var npc in bar.waitingAtBar)
                    {
                        if (npc == null || npc.customer == null || npc.customer.currentRequest == null) continue;
                        parts.Add($"pedido {++n}: {ReqName(npc.customer.currentRequest)}");
                    }
            }
            catch { }
            try
            {
                var bom = BanquetOrdersManager.instance;
                if (bom != null && bom.tableOrders != null)
                    foreach (var c in bom.tableOrders)
                    {
                        if (c == null || c.currentRequest == null) continue;
                        parts.Add($"pedido {++n}: {ReqName(c.currentRequest)}");
                    }
            }
            catch { }
            ScreenReader.Say(parts.Count == 0 ? "Nenhum pedido na fila" : string.Join(", ", parts), interrupt: true);
        }

        private void ServeLast()
        {
            var tray = GetTray();
            if (tray == null || tray.currentDrinks == null || tray.currentDrinks.Count == 0) { ScreenReader.Say("Bandeja vazia", interrupt: true); return; }
            var last = tray.currentDrinks[tray.currentDrinks.Count - 1];
            int lastId = ReqId(last);
            string drinkName = ReqName(last);

            // Tavern customer wanting it
            try
            {
                var bar = Bar.instance;
                if (bar != null && bar.waitingAtBar != null)
                    foreach (var npc in bar.waitingAtBar)
                    {
                        if (npc == null || npc.customer == null || npc.customer.currentRequest == null) continue;
                        if (ReqId(npc.customer.currentRequest) != lastId) continue;
                        bool ok = false;
                        try { ok = npc.customer.ServeCustomer(1, NLCDDFDGACP: true, DOGOFILIHPJ: tray, NAKCFGEAGHH: null); } catch (System.Exception e) { if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: tavern serve error {e.Message}"); }
                        ScreenReader.Say(ok ? $"Servido {drinkName} ao cliente" : $"Não consegui servir {drinkName}", interrupt: true);
                        return;
                    }
            }
            catch { }

            // Banquet customer wanting it
            try
            {
                var bom = BanquetOrdersManager.instance;
                if (bom != null && bom.tableOrders != null)
                    foreach (var c in bom.tableOrders)
                    {
                        if (c == null || c.currentRequest == null) continue;
                        if (ReqId(c.currentRequest) != lastId) continue;
                        bool ok = false;
                        try { ok = c.ServeCustomer(1, NLCDDFDGACP: true, DOGOFILIHPJ: tray); } catch (System.Exception e) { if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: banquet serve error {e.Message}"); }
                        ScreenReader.Say(ok ? $"Servido {drinkName} ao cliente" : $"Não consegui servir {drinkName}", interrupt: true);
                        return;
                    }
            }
            catch { }

            ScreenReader.Say($"Nenhum cliente quer {drinkName} agora", interrupt: true);
        }
    }
}
