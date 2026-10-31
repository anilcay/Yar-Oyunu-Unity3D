using System.Text;
using LugeRush.Input;
using LugeRush.Race;
using LugeRush.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>
    /// Temporary minimal HUD (uGUI, built in code): position, lap, times, speed, drift/boost meter,
    /// countdown, notifications and the results table. The real HUD comes from the UI programmer later.
    /// </summary>
    public class DebugRaceHud : MonoBehaviour
    {
        public RaceManager Manager;
        public RaceParticipant Player;
        [SerializeField] private int referenceWidth = 1920;
        [SerializeField] private int referenceHeight = 1080;

        private Font font;
        private Text positionText, lapText, timeText, speedText, centerText, noticeText, resultsText, meterLabel;
        private Image driftFill, slipFill;
        private GameObject resultsPanel;
        private float centerHideTime, noticeHideTime;
        private bool paused;
        private float timeScaleBeforePause = 1f;
        private PlayerSledInput playerInput;
        private SledController sled;
        private readonly StringBuilder sb = new StringBuilder(512);

        private void Start()
        {
            if (Manager == null) Manager = RaceManager.Instance != null ? RaceManager.Instance : FindFirstObjectByType<RaceManager>();
            if (Player == null)
                foreach (var p in FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None))
                    if (p.IsLocalPlayer) { Player = p; break; }
            if (Player != null)
            {
                sled = Player.GetComponent<SledController>();
                playerInput = Player.GetComponent<PlayerSledInput>();
            }

            BuildUi();
            if (Manager != null)
            {
                Manager.CountdownTick += OnCountdown;
                Manager.LapCompleted += OnLap;
                Manager.RaceFinished += OnRaceFinished;
                Manager.ParticipantFinished += OnParticipantFinished;
            }
            if (sled != null) sled.BoostStarted += OnBoost;
        }

        private void OnDestroy()
        {
            if (Manager != null)
            {
                Manager.CountdownTick -= OnCountdown;
                Manager.LapCompleted -= OnLap;
                Manager.RaceFinished -= OnRaceFinished;
                Manager.ParticipantFinished -= OnParticipantFinished;
            }
            if (sled != null) sled.BoostStarted -= OnBoost;
        }

        // ---------- Construction ----------

        private void BuildUi()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(referenceWidth, referenceHeight);
            scaler.matchWidthOrHeight = 0.5f;
            var root = canvasGo.transform;

            positionText = MakeText(root, "Position", new Vector2(0, 1), new Vector2(40, -30), 64, TextAnchor.UpperLeft);
            lapText = MakeText(root, "Lap", new Vector2(0, 1), new Vector2(40, -110), 40, TextAnchor.UpperLeft);
            timeText = MakeText(root, "Time", new Vector2(1, 1), new Vector2(-40, -30), 34, TextAnchor.UpperRight);
            speedText = MakeText(root, "Speed", new Vector2(1, 0), new Vector2(-40, 120), 60, TextAnchor.LowerRight);
            meterLabel = MakeText(root, "MeterLabel", new Vector2(1, 0), new Vector2(-40, 84), 24, TextAnchor.LowerRight);
            centerText = MakeText(root, "Center", new Vector2(0.5f, 0.5f), new Vector2(0, 120), 140, TextAnchor.MiddleCenter);
            noticeText = MakeText(root, "Notice", new Vector2(0.5f, 1), new Vector2(0, -60), 48, TextAnchor.UpperCenter);

            driftFill = MakeBar(root, "DriftMeter", new Vector2(-40, 60), new Color(1f, 0.55f, 0.1f));
            slipFill = MakeBar(root, "SlipMeter", new Vector2(-40, 36), new Color(0.3f, 0.8f, 1f));

            resultsPanel = new GameObject("Results", typeof(RectTransform), typeof(Image));
            resultsPanel.transform.SetParent(root, false);
            var prt = (RectTransform)resultsPanel.transform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.sizeDelta = new Vector2(900, 620);
            resultsPanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            resultsText = MakeText(resultsPanel.transform, "ResultsText", new Vector2(0.5f, 0.5f), Vector2.zero, 32, TextAnchor.MiddleCenter);
            ((RectTransform)resultsText.transform).sizeDelta = new Vector2(860, 600);
            resultsPanel.SetActive(false);
            centerText.text = "";
            noticeText.text = "";
        }

        private Text MakeText(Transform parent, string name, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(900, size * 1.4f);
            var t = go.GetComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            return t;
        }

        private Image MakeBar(Transform parent, string name, Vector2 pos, Color color)
        {
            var bg = new GameObject(name, typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(parent, false);
            var rt = (RectTransform)bg.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(320, 16);
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bg.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(0, 1);
            frt.pivot = new Vector2(0, 0.5f);
            frt.sizeDelta = new Vector2(0, 0);
            var img = fill.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static void SetFill(Image fill, float value01)
        {
            var rt = (RectTransform)fill.transform;
            rt.anchorMax = new Vector2(Mathf.Clamp01(value01), 1);
        }

        // ---------- Update ----------

        private void Update()
        {
            if (playerInput != null && playerInput.ConsumePause()) TogglePause();
            if (Manager == null || Player == null) return;

            int total = Manager.Participants.Count;
            positionText.text = $"{Player.Position}/{total}";
            lapText.text = $"TUR {Mathf.Max(1, Player.Lap)}/{Manager.TotalLaps}";

            sb.Clear();
            sb.Append("SÜRE  ").Append(FormatTime(Player.Finished ? Player.TotalTime : Manager.RaceTime)).Append('\n');
            sb.Append("TUR   ").Append(FormatTime(Player.Finished ? Player.LastLapTime : Player.CurrentLapTime)).Append('\n');
            sb.Append("EN İYİ ").Append(Player.BestLapTime > 0f ? FormatTime(Player.BestLapTime) : "--:--.--");
            if (!float.IsPositiveInfinity(Manager.GraceRemaining) && !Player.Finished && Manager.State == RaceState.Racing)
                sb.Append("\nKALAN ").Append(Manager.GraceRemaining.ToString("0")).Append(" sn");
            timeText.text = sb.ToString();

            if (sled != null)
            {
                speedText.text = $"{sled.SpeedKmh:0} km/s";
                SetFill(driftFill, sled.DriftCharge01);
                SetFill(slipFill, sled.SlipstreamCharge01);
                string label = sled.IsBoosting ? "BOOST!" : sled.IsDrifting ? (sled.DriftLevel == 2 ? "DRIFT  SÜPER" : sled.DriftLevel == 1 ? "DRIFT  MİNİ" : "DRIFT") :
                               sled.InSlipstream ? "RÜZGAR ÇEKME" : sled.IsOnLift ? "LİFT" : "";
                meterLabel.text = label;
            }

            if (Player.WrongWay && Manager.State == RaceState.Racing) ShowNotice("YANLIŞ YÖN!", 0.2f);
            if (Time.unscaledTime > centerHideTime) centerText.text = paused ? "DURAKLATILDI" : "";
            if (Time.unscaledTime > noticeHideTime) noticeText.text = "";
        }

        private void TogglePause()
        {
            paused = !paused;
            if (paused)
            {
                timeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;
                centerText.text = "DURAKLATILDI";
            }
            else
            {
                Time.timeScale = timeScaleBeforePause;
                centerText.text = "";
            }
        }

        private void ShowCenter(string text, float seconds)
        {
            centerText.text = text;
            centerHideTime = Time.unscaledTime + seconds;
        }

        private void ShowNotice(string text, float seconds)
        {
            noticeText.text = text;
            noticeHideTime = Time.unscaledTime + seconds;
        }

        private void OnCountdown(int tick) => ShowCenter(tick > 0 ? tick.ToString() : "BAŞLA!", tick > 0 ? 1.1f : 1.2f);

        private void OnLap(RaceParticipant p, int completedLap)
        {
            if (p != Player || p.Finished) return;
            if (completedLap == Manager.TotalLaps - 1) ShowNotice("SON TUR!", 2.5f);
            else ShowNotice($"TUR {completedLap}: {FormatTime(p.LastLapTime)}", 2.5f);
        }

        private void OnParticipantFinished(RaceParticipant p)
        {
            if (p == Player) ShowCenter($"{p.Position}. oldun!", 3f);
        }

        private void OnBoost(SledBoostKind kind)
        {
            if (kind == SledBoostKind.Start) ShowNotice("SÜPER ÇIKIŞ!", 1.5f);
        }

        private void OnRaceFinished()
        {
            sb.Clear();
            sb.Append("SONUÇLAR\n\n");
            foreach (var p in Manager.Standings)
            {
                sb.Append(p.Position).Append(". ").Append(p.DisplayName.PadRight(10));
                if (p.Finished) sb.Append("   ").Append(FormatTime(p.TotalTime)).Append("   en iyi ").Append(FormatTime(p.BestLapTime));
                else sb.Append("   bitiremedi");
                sb.Append('\n');
            }
            resultsText.text = sb.ToString();
            resultsPanel.SetActive(true);
        }

        public static string FormatTime(float seconds)
        {
            if (seconds <= 0f || float.IsInfinity(seconds)) return "00:00.00";
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60;
            return $"{m:00}:{s:00.00}";
        }
    }
}
