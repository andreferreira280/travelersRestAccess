using HarmonyLib;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Blocks Space from closing/accepting Character Creator.
    ///
    /// Four fix attempts total. The first three (restricting our own Activate() to the
    /// Accept button, clearing EventSystem.currentSelectedGameObject in OnUpdate, then
    /// again in OnLateUpdate, then patching MainUI.CloseLastWindowOpen) all targeted the
    /// wrong cause - confirmed live each time the bug persisted unchanged. A diagnostic log
    /// of EventSystem.currentSelectedGameObject at the moment of the bug showed it was
    /// already null, ruling out "real UI selection" entirely; patching
    /// MainUI.CloseLastWindowOpen produced zero log output, meaning that wasn't even the
    /// call path being used.
    ///
    /// Found the ACTUAL cause by reading CharacterCreatorUI's own decompiled Update()
    /// override directly (the lesson: this game handles input per-window, not centrally -
    /// check the specific UIWindow subclass's own Update() first, not just the generic
    /// PlayerInputs dispatcher). It contains:
    ///
    ///   else if (IsOpen() && ... && PlayerInputs.GetPlayer(...).GetButtonDown("ClosePopUp")
    ///       && (!nameInput || !nameInput.isFocused) && (!tavernInput || !tavernInput.isFocused))
    ///   {
    ///       AcceptButton();
    ///   }
    ///
    /// "ClosePopUp" is a virtual button apparently bound to both Escape and Space - so this
    /// screen calls AcceptButton() directly and unconditionally whenever Space is pressed
    /// (as long as no text field has focus), regardless of cursor position or any UI
    /// selection state. This patches AcceptButton() itself: since our own code never
    /// triggers Space anymore (removed entirely from KeyboardUINavigator), the only
    /// remaining caller of AcceptButton() in response to Space is this exact line - so
    /// blocking AcceptButton() whenever Space was just pressed is safe and precise. Enter
    /// (the way our own code and the real button both activate Accept) is untouched.
    /// </summary>
    public static class SpaceClosePatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(CharacterCreatorUI), "AcceptButton");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(SpaceClosePatch), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            if (!Input.GetKeyDown(KeyCode.Space)) return true;

            DebugLogger.LogState("SpaceClosePatch: blocked Space from triggering CharacterCreatorUI.AcceptButton()");
            return false;
        }
    }
}
