using System.Collections;
using System.IO;
using System.Linq;
using LugeRush.Race;
using LugeRush.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LugeRush.Network
{
    /// <summary>
    /// Command line driven test runs (see <see cref="LaunchArgs"/>): auto host / join / single player,
    /// optional UI screenshots, quit after the race. Logs "[LR] AUTO ..." milestones.
    /// </summary>
    public class RaceAutomation : MonoBehaviour
    {
        private NetworkSessionService svc;
        private bool raceSeen;
        private float raceSceneTime;
        private bool quitting;

        private string MenuScene => GameContent.Instance != null ? GameContent.Instance.MainMenuScene : "MainMenu";

        private void Start()
        {
            LaunchArgs.Parse();
            if (!LaunchArgs.IsAutomation && string.IsNullOrEmpty(LaunchArgs.ScreenshotDir)) { enabled = false; return; }
            if (!TryGetComponent(out svc)) svc = NetworkSessionService.Instance;
            if (LaunchArgs.IsAutomation) StartCoroutine(Run());
        }

        private static void Log(string msg) => Debug.Log("[LR] AUTO " + msg);

        private IEnumerator WaitForScene(string scene, float timeout = 60f)
        {
            float t = 0f;
            while (SceneManager.GetActiveScene().name != scene && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
        }

        private IEnumerator Run()
        {
            yield return WaitForScene(MenuScene);
            yield return new WaitForSecondsRealtime(1f);
            var menu = MainMenuController.Instance;
            Log($"main menu loaded (menu={(menu != null)})");

            if (!string.IsNullOrEmpty(LaunchArgs.ScreenshotDir) && menu != null)
            {
                yield return Capture("ui_main_menu.png");
                menu.OpenSledSelect(false);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Capture("ui_sled_select.png");
                menu.Show("raceoptions");
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Capture("ui_race_options.png");
                menu.Show("settings");
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Capture("ui_settings.png");
                menu.Show("multiplayer");
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Capture("ui_multiplayer.png");
                menu.ShowRoot();
                yield return new WaitForSecondsRealtime(0.3f);
            }

            if (LaunchArgs.AutoSingle) yield return RunSingle();
            else if (LaunchArgs.AutoHost) yield return RunHost();
            else yield return RunClient();
        }

        private IEnumerator RunSingle()
        {
            var gs = GameSettings.Current;
            if (LaunchArgs.Laps > 0) gs.Laps = LaunchArgs.Laps;
            if (LaunchArgs.Bots >= 0) gs.Bots = LaunchArgs.Bots;
            if (LaunchArgs.Difficulty >= 0) gs.Difficulty = (BotDifficulty)LaunchArgs.Difficulty;
            if (!string.IsNullOrEmpty(LaunchArgs.TrackId)) gs.TrackId = LaunchArgs.TrackId;
            Log($"single player: laps={gs.Laps} bots={gs.Bots} track={gs.TrackId}");
            MainMenuController.Instance.LaunchSinglePlayer();
            yield return RaceAndResults();
            if (quitting) yield break;
            Log("results shown, returning to menu");
            RaceFlow.Exit();
            yield return WaitForScene(MenuScene, 20f);
            yield return new WaitForSecondsRealtime(1.5f);
            bool ok = SceneManager.GetActiveScene().name == MenuScene && MainMenuController.Instance != null && !svc.InSession;
            Log($"back in main menu: {ok}");
            Quit(ok ? 0 : 1);
        }

        private IEnumerator RunHost()
        {
            if (!svc.HostDirect(LaunchArgs.Port)) { Log("host failed"); Quit(2); yield break; }
            MainMenuController.Instance?.Show("lobby", false);
            Log($"hosting on port {LaunchArgs.Port}, waiting for {LaunchArgs.ExpectPlayers} player(s)");
            float t = 0f;
            while (t < 120f)
            {
                var lobby = NetworkLobby.Instance;
                if (lobby != null && lobby.Players.Count >= LaunchArgs.ExpectPlayers && LobbyRules.CanStart(lobby.Members())) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            var l = NetworkLobby.Instance;
            if (l == null || l.Players.Count < LaunchArgs.ExpectPlayers) { Log("players did not join in time"); Quit(3); yield break; }
            Log($"lobby ready: {string.Join(", ", l.Members().Select(m => $"{m.Name}({m.Sled},{m.Color},{(m.IsHost ? "host" : m.Ready ? "ready" : "waiting")})"))} bots={l.EffectiveBots}");
            yield return new WaitForSecondsRealtime(1f);
            if (!string.IsNullOrEmpty(LaunchArgs.ScreenshotDir)) yield return Capture("ui_lobby.png");
            if (!svc.StartRace()) { Log("start failed"); Quit(4); yield break; }
            yield return RaceAndResults();
            if (quitting) yield break;
            // Let the clients quit first so their logs end cleanly.
            float wait = 0f;
            while (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.ConnectedClientsIds.Count > 1 && wait < 15f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }
            Log("host done");
            Quit(0);
        }

        private IEnumerator RunClient()
        {
            if (!LaunchArgs.TryParseAddress(LaunchArgs.AutoJoin, NetworkSessionService.DefaultPort, out string ip, out ushort port))
            {
                Log($"bad address {LaunchArgs.AutoJoin}");
                Quit(2);
                yield break;
            }
            // The host may still be starting: retry for a while.
            float t = 0f;
            bool joined = false;
            while (t < 90f && !joined)
            {
                if (!svc.InSession && !svc.IsBusy && Unity.Netcode.NetworkManager.Singleton != null && !Unity.Netcode.NetworkManager.Singleton.ShutdownInProgress)
                    svc.JoinDirect(ip, port);
                float wait = 0f;
                while (wait < 6f && NetworkLobby.Instance == null && svc.InSession) { wait += Time.unscaledDeltaTime; yield return null; }
                joined = NetworkLobby.Instance != null && svc.InSession;
                if (!joined) { yield return new WaitForSecondsRealtime(2f); t += 8f; }
                if (!joined && SceneManager.GetActiveScene().name != MenuScene) yield return WaitForScene(MenuScene, 10f);
            }
            if (!joined) { Log("could not join"); Quit(3); yield break; }
            MainMenuController.Instance?.Show("lobby", false);
            Log($"joined {ip}:{port}");
            // Wait until our profile is registered, then set ready.
            t = 0f;
            while (t < 20f && !NetworkLobby.Instance.TryGetLocal(out _)) { t += Time.unscaledDeltaTime; yield return null; }
            yield return new WaitForSecondsRealtime(0.5f);
            NetworkLobby.Instance.SetLocalReady(true);
            Log("ready");
            yield return RaceAndResults();
            if (quitting) yield break;
            Log("client done");
            svc.Leave();
            yield return new WaitForSecondsRealtime(1f);
            Quit(0);
        }

        /// <summary>Waits for the race scene, the start, the finish and the results screen.</summary>
        private IEnumerator RaceAndResults()
        {
            float t = 0f;
            while (RaceManager.Instance == null && t < 120f)
            {
                if (!svc.InSession && t > 5f) { Log("session ended before the race"); Quit(5); yield break; }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            var rm = RaceManager.Instance;
            if (rm == null) { Log("race scene not loaded"); Quit(5); yield break; }
            raceSeen = true;
            raceSceneTime = Time.unscaledTime;
            Log($"race scene {SceneManager.GetActiveScene().name} loaded");

            bool hudShot = false;
            while (rm != null && rm.State != RaceState.Finished)
            {
                if (Time.unscaledTime - raceSceneTime > LaunchArgs.RaceTimeout) { Log("race timeout"); Quit(6); yield break; }
                if (!svc.InSession) { Log("session lost during the race"); Quit(7); yield break; }
                if (!hudShot && rm.State == RaceState.Racing && rm.RaceTime > 14f && !string.IsNullOrEmpty(LaunchArgs.ScreenshotDir))
                {
                    hudShot = true;
                    yield return Capture("ui_hud.png");
                }
                yield return null;
            }
            if (rm == null) { Log("race scene unloaded"); Quit(8); yield break; }
            int finished = rm.Participants.Count(p => p != null && p.Finished);
            Log($"race finished: {finished}/{rm.Participants.Count} finished");
            yield return new WaitForSecondsRealtime(2.5f);
            if (!string.IsNullOrEmpty(LaunchArgs.ScreenshotDir)) yield return Capture("ui_results.png");
        }

        private IEnumerator Capture(string file)
        {
            if (string.IsNullOrEmpty(LaunchArgs.ScreenshotDir) || Application.isBatchMode && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                yield break;
            Directory.CreateDirectory(LaunchArgs.ScreenshotDir);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(LaunchArgs.ScreenshotDir, file);
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Log($"screenshot {path} ({Screen.width}x{Screen.height})");
        }

        private void Update()
        {
            // Safety net for hung runs.
            if (LaunchArgs.IsAutomation && !quitting && Time.realtimeSinceStartup > LaunchArgs.RaceTimeout + 240f)
            {
                Log("global timeout");
                Quit(9);
            }
        }

        private void Quit(int code)
        {
            if (quitting) return;
            quitting = true;
            Log($"quit (exit code {code}, raceSeen={raceSeen})");
            if (!LaunchArgs.AutoQuitAfterRace && code == 0) { Log("staying open (no -lr-autoquit-after-race)"); return; }
            StartCoroutine(QuitSoon(code));
        }

        private IEnumerator QuitSoon(int code)
        {
            yield return new WaitForSecondsRealtime(0.5f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
