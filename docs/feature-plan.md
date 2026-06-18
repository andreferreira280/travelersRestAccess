# Feature Plan - TravellersRestAccess

Based on codebase analysis in `docs/game-api.md`. Order follows what a player encounters first in-game.

## Detailed Features (most important)

### 1. Mod Framework + Basic Announcement (Phase 2 prerequisite)
- **Goal:** Mod loads, Tolk/NVDA announces "Mod loaded", confirms MelonLoader + Harmony + Tolk all wired up correctly.
- **Classes:** None yet - just `Main.cs` entry point.
- **Dependencies:** None - this is the foundation everything else builds on.
- **Challenge:** Getting the `net472` build + `MelonGame("Louqou", "TravellersRest")` attribute exactly right (see `project_status.md`).

### 2. Title Screen / Main Menu Navigation
- **Goal:** Announce title screen on load, navigate "New Game" / "Continue" / "Options" / "Quit", announce save slots in `SaveUI`.
- **Classes:** `TitleScreen`, `SaveUI`, `SaveSlotUI`.
- **Harmony hooks:** `UIWindow.OnAnyUIOpen`/`OnAnyUIClose` to detect screen transitions.
- **Keys:** Arrow keys (navigation, already expected by screen reader users), Enter (confirm - already game-used for same purpose, no conflict).
- **Dependencies:** Feature 1.
- **Challenge:** TitleScreen doesn't appear to be a typical list-navigation menu - may need custom handling.

### 3. Tutorial Announcements (high priority - first real gameplay content)
- **Goal:** Announce tutorial popups (`NewTutorialManager.ShowPopUp`) and objectives (`UpdateObjectives`) as they appear; this is how a blind player learns every core mechanic.
- **Classes:** `NewTutorialManager` (primary, active system), `TutorialManager` (legacy, lower priority).
- **Harmony hooks:** Patch `NewTutorialManager.ShowPopUp(string)` and `ObjectivesUpdated` event.
- **Keys:** F1 (repeat current tutorial text/objective).
- **Dependencies:** Features 1-2.
- **Challenge:** Tutorial blocks several systems until unlocked (`SleepBlocked()`, `CrafterBlocked()`, etc.) - mod must not announce actions as available when tutorial-locked.

### 4. Core Gameplay Status + Navigation
- **Goal:** Announce player zone changes, basic movement feedback, current money/balance on demand.
- **Classes:** `PlayerController` (`OnZoneChanged`, `OnPlayerMoving`), `Money` (`GetBalance()`).
- **Keys:** F2 (announce status: zone, money, time of day via `WorldTime`).
- **Dependencies:** Feature 1.

### 5. Inventory / Action Bar
- **Goal:** Navigate and announce inventory slots, item names/counts, action bar quick-items.
- **Classes:** `InventoryUI`/`GameInventoryUI`, `PlayerInventory`, `Slot`, `Item`/`ItemInstance`.
- **Keys:** Reuse game's existing `OpenInventory` action; arrow keys to navigate slots once open.
- **Dependencies:** Features 1, 4.
- **Challenge:** Need consistent "position X of Y: item name, count" announcement pattern (see `ACCESSIBILITY_MODDING_GUIDE.md`).

## Rough Features (less detail, build later)

- **Crafting/Cooking (`GameCraftingUI`, `Crafter`, `Recipe`):** Announce recipe list, ingredients needed, crafting progress. Medium complexity. Depends on: Inventory (4).
- **Customer Service Loop (`Customer`, `CustomerOrder`, `Barworker`, `Tray`):** Announce waiting customers, their orders, serving actions. Complex - this is the core gameplay loop. Depends on: Inventory, Core gameplay.
- **Shop/Trading (`ShopBaseUI`, `Shop`, `Price`):** Announce items for sale, prices, basket contents. Medium complexity. Depends on: Inventory.
- **Dialogue/NPCs (`DialogueNPCBase`, Pixel Crushers Dialogue System):** Announce dialogue text and choices. Medium-complex (3rd-party plugin integration). Depends on: Core gameplay.
- **Quests (`Quest`, `ActiveQuest`, `QuestTooltipUI`):** Announce active quest objectives and progress. Simple-medium. Depends on: Core gameplay.
- **Map (`CityMapUI`):** Announce player location, navigate map. Medium complexity (spatial info needs careful linearization). Depends on: Core gameplay.
- **Settings/Options (`OptionsMenuUI`):** Navigate tabbed settings. Simple-medium. Depends on: Title screen pattern (2).
- **Staff Management (`HireStaffUI`, `Employee`):** Announce hireable staff, manage assignments. Medium. Depends on: Inventory/Money.
- **Construction/Building (`TavernConstructionUI`, `FarmConstructionManager`):** Placement-based building - likely the hardest feature for screen reader accessibility (spatial placement). Complex. Lower priority initially.
- **Crop/Farming:** Not yet analyzed in depth. Complexity TBD.
- **Encyclopedia (`EncyclopediaUI`):** Browse reference info. Simple.
- **Calendar/Postbox (`CalendarUI`, `PostboxUI`):** Simple info screens.

## Suggested Priority Order

1. Mod framework + "Mod loaded" announcement
2. Title screen / main menu
3. Tutorial announcements
4. Core gameplay status (zone, money, time)
5. Inventory navigation
6. Customer service loop (the actual core gameplay)
7. Crafting/cooking
8. Shop/trading, Dialogue, Quests (can be parallel/any order)
9. Map, Settings, Staff management
10. Construction/building, Farming, Encyclopedia, Calendar/Postbox (polish/later)

This order may change as we learn more during implementation.
