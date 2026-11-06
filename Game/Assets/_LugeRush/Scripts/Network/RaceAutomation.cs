using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LugeRush.Race;
using LugeRush.UI;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private bool flowFailed;
        private int gcAtStart;
        private readonly List<float> frameTimes = new List<float>(8192);
        private readonly Dictionary<int, int> slowAt = new Dictionary<int, int>();

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
            if (LaunchArgs.Flow) { yield return SingleFlow(); yield break; }
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
            if (LaunchArgs.Flow) yield return HostPauseCheck();
            yield return RaceAndResults();
            if (quitting) yield break;
            if (LaunchArgs.Flow) yield return HostFlow();
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
            if (LaunchArgs.Flow) yield return ClientFlow();
            if (quitting) yield break;
            Log("client done");
            svc.Leave();
            yield return new WaitForSecondsRealtime(1f);
            Quit(0);
        }

        // ------------------------------------------------------------------ Flow checks (-lr-flow)

        private void Check(string step, bool ok, string detail = "")
        {
            Log($"FLOW {(ok ? "ok" : "FAIL")} {step} {detail}");
            if (!ok) flowFailed = true;
        }

        private static RaceHud Hud() => FindFirstObjectByType<RaceHud>();

        /// <summary>Waits for a race manager other than <paramref name="previous"/> to reach Racing, some seconds in.</summary>
        private IEnumerator WaitForRacing(RaceManager previous, float raceTime = 2f)
        {
            float t = 0f;
            while (t < 60f)
            {
                var rm = RaceManager.Instance;
                if (rm != null && rm != previous && rm.State == RaceState.Racing && rm.RaceTime >= raceTime && Hud() != null) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Check("race start", false, "no race within 60 s");
        }

        private IEnumerator SingleFlow()
        {
            yield return WaitForRacing(null);
            var rm = RaceManager.Instance;
            var hud = Hud();
            if (rm == null || hud == null) { Quit(10); yield break; }

            hud.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.3f);
            float frozenAt = rm.RaceTime;
            yield return new WaitForSecondsRealtime(0.7f);
            Check("pause freezes time", Time.timeScale == 0f && Mathf.Approximately(frozenAt, rm.RaceTime), $"timeScale={Time.timeScale}");
            Check("pause selects resume", EventSystem.current != null && EventSystem.current.currentSelectedGameObject == hud.PauseResumeButton.gameObject);
            Check("pause restart offered", hud.PauseRestartButton.gameObject.activeInHierarchy);
            hud.PauseResumeButton.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.7f);
            Check("resume", !hud.IsPaused && Time.timeScale == 1f && rm.RaceTime > frozenAt, $"t={rm.RaceTime:0.0}");

            hud.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.3f);
            hud.PauseRestartButton.onClick.Invoke();
            yield return WaitForRacing(rm);
            var rm2 = RaceManager.Instance;
            Check("pause restart", rm2 != null && rm2 != rm && Time.timeScale == 1f && !Hud().IsPaused, $"sleds={(rm2 != null ? rm2.Participants.Count : 0)}");

            yield return RaceAndResults();
            if (quitting) yield break;
            hud = Hud();
            Check("results shown", hud != null && hud.ResultsVisible);
            hud.ResultsRematchButton.onClick.Invoke();
            yield return WaitForRacing(rm2);
            var rm3 = RaceManager.Instance;
            Check("results rematch", rm3 != null && rm3 != rm2 && rm3.State == RaceState.Racing && !Hud().ResultsVisible, $"sleds={(rm3 != null ? rm3.Participants.Count : 0)}");

            hud = Hud();
            hud.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.3f);
            hud.PauseExitButton.onClick.Invoke();
            yield return WaitForScene(MenuScene, 20f);
            yield return new WaitForSecondsRealtime(1.5f);
            Check("pause exit to menu", SceneManager.GetActiveScene().name == MenuScene && MainMenuController.Instance != null && !svc.InSession && Time.timeScale == 1f);

            MainMenuController.Instance.LaunchSinglePlayer();
            yield return WaitForRacing(rm3, 1f);
            Check("second session from the menu", RaceManager.Instance != null && RaceManager.Instance.State == RaceState.Racing);
            RaceFlow.Exit();
            yield return WaitForScene(MenuScene, 20f);
            yield return new WaitForSecondsRealtime(1f);
            Check("exit after second session", SceneManager.GetActiveScene().name == MenuScene && !svc.InSession);
            Quit(flowFailed ? 11 : 0);
        }

        private IEnumerator HostPauseCheck()
        {
            yield return WaitForRacing(null);
            var hud = Hud();
            if (hud == null) yield break;
            float t = RaceManager.Instance.RaceTime;
            hud.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.7f);
            Check("mp pause keeps running", Time.timeScale == 1f && RaceManager.Instance.RaceTime > t);
            Check("mp pause hides restart", !hud.PauseRestartButton.gameObject.activeInHierarchy);
            Check("mp pause exit label", UIFactory.LabelOf(hud.PauseExitButton).text == "Lobiye Dön", UIFactory.LabelOf(hud.PauseExitButton).text);
            hud.PauseResumeButton.onClick.Invoke();
        }

        private IEnumerator HostFlow()
        {
            var rm = RaceManager.Instance;
            var hud = Hud();
            Check("host results rematch offered", hud != null && hud.ResultsRematchButton.gameObject.activeInHierarchy);
            hud.ResultsRematchButton.onClick.Invoke();
            yield return WaitForRacing(rm);
            Check("host rematch", RaceManager.Instance != rm && RaceManager.Instance != null, $"sleds={RaceManager.Instance?.Participants.Count}");
            yield return RaceAndResults();
            if (quitting) yield break;
            hud = Hud();
            Check("host exit label", UIFactory.LabelOf(hud.ResultsExitButton).text == "Lobiye Dön");
            hud.ResultsExitButton.onClick.Invoke();
            yield return WaitForScene(MenuScene, 20f);
            yield return new WaitForSecondsRealtime(2f);
            var lobby = NetworkLobby.Instance;
            var menu = MainMenuController.Instance;
            Check("back in lobby", menu != null && menu.Current != null && menu.Current.Id == "lobby" && svc.InSession, $"panel={menu?.Current?.Id}");
            Check("lobby reopened", lobby != null && !lobby.InRace.Value, $"players={lobby?.Players.Count}");
            if (flowFailed) { Quit(11); yield break; }
        }

        private IEnumerator ClientFlow()
        {
            var rm = RaceManager.Instance;
            yield return WaitForRacing(rm, 1f);
            Check("client rematch", RaceManager.Instance != rm && RaceManager.Instance != null);
            yield return RaceAndResults();
            if (quitting) yield break;
            yield return WaitForScene(MenuScene, 60f);
            yield return new WaitForSecondsRealtime(1.5f);
            var menu = MainMenuController.Instance;
            Check("client back in lobby", menu != null && menu.Current != null && menu.Current.Id == "lobby", $"panel={menu?.Current?.Id}");
            if (flowFailed) Quit(11);
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
                if (rm.State == RaceState.Racing && rm.RaceTime > 1f)
                {
                    if (frameTimes.Count == 0) gcAtStart = System.GC.CollectionCount(0);
                    frameTimes.Add(Time.unscaledDeltaTime * 1000f);
                    if (Time.unscaledDeltaTime > 0.025f)
                    {
                        var me = rm.Participants.FirstOrDefault(p => p.IsLocalPlayer);
                        if (me != null) slowAt[(int)(me.TrackDistance / 50f) * 50] = slowAt.TryGetValue((int)(me.TrackDistance / 50f) * 50, out int n) ? n + 1 : 1;
                    }
                }
                yield return null;
            }
            if (rm == null) { Log("race scene unloaded"); Quit(8); yield break; }
            LogFrameStats();
            int finished = rm.Participants.Count(p => p != null && p.Finished);
            Log($"race finished: {finished}/{rm.Participants.Count} finished");
            yield return new WaitForSecondsRealtime(2.5f);
            if (!string.IsNullOrEmpty(LaunchArgs.ScreenshotDir)) yield return Capture("ui_results.png");
        }

        /// <summary>Average and 1 % low frame time over the race just finished ("[LR] PERF ...").</summary>
        private void LogFrameStats()
        {
            if (frameTimes.Count == 0) return;
            // Where the slow frames fall: count over 20 ms per 10 s of race.
            var buckets = new int[32];
            float clock = 0f;
            foreach (float ms in frameTimes)
            {
                clock += ms / 1000f;
                if (ms > 20f) buckets[Mathf.Min(buckets.Length - 1, (int)(clock / 10f))]++;
            }
            Debug.Log($"[LR] PERF frames over 25 ms by track metre: {string.Join(" ", slowAt.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}m:{kv.Value}"))}");
            slowAt.Clear();
            int gcCount = System.GC.CollectionCount(0) - gcAtStart;
            Debug.Log($"[LR] PERF slow frames per 10 s: {string.Join(" ", buckets.Take(1 + (int)(clock / 10f)))} gc={gcCount} over33ms={frameTimes.Count(f => f > 33f)} over16ms={frameTimes.Count(f => f > 16.7f)}");
            frameTimes.Sort();
            int worst = Mathf.Max(1, frameTimes.Count / 100);
            float avg = frameTimes.Average();
            float low1 = frameTimes.Skip(frameTimes.Count - worst).Average();
            Debug.Log($"[LR] PERF scene={SceneManager.GetActiveScene().name} frames={frameTimes.Count} avg={avg:0.00}ms ({1000f / avg:0.0} fps) " +
                      $"low1%={low1:0.00}ms ({1000f / low1:0.0} fps) max={frameTimes[frameTimes.Count - 1]:0.0}ms " +
                      $"res={Screen.width}x{Screen.height} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} " +
                      $"gpu={SystemInfo.graphicsDeviceName} api={SystemInfo.graphicsDeviceType}");
            frameTimes.Clear();
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
