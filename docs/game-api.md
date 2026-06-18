# Travellers Rest - Game API Documentation

## Overview

- **Game:** Travellers Rest (Early Access) - cozy tavern-management game
- **Engine:** Unity 2022.3.62f2
- **Runtime:** net35 (use `net472` TargetFramework)
- **Architecture:** 64-bit
- **Developer:** Louqou
- **No combat system** - purely management/cooking/serving gameplay
- **Multiplayer support** (Photon PUN2) pervasive - most systems have `OnlineX` counterparts
- **Important:** Many internal class members use compiler-obfuscated names (e.g. `GGFJGHHHEJC`, `IADEMLIIDPC`). These are real working symbols, just not human-readable - always verify exact names directly in `decompiled/` before using them in code, never guess.

---

## 1. Singleton Access Points

- **PlayerController** (`PlayerController.cs`) - per-player array (1-5 players), static accessor `PlayerController.GetPlayer(playerNum)`. Movement, speed, sprint, zone tracking.
- **PlayerInventory** (`PlayerInventory.cs`) - per-player inventory + action bar, `PlayerInventory.GetPlayer(playerNum)`.
- **Inventory** (`Inventory.cs`) - base inventory container, static instance + per-player array.
- **GameManager** (`GameManager.cs`) - scene loading, game state, coop.
- **CommonReferences** (`CommonReferences.cs`) - central hub to reach other managers; has events like `OnAnyQuestProgress`, `OnWaiterGoingToWork`, `OnBarOpenWithTrends`.
- **MainUI** (`MainUI.cs`, ISingleton) - root UI manager, tracks per-player window stacks (`LinkedList<UIWindow>`). Methods: `AddWindow`, `RemoveWindow`, `GetCurrentOpenWindows(playerNum)`, `IsAnyUIOpen()`, `CloseAllUIWindows()`.
- **PauseMenuUI** (`PauseMenuUI.cs`, ISingleton) - pause/settings.
- **WorldTime** (`WorldTime.cs`, ISingleton) - day/night cycle, `GetInstance()`.
- **Money** (`Money.cs`) - currency, `GetBalance()`, `ToCopper()`.
- **CraftingInventory**, **RecipesManager**, **ShopDatabaseAccessor**, **ItemDatabaseAccessor**, **CropDatabaseAccessor**, **MissionsDatabaseAccessor**, **FarmConstructionManager**, **ContentLocksManager** - all ISingleton, `GetInstance()` pattern, database/manager classes for their respective domains.
- **Barworker** (`Barworker.cs`) - serving staff singleton, `.GetInstance()`.
- **Bar** (`Bar.cs`) - tavern bar room, `static instance`, tracks `waitingAtBar` list.
- **TavernReputation** (ISingleton) - NPC relationship/reputation tracking.
- **CustomerPool** (ISingleton) - customer spawning/pooling.
- **TutorialManager** / **NewTutorialManager** - two parallel tutorial systems (see section 13).
- **LocalisationSystem** - custom localization wrapper around I2.Loc (see section 10).

---

## 2. Game Key Bindings (DO NOT override in mod!)

**CRITICAL ARCHITECTURE NOTE:** The game uses **Rewired** (3rd-party input remapping library), NOT plain Unity `Input` for core gameplay actions. Reference DLLs: `Rewired_Core.dll`, `Rewired_Windows.dll`. Main entry: `PlayerInputs.cs` wraps a Rewired `Player` object per player (up to 5 for local coop).

**Rewired Action Types** (`ActionType.cs` enum, ~50 actions, remappable by the player in-game so exact physical keys can vary):
- Gameplay: `Interact`, `Use`, `Select`, `Hold`, `MoveObject`, `PreviousItem`, `NextItem`, `Rotate`, `Style`, `ScrollUp`, `ScrollDown`, `SprintHoldAction`
- UI: `UIInteract`, `UIBack`, `UIAddRemove`, `UISelectGamepad`, `UISelectMouse`, `UIPreviousPage`, `UINextPage`, `UIPreviousCategory`, `UINextCategory`, `UIScrollUp`, `UIScrollDown`, `UIPlaceAll`
- Menus: `OpenInventory`, `OpenQuests`, `OpenTalents`, `OpenXPModifiers`, `OpenRecipeBook`, `Pause`, `BuildMode`, `OpenTavern`, `ClosePopUp`
- Movement: `HorizontalMove`, `VerticalMove`, `ObjectHorizontalMove`, `ObjectVerticalMove`, `Left`, `Right`, `Up`, `Down`, `WASD`, `Objective`, `ObjectMove`

**Hardcoded KeyCode usages found directly in code** (bypass Rewired, so these are fixed regardless of player rebinding):
- `Shift` (Left/Right) - item stack splitting modifier (heavily used: ActionBarUI, GameActionBarUI, GameInventoryUI, MouseSlot, ShopElementUI)
- `Q` - shop action (`ShopElementUI`)
- `Period` (.) - shop action (`ShopElementUI`)
- `@` (At, with Shift) - shop action (`ShopElementUI`)
- `Alpha6` - gameplay hotkey (`ActionBarUI`)
- `Return`/Enter - chat/dialogue confirm (`OnlineChatUI`)
- `Delete`, `}`, `,` - inventory actions (`MouseSlot`)
- Several numeric/negative `(KeyCode)N` values in `InteractObject`, `ShopElementUI`, `MouseSlot`, `OnlineChatUI` - likely platform-specific or controller-related codes, not standard letter keys; treat with caution, re-verify in source before assuming free.

**Mouse:** `Input.GetMouseButton(0/1/2)` used in a few non-core UI helpers (chat resize). Pointer events (`OnPointerEnter`, `OnPointerClick`) used pervasively for UI hover/tooltips/clicks.

---

## 3. Safe Keys for Mod

Not used anywhere in decompiled code - safe to claim for accessibility mod shortcuts:

- **F1-F12** - all free (top recommendation: F1 = help, F2+ = context announcements)
- **Numpad 0-9**, NumpadEnter, NumpadPlus/Minus/Multiply/Divide/Period
- **Number row** mostly free (caution: `Alpha6` is used)
- Punctuation: `;` `'` `\` `/` `-` `=` backtick - mostly free (caution: `.` `,` `@` are used)
- `Alt` (Left/Right), CapsLock, ScrollLock, Pause/Break
- Arrow keys, Home, End, PageUp, PageDown - free in core gameplay (but used for standard UI/list navigation, which is expected/desired)

**Avoid:** Shift, Enter/Return, Delete, Q, comma, period, @ - confirmed used by the game.

**Reminder:** because gameplay actions go through Rewired and are player-remappable, always re ‑verify a specific key isn't currently bound before relying on it being silent; the list above reflects hardcoded `KeyCode` usage found in source, not the full remappable action set.

---

## 4. UI System

### Base architecture
- **`UIWindow`** - base class for all windows. Methods: `OpenUI()`, `CloseUI()`, `ToggleUI()`, `IsOpen()`. Static events: `OnAnyUIOpen(int playerNum, UIWindow window)`, `OnAnyUIClose(int playerNum, UIWindow window)` - **best universal Harmony patch point for detecting any UI open/close**.
- **`EWindow`** enum - window category: `Main` (closes other Main windows), `Concatenated`/`ConcatenatedOpened` (stacking popups), `Disabled` (untracked), `Permanent` (can't close).
- **`MainPanelWindow`** extends `UIWindow` - tabbed main panels.
- **`UISoloWindow`** extends `UIWindow` - only one instance open at a time.
- **`FadingWindow`** extends `UIWindow` - CanvasGroup alpha fade.
- **`MainUI`** - central manager, per-player `LinkedList<UIWindow>` window stacks.

### Key windows (67+ `UIWindow` subclasses total)
- **TitleScreen** - main menu/title.
- **PauseMenuUI** (ISingleton) - pause/settings.
- **OptionsMenuUI** - tabbed settings, similar structure to MainPanelUI.
- **InventoryUI** / **GameInventoryUI** (MainPanelWindow) - slot-grid inventory.
- **GameCraftingUI** → **CraftingUI** → **MainPanelWindow** - recipe list, crafter name, events `OnCraftingUIOpen`/`OnCraftingUIClose`.
- **ShopBaseUI** (abstract) → `ShopUI`, `AnimalShopUI`, `FerroShopUI` - item list + basket, event `OnItemsBought(playerNum, List<BasketItem>)`.
- **YesNoDialogueUI** - yes/no confirm dialog, `Open(playerNum)`, gamepad-aware.
- **MultipleChoiceUI** - up to 4 extra choices plus yes/no.
- **CityMapUI** - map with scrollbar, per-player markers.
- **EncyclopediaUI** - tabbed sections with scrollable text.
- **SaveUI** - save slots list, new game flow.
- **HireStaffUI**, **RentRoomUI** (UISoloWindow), **CalendarUI**, **PostboxUI**, **QuestTooltipUI**, **CharacterCreatorUI**, **ChallengesUI**, **BugReportsUI** - specialized screens.
- **TavernManagerUI** - NOT a UIWindow (persistent overlay) - reputation bars, temperature/dirtiness indicators, time/weather.

### Text
- **TextMeshProUGUI** is the only text component used (1139 occurrences, 135 files) - legacy Unity `Text` is not used at all. Good news: consistent API for text extraction.
- Localized text handled via `LocaliseText` component attached to UI text fields.

### Tooltips
- **`TooltipDisplay`** (implements `IPointerEnterHandler`/`IPointerExitHandler`) - attached to hoverable elements, shows/hides tooltip.
- **`TooltipInfo`** struct - `toolTipTitle`, `mainBody`, `itemInstance`.
- **`TooltipPanel`** - rendering (title + body TextMeshProUGUI fields).
- **`ItemTooltip`** (extends UIWindow) - per-player instances, 0.5s show delay.
- **`QuestTooltipUI`**, **`BirdTooltipPanel`** - specialized tooltip variants.

---

## 5. Game Mechanics - Feature Catalog

### Player / Character
- **PlayerController** - `playerNum`, `speed`, `sprintMultiplier`, `moving`, `zoneIndex`, events `OnZoneChanged`, `OnPlayerMoving`. No explicit health/stamina system found - this game has no combat/health bars.
- **PlayerInventory** - `inventory`, `actionBarInventory`, `mouseSlot`, method `HasItem(...)`, event `OnChanged`.

### Inventory / Items
- **Container** (base) - `Slot[] slots`, `ContainerType inventoryType`, allowed item/tag/food-type filters, `maxStack`.
- **Inventory** extends Container - main player inventory.
- **Item** (ScriptableObject) - `id`, `nameId` (translation key), `description`, `icon`/`sprite`, `canBeStacked`, `price`/`sellPrice`, `category`, `tags`, `recipe`, `appearsInOrders`.
- **Food** extends Item - `foodType`, `modifiers` (taste/spice etc.), `canBeAged`, `containsAlcohol`. **No separate Drink class** - drinks are `Food` with `foodType = Drink`.
- **ItemInstance** - runtime instance wrapping an `Item`, with `currentSlot`, methods for display name/sprite; subclasses `FoodInstance`, `AnimalInstance`, `ToolInstance`.
- **Slot** - single inventory slot, events `OnItemAdded`/`OnItemRemoved`/`OnChangedSlot`.

### Interaction
- **`IInteractable`** interface - `MouseUp(playerNum)`, `MouseHold(playerNum, held)`, `MouseUpOnline(playerNum)`.
- Companion interfaces: `IProximity` (distance), `IHoverable` (mouse-over), `ISelectable` (click selection). Most interactables implement all three + IInteractable.
- Examples: `Crafter` (cooking station), `ItemContainer` (chests), `DrinkDispenser`, `DialogueNPCBase` (NPCs).

### Quests / Missions
- **Quest** (ScriptableObject) - `id`, `nameId`, `description`, `requiredAmount`, `reward`, `recipesUnlocked`, `isRepeatable`, event `OnProgressQuest(playerNum, amount, completed)`.
- Subtypes: `ActionDoneQuest`, `RequiredItemQuest`, `BuyItemQuest`, `CraftItemTypeQuest`, `ServeCustomerQuest`, `ImportantCustomerQuest`, `FarmCropQuest`.
- **ActiveQuest** / **ActiveMission** - runtime progress tracking.

### Dialogue / NPCs
- **NPC** (base MonoBehaviour) - movement/pathfinding (`NPCWalkTo`), `routine` (daily schedule), event `OnNPCStateChanged`.
- **DialogueNPCBase** extends NPC, implements IInteractable/IProximity/IHoverable - uses **Pixel Crushers Dialogue System** (3rd-party plugin) via `DialogueSystemTrigger`. Events `ConversationStarted(bool)`, `ConversationEnded(bool)`.
- **HumanNPC** extends NPC - adds `customer` (if also a Customer), `seat`.
- Many named NPC subclasses (BobNPC, AgathaNPC, CatNPC, DogNPC, BuzzNPC, etc.)

### Customers / Tavern Service (game-specific core loop)
- **CustomerBase** (abstract) → **Customer** - `customerState` (enum below), `customerOrder`, `seat`. Methods `ChangeState()`, `GiveItem()`, `Kick()`.
- **CustomerState** enum: `Inactive, Spawning, Despawning, HeadingToBar, WaitingAtBar, HeadingToSeat, EatingAtTable, BeingANuisance, Leaving, WaitingForBarSpot, RentRoom, OrderInTable, RequestRoom, RouteWalk, Adoption`.
- **CustomerOrder** - `orderedItems[]`, `reputation` (reward), `dateIssued`/`dateDeadline`.
- **CustomerPool** - spawning/pooling.
- **Employee** (abstract base for staff) → **Barworker** - `barworkerState` (Waiting/ServingCustomer/TakingDrink/Leaving/AvoidingWork), `customerServing`, tray handling via `TrayHandler`/`Tray`.
- **Bar** - `waitingAtBar` list, tavern bar room logic.

### Crafting / Cooking
- **Recipe** (ScriptableObject) - `ingredientsNeeded[]`, `output`, `recipeSilverCost`, `fuel`, `time`, `recipeGroup` (Food/Drink/Wood/Stone/Metal), `page` (cookbook category), `unlock` condition.
- **Crafter** (cooking station, implements ISelectable/IInteractable/IHoverable/IProximity) - `craftingList` queue, `currentRecipe`, `dateFinished`, `broken`/`repairCost`.

### Shop / Currency
- **Money** - singleton, `GetBalance()`, `ToCopper()`, price-modifier settings.
- **Price** struct - `gold`/`silver`/`copper` (1 gold = 100 silver = 10000 copper).
- **Shop** (ScriptableObject) - `shopType`, `shopItems`, restock settings (`minItems`/`maxItems`/`delayHours`), seasonal stock.

---

## 6. Status and Notifications

- **No centralized "toast/popup" system for generic messages** was found. Feedback to the player happens mainly via:
  - **TutorialManager** / **NewTutorialManager** popups (see section 13) - text-based, good hook for screen reader announcements.
  - Money gain/loss popups: `Money.LNDBFPMBBBD(Price, Vector3, bool)` (obfuscated name, verify before use).
  - Quest/objective progress events (`OnProgressQuest`, `ObjectivesUpdated`).
- **PlayerInfo** static class - player name(s), tavern name (`PlayerInfo.tavernName`).
- Recommended mod approach: hook `UIWindow.OnAnyUIOpen`/`OnAnyUIClose` for screen-level announcements, plus per-window specific text extraction (TextMeshProUGUI is consistent across the whole UI).

---

## 7. Audio System

- Game uses **psai.net** (`psai.net` namespace, 40+ files) - a dynamic/adaptive music middleware (PsaiCore, Logik, Theme, Segment, AudioPlaybackLayerChannelUnity). Not yet analyzed in depth; likely irrelevant to accessibility (music layer only), but worth knowing it exists if SFX cues need investigation later.

---

## 8. Save/Load

- **SaveUI** (UIWindow) - save slot list (`SaveSlotUI`), new-game flow, `loading`/`tutorial` state flags, events `OnLoadFadeOut`/`OnLoadFadeOutStart`.
- Deeper save-file format not yet analyzed (not critical for early mod phases).

---

## 9. Event Hooks for Harmony Patches

Best candidates found so far:

- **`UIWindow.OnAnyUIOpen` / `OnAnyUIClose`** (static `Action<int, UIWindow>`) - universal UI open/close detection. **Top priority hook.**
- **`PlayerController.OnZoneChanged`** (`Action<int, ZoneType, int>`), `OnPlayerMoving` - player movement/location announcements.
- **`LocalisationSystem.OnLanguageChanged`** (`Action`) - detect in-game language switch, refresh mod's Loc dictionary.
- **`TutorialManager`/`NewTutorialManager`** - `ShowPopUp(...)`, `ObjectivesUpdated`, `OnTutorialActivate(bool)` - tutorial text/objective announcements.
- **`CommonReferences`** - `OnAnyQuestProgress`, `OnWaiterGoingToWork`, `OnBarOpenWithTrends` - misc gameplay events.
- **`ShopBaseUI.OnItemsBought`**, **`GameCraftingUI.OnCraftingUIOpen/Close`** - feature-specific events.
- General method-name patterns confirmed present: `Open()`/`OnOpen()`, `Close()`/`CloseUI()`, `Show()`/`Hide()`.

---

## 10. Localization

- Custom **`LocalisationSystem`** class wraps the 3rd-party **I2 Localization** plugin (`I2.Loc` namespace, `LocalizationManager` class).
- **Current language:** `LocalizationManager.CurrentLanguage` (I2.Loc, string) or `LocalisationSystem`'s own `GameLanguage` object (property name obfuscated - verify in source).
- **Format:** Full language name string (e.g. `"English"`, `"Spanish"`), **NOT** a 2-letter ISO code. The mod's `Loc.RefreshLanguage()` switch statement must match on these full names, not codes.
- **String lookup:** `LocalisationSystem.Get(textID)` or `LocalizationManager.GetTranslation(textID)`.
- **Change event:** `LocalisationSystem.OnLanguageChanged` - subscribe to refresh mod language automatically when player changes it in Options.
- **GameLanguage** class - `name`, `localisedName`, `spreadSheetIndex` (column index into the translation data table).
- Underlying data: tab-separated translation file, loaded via I2.Loc machinery (exact path not yet critical to confirm for mod purposes - mod has its own independent `Loc.cs`, only needs to *read* the current language string).

---

## 11. Code Examples

```csharp
// Check if any UI is open for player 1
bool anyOpen = MainUI.GetInstance().IsAnyUIOpen(1);

// Get current player position/zone
var player = PlayerController.GetPlayer(1);
int zone = player.zoneIndex;

// Get current language (I2.Loc)
string lang = I2.Loc.LocalizationManager.CurrentLanguage;

// Harmony patch: announce on any UI open
[HarmonyPatch(typeof(UIWindow), nameof(UIWindow.OnAnyUIOpen))]
// NOTE: OnAnyUIOpen is a static Action/event, not a method - patch via event subscription
// in Main.cs instead: UIWindow.OnAnyUIOpen += (playerNum, window) => { ... };
```

*(More examples to be added as features are implemented and verified against actual compiled behavior.)*

---

## 12. Known Issues and Workarounds

- Many internal fields/methods have **compiler-obfuscated names** (random uppercase letter sequences like `GGFJGHHHEJC`). These are legitimate working symbols - always grep the decompiled source to get exact current names before writing code; never guess or assume a "clean" name exists.
- Some `KeyCode` usages appear as raw negative or unusual integers (e.g. `(KeyCode)(-160)`) - purpose unconfirmed, re-verify context in source before assuming a key is free.

---

## 13. Tutorial System (detailed)

Two parallel systems exist:

**A. Legacy `TutorialManager`** (singleton, `.GetInstance()`)
- Text popups (`TutorialPopUp` extends `PopUp`) shown via `ShowPopUp(popup, minimizable, blockClosed)`, queued in `popUpQueue`.
- Enabled/disabled via boolean flag (obfuscated name - verify); player can opt out via a yes/no dialog (`ShowDialogueTutorialEnabled()`).

**B. `NewTutorialManager`** (static `instance`) - the active/current tutorial system
- Phase-based (`tutorialPhases[]`, phase IDs in the 100s range), auto-advances with gameplay milestones.
- Objectives panel (`objectivesPanel`, `objectives: TextImageUI[]`) shows current goals; `UpdateObjectives()` refreshes it.
- Text shown via `ShowPopUp(text)`, looked up as `LocalizationManager.GetTranslation("Tutorial_Main_" + phaseID)`.
- Phases cover (approx, by ID range 100-142): sleep mechanics, floor cleaning, candles/cellar, beer serving, customer interaction, room renting, city exploration, crafting, farming, quarry.
- Several gameplay systems are deliberately **blocked during tutorial** until the relevant phase (`SleepBlocked()`, `OpenTavernBlocked()`, `CrafterBlocked()`, `ContainerBlocked()`, `CleanTableBlocked()`, and 15+ similar methods) - important to know so the mod doesn't announce actions as available when they're tutorial-locked.
- Skippable - player can disable via dialog, sets an internal flag to false.

**Accessibility implication:** the tutorial is the natural starting point for a new blind player and introduces every core mechanic in sequence - high priority to make its popups and objectives screen-reader-announced first.

---

## 14. Not Yet Analyzed

- [ ] Exact save-file format/path
- [ ] Full Rewired action-to-physical-key default mapping (only hardcoded KeyCodes found so far; default Rewired bindings need checking via in-game settings or Rewired input manager asset)
- [ ] Farming/crop system detail
- [ ] Staff hiring detail beyond Barworker
- [ ] Construction/building mode detail (`TavernConstructionUI`, `FarmConstructionManager`)
- [ ] Exact obfuscated property names for several singletons (re-verify at implementation time, not guessed here)

---

## Change History

- **2026-06-18:** Initial Phase 1 codebase analysis completed (namespaces, singletons, input system, UI system, game mechanics, status/events/localization/tutorial).
