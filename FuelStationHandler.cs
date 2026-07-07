using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Accessible fuel menu for Crafter stations (forno / máquina de malte / tanque de fermentação),
    /// which open the FuelUI window. Rebuilds the exact menu the user described from the old build:
    /// NO right-side inventory panel - just the list of every fuel TYPE the station accepts, how
    /// much of each the player has available to add, and how much fuel is already loaded.
    ///
    /// FuelUI already renders this (its own fuel slots: firewoodSlot + fuelSlotUIs, each a
    /// FuelElementUI : SlotUIRecipe). We voice those slots and add fuel through the game's own
    /// FuelElementUI.FuelClicked() (real method, not an obfuscator decoy), so amounts/sounds/limits
    /// behave exactly like a mouse click. The generic KeyboardUINavigator is told to skip FuelUI
    /// (see the guard in KeyboardUINavigator.Update) so the two don't both act on the arrow keys.
    /// Detection is FuelUI.IsOpen() - never activeInHierarchy (placed Crafters stay active; that was
    /// the old hotbar-scrambling bug).
    /// </summary>
    public class FuelStationHandler
    {
        private static readonly FieldInfo CrafterField = AccessTools.Field(typeof(FuelUI), "LDLINOBIKPL");
        private static readonly FieldInfo FuelSlotsField = AccessTools.Field(typeof(FuelUI), "fuelSlotUIs");

        // An entry is either a fuel type (Slot != null) or an action button ("craft" / "back").
        private class Entry
        {
            public FuelElementUI Slot;
            public string Action;
        }

        private bool _wasOpen;
        private readonly List<Entry> _entries = new List<Entry>();
        private int _index;

        public void Update()
        {
            FuelUI fuel = null;
            try { fuel = FuelUI.Get(1); } catch { }
            bool open = fuel != null && fuel.IsOpen();

            if (open != _wasOpen)
            {
                _wasOpen = open;
                if (open)
                {
                    RebuildEntries(fuel);
                    _index = 0;
                    AnnounceOpen(fuel);
                }
                return;
            }
            if (!open) return;

            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.UpArrow))
            {
                RebuildEntries(fuel);
                if (_entries.Count == 0) { ScreenReader.Say("Nada pra colocar aqui", interrupt: true); return; }
                if (_index >= _entries.Count) _index = _entries.Count - 1;
                _index = Input.GetKeyDown(KeyCode.DownArrow)
                    ? (_index + 1) % _entries.Count
                    : (_index - 1 + _entries.Count) % _entries.Count;
                AnnounceEntry(fuel, _index);
                return;
            }

            // Ctrl+Enter ativa a entrada selecionada (adiciona o combustível, ou abre fabricar / volta).
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                ActivateEntry(fuel);
            }
        }

        private static Crafter GetCrafter(FuelUI fuel)
        {
            return CrafterField != null ? CrafterField.GetValue(fuel) as Crafter : null;
        }

        private static int LoadedFuel(FuelUI fuel)
        {
            var c = GetCrafter(fuel);
            return c != null ? c.LCCABPFHCOL : 0;
        }

        // Name of the fuel item a slot represents, or null if the slot is empty/inactive.
        private static string FuelName(FuelElementUI slot)
        {
            var item = slot != null && slot.IHENCGDNPBL != null && slot.IHENCGDNPBL.itemInstance != null
                ? slot.IHENCGDNPBL.itemInstance.LHBPOPOIFLE()
                : null;
            if (item == null) return null;
            string name = item.IABAKHPEOAF();
            return string.IsNullOrEmpty(name) ? "Combustível" : name;
        }

        private static int AvailableInInventory(FuelElementUI slot)
        {
            var item = slot != null && slot.IHENCGDNPBL != null && slot.IHENCGDNPBL.itemInstance != null
                ? slot.IHENCGDNPBL.itemInstance.LHBPOPOIFLE()
                : null;
            if (item == null) return 0;
            var pi = PlayerInventory.GetPlayer(1);
            return pi != null ? pi.NumberOfItems(item.JDJGFAACPFC()) : 0;
        }

        private void RebuildEntries(FuelUI fuel)
        {
            _entries.Clear();

            var slots = new List<FuelElementUI>();
            var arr = FuelSlotsField != null ? FuelSlotsField.GetValue(fuel) as FuelElementUI[] : null;
            if (arr != null) foreach (var s in arr) if (s != null) slots.Add(s);
            var firewood = fuel.GetLogFuelSlot();
            if (firewood != null) slots.Add(firewood);

            var seenNames = new HashSet<string>();
            foreach (var slot in slots)
            {
                if (slot == null || !slot.gameObject.activeInHierarchy) continue;
                string name = FuelName(slot);
                if (name == null) continue;
                if (!seenNames.Add(name)) continue; // firewoodSlot can duplicate a fuelSlotUIs entry
                _entries.Add(new Entry { Slot = slot });
            }

            // Actions the FuelUI itself offers, so the player isn't stuck in the list.
            var crafter = GetCrafter(fuel);
            if (crafter != null && crafter.CanOpenCraftingUI())
                _entries.Add(new Entry { Action = "craft" });
            _entries.Add(new Entry { Action = "back" });
        }

        private void AnnounceOpen(FuelUI fuel)
        {
            string name = fuel.crafterName != null && !string.IsNullOrEmpty(fuel.crafterName.text)
                ? fuel.crafterName.text
                : "Estação";
            ScreenReader.Say($"Combustível de {name}. {LoadedFuel(fuel)} colocado. Setas pra escolher, Ctrl+Enter pra adicionar.", interrupt: true);
            if (_entries.Count > 0) AnnounceEntry(fuel, 0);
            if (Main.DebugMode) DebugLogger.LogState($"FuelStation: opened \"{name}\", {_entries.Count} entries, loaded={LoadedFuel(fuel)}");
        }

        private void AnnounceEntry(FuelUI fuel, int i)
        {
            if (i < 0 || i >= _entries.Count) return;
            var e = _entries[i];
            string msg;
            if (e.Action == "craft") msg = "Fabricar";
            else if (e.Action == "back") msg = "Voltar";
            else msg = $"{FuelName(e.Slot)}, você tem {AvailableInInventory(e.Slot)}, {LoadedFuel(fuel)} colocado";
            ScreenReader.Say(msg, interrupt: true);
        }

        private void ActivateEntry(FuelUI fuel)
        {
            if (_index < 0 || _index >= _entries.Count) return;
            var e = _entries[_index];

            if (e.Action == "craft") { try { fuel.OpenCrafter(); } catch { } return; }
            if (e.Action == "back") { try { fuel.CloseUI(); } catch { } return; }

            // Fuel type: add one unit through the game's own slot click.
            string name = FuelName(e.Slot);
            if (AvailableInInventory(e.Slot) <= 0)
            {
                ScreenReader.Say($"Você não tem {name} no inventário", interrupt: true);
                return;
            }

            int before = LoadedFuel(fuel);
            try { e.Slot.FuelClicked(); }
            catch (System.Exception ex) { if (Main.DebugMode) DebugLogger.LogState($"FuelStation: FuelClicked error {ex.Message}"); }
            int after = LoadedFuel(fuel);

            if (after > before)
                ScreenReader.Say($"{name} adicionado. {after} colocado, {AvailableInInventory(e.Slot)} restante", interrupt: true);
            else
                ScreenReader.Say($"Não consegui adicionar {name}", interrupt: true);

            if (Main.DebugMode) DebugLogger.LogState($"FuelStation: add \"{name}\" fuel {before} -> {after}");
        }
    }
}
