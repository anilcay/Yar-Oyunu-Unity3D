using System.Collections;
using LugeRush.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LugeRush.Network
{
    /// <summary>
    /// Boot scene: parses the command line, loads + applies settings, starts Unity Gaming Services
    /// (bounded wait, never blocks the game) and continues to the main menu. The Boot object (NetworkManager,
    /// session service, automation) persists for the whole run.
    /// </summary>
    public class AppBoot : MonoBehaviour
    {
        [SerializeField] private float maxServicesWait = 4f;

        private IEnumerator Start()
        {
            LaunchArgs.Parse();
            if (LaunchArgs.IsAutomation || !string.IsNullOrEmpty(LaunchArgs.ScreenshotDir)) GameSettings.SkipDisplayApply = true;
            Application.targetFrameRate = Application.isBatchMode ? 60 : -1;
            Application.runInBackground = true;
            var settings = GameSettings.Current;
            settings.Apply();
            settings.ApplyToRaceSession();
            Debug.Log($"[LR] Boot: version {Application.version}, automation={LaunchArgs.IsAutomation}, batch={Application.isBatchMode}");

            var canvas = UIFactory.CreateCanvas("BootCanvas", 0);
            var bg = UIFactory.Image(canvas.transform, "Bg", new Color(0.05f, 0.08f, 0.13f, 1f));
            UIFactory.Stretch(bg.rectTransform);
            var title = UIFactory.Text(canvas.transform, $"LUGE <color=#{UIStyle.Hex(UIStyle.Accent)}>RUSH</color>", UIStyle.TitleSize, UIStyle.Text, TextAlignmentOptions.Center, true);
            title.fontStyle = FontStyles.Bold | FontStyles.Italic;
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(1200, 140));
            var status = UIFactory.Text(canvas.transform, "Yükleniyor...", UIStyle.BodySize, UIStyle.TextMuted, TextAlignmentOptions.Center);
            UIFactory.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -60), new Vector2(1200, 50));

            var svc = NetworkSessionService.Instance;
            if (svc != null)
            {
                var task = svc.InitializeServicesAsync();
                float until = Time.realtimeSinceStartup + maxServicesWait;
                while (!task.IsCompleted && Time.realtimeSinceStartup < until)
                {
                    status.text = svc.ServicesMessage;
                    yield return null;
                }
            }
            yield return null;
            var content = GameContent.Instance;
            SceneManager.LoadScene(content != null ? content.MainMenuScene : "MainMenu");
        }
    }
}
