using System.Collections.Generic;

namespace TravellersRestAccess
{
    /// <summary>
    /// Announces window-level open/close events for any UIWindow screen
    /// (main menu, options, save slots, pause, etc.).
    ///
    /// Element-level focus/navigation is handled separately by KeyboardUINavigator,
    /// since this game's native UI selection only works reliably with mouse/gamepad.
    /// </summary>
    public class MenuAnnouncer
    {
        private static readonly Dictionary<string, string> WindowNames = new Dictionary<string, string>
        {
            { "TitleScreen", "Menu principal" },
            { "OptionsMenuUI", "Opções" },
            { "SaveUI", "Janela de saves" },
            { "PauseMenuUI", "Menu de pausa" },
        };

        public void Initialize()
        {
            UIWindow.OnAnyUIOpen += OnAnyUIOpen;
            UIWindow.OnAnyUIClose += OnAnyUIClose;
        }

        private void OnAnyUIOpen(int playerNum, UIWindow window)
        {
            ScreenReader.Announce("Aberto: " + GetWindowName(window));
            DebugLogger.LogState($"UI opened: {window.GetType().Name} (player {playerNum})");
        }

        private void OnAnyUIClose(int playerNum, UIWindow window)
        {
            ScreenReader.Announce("Fechado: " + GetWindowName(window));
            DebugLogger.LogState($"UI closed: {window.GetType().Name} (player {playerNum})");
        }

        private string GetWindowName(UIWindow window)
        {
            string typeName = window.GetType().Name;
            return WindowNames.TryGetValue(typeName, out var friendly) ? friendly : typeName;
        }
    }
}
