using UnityEngine;
using UnityEngine.SceneManagement;

namespace LugeRush.UI
{
    /// <summary>What the results/pause screens can do; implemented by the network session service.</summary>
    public interface IRaceFlow
    {
        bool IsMultiplayer { get; }
        bool CanRematch { get; }
        string ExitLabel { get; }
        void Rematch();
        void Exit();
    }

    /// <summary>
    /// Entry point used by HUD buttons. Without a handler (race scene opened directly in the editor) it
    /// falls back to reloading the scene / loading the main menu.
    /// </summary>
    public static class RaceFlow
    {
        public static IRaceFlow Handler;

        public static bool IsMultiplayer => Handler != null && Handler.IsMultiplayer;
        public static bool CanRematch => Handler == null || Handler.CanRematch;
        public static string ExitLabel => Handler != null ? Handler.ExitLabel : "Ana Menü";

        public static void Rematch()
        {
            Time.timeScale = 1f;
            if (Handler != null) Handler.Rematch();
            else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        public static void Exit()
        {
            Time.timeScale = 1f;
            if (Handler != null) { Handler.Exit(); return; }
            var content = GameContent.Instance;
            string menu = content != null ? content.MainMenuScene : "MainMenu";
            if (Application.CanStreamedLevelBeLoaded(menu)) SceneManager.LoadScene(menu);
            else SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
