using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Keyboard drink-serving for the banquet competition (user's design, round 247):
    ///   Alt + digit (1..9, 0)  once  -> announce that dispenser/barrel and the drink it pours
    ///   Alt + digit           twice  -> pour that drink onto the player's tray
    ///   X                            -> serve the LAST drink on the tray to the customer who wants it
    /// The player does NOT walk to the customer (the "Servir" nav category stays only for tracking).
    ///
    /// Barrels: BanquetDrinksManager.instance.banquetBarrels. Pour reuses the game's own
    /// DrinkDispenser.FinishPull (adds to PlayerController.trayHandler.tray). Serve reuses
    /// BanquetCustomer.ServeCustomer(playerNum, true, tray), which takes the matching drink off the
    /// tray. Digit->barrel: 1..9 = barrels 0..8, 0 = barrel 9.
    /// </summary>
    public class DrinkServingHandler
    {
        private int _lastDigit = -1;
        private float _lastDigitTime = -999f;
        private const float DoubleTapWindow = 0.5f;

        public void Update()
        {
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
                    if (doubleTap)
                    {
                        _lastDigit = -1;   // reset so a 3rd tap announces again instead of chain-pouring
                        _lastDigitTime = -999f;
                        PourToTray(index);
                    }
                    else
                    {
                        _lastDigit = digit;
                        _lastDigitTime = Time.unscaledTime;
                        AnnounceBarrel(index, digit);
                    }
                    return;
                }
            }

            if (Input.GetKeyDown(KeyCode.X)) ServeLast();
        }

        private static BanquetBarrel GetBarrel(int index)
        {
            var mgr = BanquetDrinksManager.instance;
            if (mgr == null || mgr.banquetBarrels == null) return null;
            if (index < 0 || index >= mgr.banquetBarrels.Length) return null;
            return mgr.banquetBarrels[index];
        }

        // The drink a barrel pours = its slot[0] item.
        private static string BarrelDrinkName(BanquetBarrel b)
        {
            try
            {
                var inst = b.slots != null && b.slots.Length > 0 ? b.slots[0].itemInstance : null;
                var item = inst != null ? inst.LHBPOPOIFLE() : null;
                return item != null ? item.IABAKHPEOAF() : null;
            }
            catch { return null; }
        }

        private void AnnounceBarrel(int index, int digit)
        {
            var b = GetBarrel(index);
            if (b == null) { ScreenReader.Say($"Barril {digit} não existe aqui", interrupt: true); return; }
            string drink = BarrelDrinkName(b);
            ScreenReader.Say($"Barril {digit}: {(string.IsNullOrEmpty(drink) ? "vazio" : drink)}. Alt {digit} de novo pra servir na bandeja.", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: announce barrel index={index} id={b.drinkDispenserId} drink={drink}");
        }

        private void PourToTray(int index)
        {
            var b = GetBarrel(index);
            if (b == null) { ScreenReader.Say("Barril não existe aqui", interrupt: true); return; }
            string drink = BarrelDrinkName(b);
            int before = TrayCount();
            try { DrinkDispenser.FinishPull(1, b.slots[0], b.work, PFFAMHBDDMA: false); }
            catch (System.Exception e) { if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: FinishPull error {e.Message}"); }
            int after = TrayCount();
            if (after > before)
                ScreenReader.Say($"{(string.IsNullOrEmpty(drink) ? "Bebida" : drink)} na bandeja. {after} na bandeja.", interrupt: true);
            else
                ScreenReader.Say("Não consegui servir na bandeja. Bandeja cheia?", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: pour index={index} tray {before}->{after}");
        }

        private static Tray GetTray()
        {
            try { return PlayerController.GetPlayer(1)?.trayHandler?.tray; } catch { return null; }
        }

        private static int TrayCount()
        {
            var t = GetTray();
            return t != null && t.currentDrinks != null ? t.currentDrinks.Count : 0;
        }

        private void ServeLast()
        {
            var tray = GetTray();
            if (tray == null || tray.currentDrinks == null || tray.currentDrinks.Count == 0)
            {
                ScreenReader.Say("Bandeja vazia", interrupt: true);
                return;
            }

            var last = tray.currentDrinks[tray.currentDrinks.Count - 1];
            int lastId = -1;
            try { var it = last != null ? last.LHBPOPOIFLE() : null; if (it != null) lastId = it.JDJGFAACPFC(); } catch { }

            // Find the waiting banquet customer who wants that drink and serve them.
            BanquetCustomer target = null;
            try
            {
                var bom = BanquetOrdersManager.instance;
                if (bom != null && bom.tableOrders != null)
                {
                    foreach (var c in bom.tableOrders)
                    {
                        if (c == null || c.currentRequest == null) continue;
                        int wantId = -1;
                        try { var wi = c.currentRequest.LHBPOPOIFLE(); if (wi != null) wantId = wi.JDJGFAACPFC(); } catch { }
                        if (wantId == lastId) { target = c; break; }
                    }
                }
            }
            catch { }

            string drinkName = null;
            try { var it = last != null ? last.LHBPOPOIFLE() : null; drinkName = it != null ? it.IABAKHPEOAF() : null; } catch { }
            if (string.IsNullOrEmpty(drinkName)) drinkName = "bebida";

            if (target == null)
            {
                ScreenReader.Say($"Nenhum cliente quer {drinkName} agora", interrupt: true);
                return;
            }

            bool ok = false;
            try { ok = target.ServeCustomer(1, NLCDDFDGACP: true, DOGOFILIHPJ: tray); }
            catch (System.Exception e) { if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: ServeCustomer error {e.Message}"); }
            ScreenReader.Say(ok ? $"Servido {drinkName} ao cliente" : $"Não consegui servir {drinkName}", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"DrinkServing: serve drink={drinkName} ok={ok}");
        }
    }
}
