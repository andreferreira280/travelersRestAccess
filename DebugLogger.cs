using MelonLoader;
using UnityEngine;

namespace TravellersRestAccess
{
    /// <summary>
    /// Centralized debug logging with categories.
    /// All logging goes through here so it can be filtered and controlled.
    ///
    /// Categories help when reading logs:
    ///   [SR] Screenreader output - what the user hears
    ///   [INPUT] Key presses and input events
    ///   [STATE] Screen/menu state changes
    ///   [HANDLER] Handler decisions and actions
    ///   [GAME] Values read from game (positions, stats, etc.)
    /// </summary>
    public static class DebugLogger
    {
        public static void Log(LogCategory category, string message)
        {
            if (!Main.DebugMode) return;

            string prefix = GetPrefix(category);
            MelonLogger.Msg($"{prefix} {message}");
        }

        public static void Log(LogCategory category, string source, string message)
        {
            if (!Main.DebugMode) return;

            string prefix = GetPrefix(category);
            MelonLogger.Msg($"{prefix} [{source}] {message}");
        }

        /// <summary>
        /// Log screenreader output. Called automatically by ScreenReader.Say().
        /// </summary>
        public static void LogScreenReader(string text)
        {
            if (!Main.DebugMode) return;

            MelonLogger.Msg($"[SR] {text}");
        }

        public static void LogInput(string keyName, string action = null)
        {
            if (!Main.DebugMode) return;

            string msg = action != null
                ? $"{keyName} -> {action}"
                : keyName;
            MelonLogger.Msg($"[INPUT] {msg}");
        }

        public static void LogState(string description)
        {
            if (!Main.DebugMode) return;

            MelonLogger.Msg($"[STATE] {description}");
        }

        public static void LogGameValue(string name, object value)
        {
            if (!Main.DebugMode) return;

            MelonLogger.Msg($"[GAME] {name} = {value}");
        }

        private static readonly KeyCode[] AllKeyCodes = (KeyCode[])System.Enum.GetValues(typeof(KeyCode));

        /// <summary>
        /// Logs every key that transitions to "down" this frame, regardless of whether any
        /// of our handlers act on it - call once per frame from Main.OnUpdate. Unlike
        /// LogInput (which only fires where we explicitly call it, i.e. only for keys we
        /// already suspected mattered), this catches everything, including keys the game's
        /// own code reacts to behind our back (this is how the Space-closes-Character-
        /// Creator bug should have been visible from frame one, instead of needing several
        /// rounds of guessing - cheap enough to leave on permanently in debug mode, since it
        /// only does the expensive enum scan on frames where Input.anyKeyDown is true).
        /// </summary>
        public static void LogRawKeyDowns()
        {
            if (!Main.DebugMode) return;
            if (!Input.anyKeyDown) return;

            foreach (var key in AllKeyCodes)
            {
                if (Input.GetKeyDown(key))
                {
                    MelonLogger.Msg($"[INPUT-RAW] KeyDown: {key}");
                }
            }
        }

        private static string GetPrefix(LogCategory category)
        {
            switch (category)
            {
                case LogCategory.ScreenReader: return "[SR]";
                case LogCategory.Input: return "[INPUT]";
                case LogCategory.State: return "[STATE]";
                case LogCategory.Handler: return "[HANDLER]";
                case LogCategory.Game: return "[GAME]";
                default: return "[DEBUG]";
            }
        }
    }

    /// <summary>
    /// Categories for debug logging.
    /// </summary>
    public enum LogCategory
    {
        ScreenReader,
        Input,
        State,
        Handler,
        Game
    }
}
