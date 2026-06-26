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

            // User's explicit request: cleaning the table updated the tutorial's objective
            // checklist (the checkmark icon toggles via ObjectiveCompleted -> UpdateObjectives,
            // confirmed in decompiled source) but nothing was announced - the objective text
            // itself (objectives[i].textMesh) doesn't change when only the checkmark icon
            // flips, so DialogueAnnouncer's generic "new/changed text" scan never re-fires for
            // it. The game's own completion sound also goes through MultiAudioManager, already
            // confirmed unreliable/silent for us (see WorldNavigationHandler's footstep note) -
            // using our own CustomSounds clip instead, same pattern as everywhere else.
            var objectiveTarget = AccessTools.Method(typeof(NewTutorialManager), "ObjectiveCompleted");
            harmony.Patch(objectiveTarget, postfix: new HarmonyMethod(typeof(TutorialTracePatch), nameof(ObjectiveCompletedPostfix)));
        }

        public static void Postfix(string CAEDMEDBEGI)
        {
            DebugLogger.LogState($"Tutorial popup shown: \"{CAEDMEDBEGI}\"");
        }

        public static void ObjectiveCompletedPostfix(NewTutorialManager __instance, int __0)
        {
            string text = null;
            if (__instance.objectives != null && __0 >= 0 && __0 < __instance.objectives.Length)
            {
                text = UITextExtractor.GetReadableText(__instance.objectives[__0]?.textMesh);
            }

            ScreenReader.Say(string.IsNullOrEmpty(text) ? "Objetivo concluído" : $"Objetivo concluído: {text}", interrupt: false);
            CustomSounds.PlayObjectiveCompleted();
            DebugLogger.LogState($"Tutorial objective completed (index {__0}): \"{text}\"");
        }
    }
}
