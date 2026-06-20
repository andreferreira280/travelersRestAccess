using HarmonyLib;

namespace TravellersRestAccess
{
    /// <summary>
    /// Debug-only visibility into tutorial popups, added per the user's request to reduce
    /// the risk of missing events/alerts/triggers during test rounds - particularly
    /// tutorials, since the next round of testing moves past Character Creator into actual
    /// gameplay where tutorial phases (game-api.md section 13) are expected to start firing.
    ///
    /// DialogueAnnouncer's generic scene-wide text scan likely already picks up and
    /// announces this popup's text on its own (it's just another active TextMeshProUGUI),
    /// but this gives a clear, unambiguous log line ("Tutorial popup shown: ...") to confirm
    /// that for certain, instead of having to infer it from a generic "Dialogue text:" line.
    ///
    /// Only NewTutorialManager (the active system per game-api.md) is patched - the legacy
    /// TutorialManager is very likely inactive in this game version.
    /// </summary>
    public static class TutorialTracePatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            var target = AccessTools.Method(typeof(NewTutorialManager), "ShowPopUp", new[] { typeof(string) });
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(TutorialTracePatch), nameof(Postfix)));
        }

        public static void Postfix(string CAEDMEDBEGI)
        {
            DebugLogger.LogState($"Tutorial popup shown: \"{CAEDMEDBEGI}\"");
        }
    }
}
