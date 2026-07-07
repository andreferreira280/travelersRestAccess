using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Accessibility for the FuelUI window - the FUEL side of Crafter stations (forno / máquina de
    /// malte / tanque de fermentação). Confirmed via the live log: these stations open FuelUI ALONE
    /// (openWindows=[FuelUI]), which the rest of the mod doesn't treat as a station, so the blind
    /// user had no way to add fuel (unlike the lareira/FireplaceUI, which already works).
    ///
    /// Unlike the fireplace (a container you Ctrl+Enter fuel INTO), FuelUI has its OWN fuel slots
    /// (FuelElementUI) and adds fuel by "clicking" a slot, which pulls one unit of that fuel from
    /// the crafting inventory. This drives the firewood slot directly through the game's own
    /// FuelElementUI.FuelClicked() (real method, not one of the obfuscator's decoy clones), so
    /// amounts, sounds and inventory limits behave exactly like a real mouse click.
    ///
    /// Kept ENTIRELY SEPARATE from the station-detection in KeyboardUINavigator/InventoryTransfer
    /// on purpose: the earlier FuelUI attempts that hooked into that path scrambled the hotbar and
    /// were reverted (commit 0df9b0a). Detection here is FuelUI.IsOpen() (NOT activeInHierarchy -
    /// placed Crafters stay active in the hierarchy, which was the exact bug), so it only ever acts
    /// while the window is really open.
    /// </summary>
    public class FuelStationHandler
    {
        // FuelUI's private reference to the Crafter it's showing - the reliable way to read the
        // current fuel amount (Crafter.LCCABPFHCOL) to confirm a successful add.
        private static readonly FieldInfo CrafterField = AccessTools.Field(typeof(FuelUI), "LDLINOBIKPL");

        private bool _wasOpen;

        public void Update()
        {
            FuelUI fuel = null;
            try { fuel = FuelUI.Get(1); } catch { }
            bool open = fuel != null && fuel.IsOpen();

            if (open != _wasOpen)
            {
                _wasOpen = open;
                if (open) AnnounceOpen(fuel);
            }
            if (!open) return;

            // Ctrl+Enter = ADICIONA, matching the project-wide station convention. Reusing the
            // game's own add-fuel action means we never re-implement the fuel/inventory math.
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                AddFirewood(fuel);
            }
        }

        private static Crafter GetCrafter(FuelUI fuel)
        {
            return CrafterField != null ? CrafterField.GetValue(fuel) as Crafter : null;
        }

        private void AnnounceOpen(FuelUI fuel)
        {
            string name = fuel.crafterName != null && !string.IsNullOrEmpty(fuel.crafterName.text)
                ? fuel.crafterName.text
                : "Estação";
            var crafter = GetCrafter(fuel);
            string amountPart = crafter != null ? $"{crafter.LCCABPFHCOL} de combustível. " : "";
            ScreenReader.Say($"Combustível de {name}. {amountPart}Ctrl+Enter pra adicionar lenha.", interrupt: true);
            if (Main.DebugMode) DebugLogger.LogState($"FuelStation: opened for \"{name}\", fuel={(crafter != null ? crafter.LCCABPFHCOL.ToString() : "?")}");
        }

        private void AddFirewood(FuelUI fuel)
        {
            var crafter = GetCrafter(fuel);
            int before = crafter != null ? crafter.LCCABPFHCOL : -1;

            var slot = fuel.GetLogFuelSlot();
            if (slot == null)
            {
                ScreenReader.Say("Sem slot de lenha nesta estação", interrupt: true);
                return;
            }

            try { slot.FuelClicked(); }
            catch (System.Exception e)
            {
                if (Main.DebugMode) DebugLogger.LogState($"FuelStation: FuelClicked error {e.Message}");
            }

            int after = crafter != null ? crafter.LCCABPFHCOL : -1;
            if (crafter == null)
                ScreenReader.Say("Lenha adicionada", interrupt: true);
            else if (after > before)
                ScreenReader.Say($"Lenha adicionada. {after} de combustível.", interrupt: true);
            else
                ScreenReader.Say("Sem lenha no inventário", interrupt: true);

            if (Main.DebugMode) DebugLogger.LogState($"FuelStation: add firewood fuel {before} -> {after}");
        }
    }
}
