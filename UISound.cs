using System.Collections;
using MelonLoader;

namespace TravellersRestAccess
{
    /// <summary>
    /// Plays a short navigation sound on every list move, reusing the game's own UI click
    /// clips (Sound.uiClickPos/uiClickNeg, confirmed in decompiled source as the standard
    /// "valid click"/"invalid click" sounds used across the game's own UI) instead of adding
    /// new audio assets. User's request: a sound on every move, and a DIFFERENT one when
    /// there's nowhere to go (only one item, or hit the top/bottom of the list) - applies to
    /// every navigable list in the mod (KeyboardUINavigator's menus, DialogueAnnouncer's
    /// response choices), vertical or horizontal alike.
    /// </summary>
    public static class UISound
    {
        // Playing both clips in the exact same frame (the first implementation) made them
        // blend into one indistinct blob - confirmed by user feedback ("tocam muito juntos
        // para identificar diferença"). A short gap lets the navigate click finish before
        // the boundary click starts, so the ear hears two distinct sounds instead of one.
        private const float BoundaryDelaySeconds = 0.15f;

        public static void PlayNavigate()
        {
            if (Sound.GGFJGHHHEJC == null)
            {
                if (Main.DebugMode) DebugLogger.LogState("UISound.PlayNavigate: Sound instance is null, sound skipped");
                return;
            }
            LogIfBlocked("PlayNavigate");
            Sound.GGFJGHHHEJC.PlayOneShot(Utils.CPDCJAHJOPE(Sound.GGFJGHHHEJC.uiClickPos), true, null, null, 0.5f);
        }

        public static void PlayBoundary()
        {
            if (Sound.GGFJGHHHEJC == null)
            {
                if (Main.DebugMode) DebugLogger.LogState("UISound.PlayBoundary: Sound instance is null, sound skipped");
                return;
            }
            LogIfBlocked("PlayBoundary");
            Sound.GGFJGHHHEJC.PlayOneShot(Utils.CPDCJAHJOPE(Sound.GGFJGHHHEJC.uiClickNeg), true, null, null, 0.5f);
        }

        // Use this instead of PlayBoundary() whenever it's layered together with
        // PlayNavigate() on the same move/action, so the two don't overlap into one sound.
        public static void PlayBoundaryDelayed()
        {
            MelonCoroutines.Start(PlayBoundaryAfterDelay());
        }

        // User's request: a sound that's distinguishable from the plain advance/skip sound
        // specifically when a dialogue RESPONSE is chosen (as opposed to just advancing to
        // the next line) - reuses the same clip, pitched up, instead of pulling in a
        // thematically-unrelated game sound (e.g. uiWindowOpen/newQuest already mean
        // something else elsewhere).
        public static void PlayChoiceConfirm()
        {
            if (Sound.GGFJGHHHEJC == null)
            {
                if (Main.DebugMode) DebugLogger.LogState("UISound.PlayChoiceConfirm: Sound instance is null, sound skipped");
                return;
            }
            LogIfBlocked("PlayChoiceConfirm");
            Sound.GGFJGHHHEJC.PlayOneShot(Utils.CPDCJAHJOPE(Sound.GGFJGHHHEJC.uiClickPos), true, null, null, 0.5f, 1.4f);
        }

        private static IEnumerator PlayBoundaryAfterDelay()
        {
            yield return new UnityEngine.WaitForSecondsRealtime(BoundaryDelaySeconds);
            PlayBoundary();
        }

        // Diagnostic only - the game itself silently skips PlayOneShot whenever its own
        // blockSound set is non-empty (e.g. during certain transitions/cutscenes). Logged so
        // a future "no sound at all" report can be confirmed instead of guessed at.
        private static void LogIfBlocked(string caller)
        {
            if (!Main.DebugMode) return;
            int blocked = Sound.GGFJGHHHEJC.blockSound.Count;
            if (blocked > 0)
            {
                DebugLogger.LogState($"UISound.{caller}: game's Sound.blockSound has {blocked} entr{(blocked == 1 ? "y" : "ies")} - game may suppress this sound");
            }
        }
    }
}
