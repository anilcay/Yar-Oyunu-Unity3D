using System.Collections.Generic;
using System.Text;
using LugeRush.Input;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>
    /// Race HUD (uGUI + TMP, built in code): position, lap, race/lap/best times, speed, drift + slipstream meters,
    /// countdown, last lap / overtake / wrong way notices, live standings, minimap from the track spline,
    /// pause menu and results screen. Binds lazily to RaceManager and the local player (network spawned sleds).
    /// </summary>
    public class RaceHud : MonoBehaviour
    {
        public RaceManager Manager;
        public RaceParticipant Player;

        private Canvas canvas;
        private TextMeshProUGUI position, positionTotal, lap, timeValues, grace, speed, meterLabel, countdown, notice, subNotice, standings;
        private RectTransform driftFill, slipFill, countdownRt;
        private Image noticeBg, subNoticeBg;
        private RectTransform minimap;
        private UILineGraphic mapLine;
        private readonly Dictionary<RaceParticipant, Image> dots = new Dictionary<RaceParticipant, Image>();
        private Image playerDot, playerDotRing;
        private Vector2 mapMin, mapMax;
        private float mapScale;
        private Vector2 mapOffset;
        private bool mapBuilt;

        private GameObject pausePanel, resultsPanel;
        private Button pauseResume, pauseRestart, pauseExit, resultsRematch, resultsExit;
        private TextMeshProUGUI resultsTitle, resultsSubtitle, resultsWaiting;
        private readonly List<TextMeshProUGUI[]> resultRows = new List<TextMeshProUGUI[]>();
        private readonly List<Image> resultRowBgs = new List<Image>();

        private SledController sled;
        private PlayerSledInput playerInput;
        private bool paused;
        private float noticeHide, subNoticeHide, countdownHide, countdownStart;
        private int lastPosition;
        private float lastOvertakeNotice;
        private readonly StringBuilder sb = new StringBuilder(512);
        private bool subscribed;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public bool IsPaused => paused;

        private void Start()
        {
            UIFactory.EnsureEventSystem();
            Build();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (paused) Time.timeScale = 1f;
        }

        private void Bind()
        {
            if (Manager == null) Manager = RaceManager.Instance;
            if (Manager != null && !subscribed)
            {
                Manager.CountdownTick += OnCountdown;
                Manager.LapCompleted += OnLap;
                Manager.ParticipantFinished += OnParticipantFinished;
                Manager.RaceFinished += OnRaceFinished;
                subscribed = true;
            }
            if (Player == null && Manager != null)
            {
                foreach (var p in Manager.Participants)
                    if (p != null && p.IsLocalPlayer) { Player = p; break; }
            }
            if (Player != null && sled == null)
            {
                sled = Player.Controller != null ? Player.Controller : Player.GetComponent<SledController>();
                playerInput = Player.GetComponent<PlayerSledInput>();
                if (sled != null) sled.BoostStarted += OnBoost;
            }
        }

        private void Unsubscribe()
        {
            if (Manager != null && subscribed)
            {
                Manager.CountdownTick -= OnCountdown;
                Manager.LapCompleted -= OnLap;
                Manager.ParticipantFinished -= OnParticipantFinished;
                Manager.RaceFinished -= OnRaceFinished;
            }
            subscribed = false;
            if (sled != null) sled.BoostStarted -= OnBoost;
        }

        // ------------------------------------------------------------------ Build

        private void Build()
        {
            canvas = UIFactory.CreateCanvas("HUD_Canvas", 10, transform);
            var root = canvas.transform;

            // Position + lap (top left)
            var posBg = UIFactory.Panel(root, "PositionBg", new Color(0.04f, 0.06f, 0.10f, 0.62f), 0.7f);
            posBg.raycastTarget = false;
            UIFactory.Place(posBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -24), new Vector2(330, 236));
            var posLabel = UIFactory.Text(root, "SIRA", UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.TopLeft, false, "PosLabel");
            posLabel.characterSpacing = 8;
            UIFactory.Place(posLabel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(176, -40), new Vector2(150, 30));
            position = Shadowed(UIFactory.Text(root, "-", 128, UIStyle.Text, TextAlignmentOptions.BottomLeft, true, "Position"));
            position.fontStyle = FontStyles.Bold | FontStyles.Italic;
            UIFactory.Place(position.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -44), new Vector2(160, 150));
            positionTotal = Shadowed(UIFactory.Text(root, "/8", 50, UIStyle.TextMuted, TextAlignmentOptions.BottomLeft, true, "PositionTotal"));
            UIFactory.Place(positionTotal.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(170, -72), new Vector2(140, 100));
            lap = Shadowed(UIFactory.Text(root, "TUR 1/3", 46, UIStyle.Text, TextAlignmentOptions.TopLeft, true, "Lap"));
            lap.fontStyle = FontStyles.Italic;
            UIFactory.Place(lap.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(66, -190), new Vector2(300, 70));

            var standingsBg = UIFactory.Panel(root, "Standings", new Color(0.04f, 0.06f, 0.10f, 0.62f), 0.7f);
            standingsBg.raycastTarget = false;
            UIFactory.Place(standingsBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(44, -272), new Vector2(330, 296));
            standings = UIFactory.Text(standingsBg.transform, "", 23, UIStyle.Text, TextAlignmentOptions.TopLeft);
            standings.lineSpacing = 4f;
            UIFactory.Stretch(standings.rectTransform, 18, 14, 14, 10);

            // Times (top right)
            var timeBg = UIFactory.Panel(root, "Times", new Color(0.04f, 0.06f, 0.10f, 0.62f), 0.7f);
            timeBg.raycastTarget = false;
            UIFactory.Place(timeBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-44, -34), new Vector2(400, 150));
            var timeLabels = UIFactory.Text(timeBg.transform, "SÜRE\nTUR\nEN İYİ", 26, UIStyle.TextMuted, TextAlignmentOptions.TopLeft);
            timeLabels.lineSpacing = 12f;
            UIFactory.Stretch(timeLabels.rectTransform, 24, 18, 24, 12);
            timeValues = UIFactory.Text(timeBg.transform, "", 30, UIStyle.Text, TextAlignmentOptions.TopRight);
            timeValues.fontStyle = FontStyles.Bold;
            timeValues.lineSpacing = 2f;
            UIFactory.Stretch(timeValues.rectTransform, 24, 14, 24, 12);
            grace = Shadowed(UIFactory.Text(root, "", 30, UIStyle.Yellow, TextAlignmentOptions.TopRight, false, "Grace"));
            grace.fontStyle = FontStyles.Bold;
            UIFactory.Place(grace.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -194), new Vector2(500, 40));

            // Speed + meters (bottom right)
            speed = Shadowed(UIFactory.Text(root, "0", 120, UIStyle.Text, TextAlignmentOptions.BottomRight, true, "Speed"));
            speed.fontStyle = FontStyles.Bold | FontStyles.Italic;
            UIFactory.Place(speed.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-150, 112), new Vector2(330, 150));
            var unit = Shadowed(UIFactory.Text(root, "km/s", 32, UIStyle.TextMuted, TextAlignmentOptions.BottomLeft, true, "Unit"));
            UIFactory.Place(unit.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-44, 132), new Vector2(100, 50));
            meterLabel = Shadowed(UIFactory.Text(root, "", 26, UIStyle.Accent, TextAlignmentOptions.BottomRight, false, "MeterLabel"));
            meterLabel.fontStyle = FontStyles.Bold;
            UIFactory.Place(meterLabel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-44, 82), new Vector2(420, 34));
            driftFill = UIFactory.FillBar(root, "DriftMeter", UIStyle.Accent, out var driftBg);
            UIFactory.Place(driftBg.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-44, 56), new Vector2(380, 24));
            var mark = UIFactory.Image(driftBg.transform, "MiniMark", new Color(1, 1, 1, 0.6f));
            UIFactory.Place(mark.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3, 24));
            slipFill = UIFactory.FillBar(root, "SlipMeter", UIStyle.Secondary, out var slipBg);
            UIFactory.Place(slipBg.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-44, 36), new Vector2(380, 14));
            var meterNames = UIFactory.Text(root, "DRIFT\nRÜZGAR", 16, UIStyle.TextMuted, TextAlignmentOptions.TopRight, false, "MeterNames");
            meterNames.lineSpacing = 0f;
            UIFactory.Place(meterNames.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-432, 30), new Vector2(120, 52));

            // Minimap (bottom left)
            var mapBg = UIFactory.Panel(root, "Minimap", new Color(0.04f, 0.06f, 0.10f, 0.55f), 0.7f);
            mapBg.raycastTarget = false;
            UIFactory.Place(mapBg.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(44, 36), new Vector2(330, 330));
            minimap = UIFactory.Rect("Map", mapBg.transform);
            UIFactory.Stretch(minimap, 20, 20, 20, 20);
            var outline = UIFactory.Rect("TrackOutline", minimap).gameObject.AddComponent<UILineGraphic>();
            outline.color = new Color(0f, 0f, 0f, 0.5f);
            outline.Thickness = 11f;
            outline.raycastTarget = false;
            UIFactory.Stretch(outline.rectTransform);
            mapLine = UIFactory.Rect("Track", minimap).gameObject.AddComponent<UILineGraphic>();
            mapLine.color = new Color(0.92f, 0.94f, 0.98f, 0.95f);
            mapLine.Thickness = 5f;
            mapLine.raycastTarget = false;
            UIFactory.Stretch(mapLine.rectTransform);
            mapLine.gameObject.AddComponent<MapOutlineLink>().Outline = outline;

            // Center: countdown and notices
            countdown = Shadowed(UIFactory.Text(root, "", 230, UIStyle.Text, TextAlignmentOptions.Center, true, "Countdown"));
            countdown.fontStyle = FontStyles.Bold | FontStyles.Italic;
            countdown.outlineWidth = 0.15f;
            countdown.outlineColor = new Color32(12, 18, 30, 230);
            countdownRt = countdown.rectTransform;
            UIFactory.Place(countdownRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(1200, 300));

            noticeBg = UIFactory.Panel(root, "NoticeBg", new Color(0.04f, 0.06f, 0.10f, 0.7f));
            noticeBg.raycastTarget = false;
            UIFactory.Place(noticeBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(620, 96));
            notice = UIFactory.Text(noticeBg.transform, "", 58, UIStyle.Yellow, TextAlignmentOptions.Center, true, "Notice");
            notice.fontStyle = FontStyles.Bold | FontStyles.Italic;
            UIFactory.Stretch(notice.rectTransform, 10, 0, 10, 0);
            noticeBg.gameObject.SetActive(false);

            subNoticeBg = UIFactory.Panel(root, "SubNoticeBg", new Color(0.04f, 0.06f, 0.10f, 0.6f));
            subNoticeBg.raycastTarget = false;
            UIFactory.Place(subNoticeBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -148), new Vector2(620, 56));
            subNotice = UIFactory.Text(subNoticeBg.transform, "", 30, UIStyle.Text, TextAlignmentOptions.Center, false, "SubNotice");
            subNotice.fontStyle = FontStyles.Bold;
            UIFactory.Stretch(subNotice.rectTransform, 10, 0, 10, 0);
            subNoticeBg.gameObject.SetActive(false);

            BuildPause(root);
            BuildResults(root);
        }

        private static TextMeshProUGUI Shadowed(TextMeshProUGUI t)
        {
            t.outlineWidth = 0.18f;
            t.outlineColor = new Color32(8, 12, 20, 180);
            return t;
        }

        private void BuildPause(Transform root)
        {
            var dim = UIFactory.Image(root, "Pause", new Color(0.02f, 0.03f, 0.06f, 0.6f), null, true);
            UIFactory.Stretch(dim.rectTransform);
            pausePanel = dim.gameObject;
            var panel = UIFactory.Panel(dim.transform, "PausePanel", UIStyle.PanelBg);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 520));
            UIFactory.VLayout(panel.gameObject, 16, UIFactory.Pad(48, 40));
            var t = UIFactory.Text(panel.transform, "DURAKLATILDI", UIStyle.HeaderSize, UIStyle.Text, TextAlignmentOptions.Center, true);
            UIFactory.Layout(t, -1, 70);
            var info = UIFactory.Text(panel.transform, "", UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.Center);
            info.name = "PauseInfo";
            UIFactory.Layout(info, -1, 36);
            pauseResume = UIFactory.Button(panel.transform, "Devam", () => SetPaused(false), UIStyle.ButtonHeight, true);
            pauseRestart = UIFactory.Button(panel.transform, "Yeniden Başlat", () => { SetPaused(false); RaceFlow.Rematch(); });
            pauseExit = UIFactory.Button(panel.transform, "Ana Menü", () => { SetPaused(false); RaceFlow.Exit(); });
            UIFactory.ChainVertical(pauseResume, pauseRestart, pauseExit);
            pausePanel.SetActive(false);
        }

        private void BuildResults(Transform root)
        {
            var dim = UIFactory.Image(root, "Results", new Color(0.02f, 0.03f, 0.06f, 0.55f), null, true);
            UIFactory.Stretch(dim.rectTransform);
            resultsPanel = dim.gameObject;
            var panel = UIFactory.Panel(dim.transform, "ResultsPanel", UIStyle.PanelBg);
            UIFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1280, 860));
            var v = UIFactory.VLayout(panel.gameObject, 8, new RectOffset(56, 56, 40, 40));

            resultsTitle = UIFactory.Text(panel.transform, "SONUÇLAR", 64, UIStyle.Text, TextAlignmentOptions.MidlineLeft, true);
            resultsTitle.fontStyle = FontStyles.Bold | FontStyles.Italic;
            UIFactory.Layout(resultsTitle, -1, 80);
            resultsSubtitle = UIFactory.Text(panel.transform, "", UIStyle.BodySize, UIStyle.Accent);
            resultsSubtitle.fontStyle = FontStyles.Bold;
            UIFactory.Layout(resultsSubtitle, -1, 44);

            var header = ResultRow(panel.transform, true);
            header[0].text = "SIRA"; header[1].text = "SÜRÜCÜ"; header[2].text = "KIZAK"; header[3].text = "TOPLAM"; header[4].text = "EN İYİ TUR";
            for (int i = 0; i < RaceConfig.MaxSleds; i++) resultRows.Add(ResultRow(panel.transform, false));

            var flex = UIFactory.Rect("Flex", panel.transform);
            UIFactory.Layout(flex, -1, -1, -1, 1);
            resultsWaiting = UIFactory.Text(panel.transform, "", UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.Center);
            UIFactory.Layout(resultsWaiting, -1, 34);
            var buttons = UIFactory.Rect("Buttons", panel.transform);
            var h = UIFactory.HLayout(buttons.gameObject, 20, null, TextAnchor.MiddleCenter);
            h.childForceExpandWidth = false;
            UIFactory.Layout(buttons, -1, UIStyle.ButtonHeight);
            resultsRematch = UIFactory.Button(buttons, "Tekrar", RaceFlow.Rematch, UIStyle.ButtonHeight, true);
            UIFactory.Layout(resultsRematch, 360, UIStyle.ButtonHeight);
            resultsExit = UIFactory.Button(buttons, "Ana Menü", RaceFlow.Exit);
            UIFactory.Layout(resultsExit, 360, UIStyle.ButtonHeight);
            resultsPanel.SetActive(false);
        }

        private TextMeshProUGUI[] ResultRow(Transform parent, bool header)
        {
            var bg = UIFactory.Image(parent, header ? "Header" : "Row", header ? Color.clear : UIStyle.Row, UIStyle.RoundedSmall);
            UIFactory.Layout(bg, -1, header ? 36 : 56);
            if (!header) resultRowBgs.Add(bg);
            var hl = UIFactory.HLayout(bg.gameObject, 12, new RectOffset(20, 20, 0, 0));
            hl.childForceExpandHeight = true;
            float[] widths = { 110, 380, 240, 200, 200 };
            var cells = new TextMeshProUGUI[5];
            for (int i = 0; i < 5; i++)
            {
                var align = i >= 3 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
                cells[i] = UIFactory.Text(bg.transform, "", header ? 20 : (i == 0 ? 34 : 28), header ? UIStyle.TextMuted : UIStyle.Text, align, i == 0 && !header);
                if (header) cells[i].characterSpacing = 6;
                UIFactory.Layout(cells[i], widths[i], -1, i == 1 ? 1 : -1);
            }
            return cells;
        }

        // ------------------------------------------------------------------ Update

        private void Update()
        {
            Bind();
            HandlePauseInput();
            if (Manager == null) return;
            UpdateMinimap();
            UpdateStandings();
            float now = Time.unscaledTime;
            if (noticeBg.gameObject.activeSelf && now > noticeHide) noticeBg.gameObject.SetActive(false);
            if (subNoticeBg.gameObject.activeSelf && now > subNoticeHide) subNoticeBg.gameObject.SetActive(false);
            UpdateCountdown(now);
            if (Player == null) return;

            int total = Manager.Participants.Count;
            position.text = Player.Position > 0 ? Player.Position.ToString() : "-";
            positionTotal.text = "/" + total;
            positionTotal.rectTransform.anchoredPosition = new Vector2(66 + position.preferredWidth + 6, -72);
            lap.text = Player.Finished ? "BİTİRDİN" : $"TUR {Mathf.Clamp(Player.Lap, 1, Manager.TotalLaps)}/{Manager.TotalLaps}";

            float raceTime = Player.Finished ? Player.TotalTime : Manager.RaceTime;
            float lapTime = Player.Finished ? Player.LastLapTime : Player.CurrentLapTime;
            sb.Clear();
            sb.Append("<mspace=0.56em>").Append(UIStyle.FormatTime(raceTime)).Append("\n")
              .Append(UIStyle.FormatTime(Manager.State == RaceState.Racing || Player.Finished ? lapTime : 0f)).Append("\n")
              .Append("<color=#").Append(UIStyle.Hex(UIStyle.Secondary)).Append('>').Append(UIStyle.FormatTime(Player.BestLapTime)).Append("</color></mspace>");
            timeValues.text = sb.ToString();
            grace.text = !float.IsPositiveInfinity(Manager.GraceRemaining) && !Player.Finished && Manager.State == RaceState.Racing
                ? $"KALAN {Mathf.CeilToInt(Manager.GraceRemaining)} sn" : "";

            if (sled != null)
            {
                speed.text = Mathf.RoundToInt(sled.SpeedKmh).ToString();
                UIFactory.SetFill(driftFill, sled.DriftCharge01);
                UIFactory.SetFill(slipFill, sled.SlipstreamCharge01);
                ((Image)driftFill.GetComponent<Image>()).color = sled.DriftLevel == 2 ? UIStyle.Yellow : UIStyle.Accent;
                meterLabel.text = sled.IsBoosting ? "BOOST!" :
                    sled.IsDrifting ? (sled.DriftLevel == 2 ? "DRIFT · SÜPER" : sled.DriftLevel == 1 ? "DRIFT · MİNİ" : "DRIFT") :
                    sled.InSlipstream ? "RÜZGAR ÇEKME" : sled.IsOnLift ? "LİFT" : "";
            }

            if (Player.WrongWay && Manager.State == RaceState.Racing) ShowNotice("YANLIŞ YÖN!", 0.3f, UIStyle.Bad);
            DetectOvertakes();
        }

        private void UpdateCountdown(float now)
        {
            if (!countdown.gameObject.activeSelf) return;
            float t = now - countdownStart;
            float punch = 1f + 0.35f * Mathf.Exp(-t * 7f);
            countdownRt.localScale = Vector3.one * punch;
            float fade = Mathf.Clamp01((countdownHide - now) / 0.25f);
            countdown.alpha = fade;
            if (now > countdownHide) countdown.gameObject.SetActive(false);
        }

        private void UpdateStandings()
        {
            sb.Clear();
            int i = 0;
            foreach (var p in Manager.Standings)
            {
                if (p == null) continue;
                if (i++ >= RaceConfig.MaxSleds) break;
                bool me = p == Player;
                string col = me ? UIStyle.Hex(UIStyle.Accent) : p.Finished ? UIStyle.Hex(UIStyle.Good) : UIStyle.Hex(UIStyle.Text);
                sb.Append("<color=#").Append(UIStyle.Hex(UIStyle.Yellow)).Append('>').Append(p.Position).Append("</color>  <color=#").Append(col).Append('>')
                  .Append(me ? "<b>" : "").Append(p.DisplayName).Append(me ? "</b>" : "").Append("</color>");
                if (p.Finished) sb.Append(" <size=80%><color=#").Append(UIStyle.Hex(UIStyle.Good)).Append(">✓</color></size>");
                sb.Append('\n');
            }
            standings.text = sb.ToString();
        }

        private void DetectOvertakes()
        {
            if (Manager.State != RaceState.Racing || Player.Finished || Manager.RaceTime < 3f)
            {
                lastPosition = Player.Position;
                return;
            }
            int pos = Player.Position;
            if (lastPosition == 0 || pos == lastPosition) { lastPosition = pos; return; }
            if (Time.unscaledTime - lastOvertakeNotice > 0.6f)
            {
                var standingsList = Manager.Standings;
                int otherIndex = Mathf.Clamp(lastPosition - 1, 0, standingsList.Count - 1);
                var other = standingsList[otherIndex];
                string name = other != null && other != Player ? other.DisplayName : "";
                if (pos < lastPosition) ShowSubNotice(string.IsNullOrEmpty(name) ? $"SOLLADIN!  {pos}. sıradasın" : $"SOLLADIN: {name}  ·  {pos}. sıra", UIStyle.Good);
                else ShowSubNotice(string.IsNullOrEmpty(name) ? $"{pos}. sıraya düştün" : $"{name} seni solladı  ·  {pos}. sıra", UIStyle.Bad);
                lastOvertakeNotice = Time.unscaledTime;
            }
            lastPosition = pos;
        }

        // ------------------------------------------------------------------ Minimap

        private void UpdateMinimap()
        {
            var track = Manager.Track;
            if (track == null) return;
            if (!mapBuilt)
            {
                if (track.SampleCount == 0) return;
                BuildMinimap(track);
            }
            foreach (var p in Manager.Participants)
            {
                if (p == null) continue;
                if (!dots.TryGetValue(p, out var dot))
                {
                    bool me = p.IsLocalPlayer;
                    if (me)
                    {
                        playerDotRing = UIFactory.Image(minimap, "PlayerRing", Color.white, UIStyle.Circle);
                        playerDotRing.rectTransform.sizeDelta = new Vector2(26, 26);
                    }
                    dot = UIFactory.Image(minimap, "Dot_" + p.DisplayName, ParticipantColor(p), UIStyle.Circle);
                    dot.rectTransform.sizeDelta = me ? new Vector2(18, 18) : new Vector2(14, 14);
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0, 0);
                    if (me) { playerDot = dot; playerDotRing.rectTransform.anchorMin = playerDotRing.rectTransform.anchorMax = Vector2.zero; }
                    dots[p] = dot;
                }
                Vector2 m = ToMap(p.transform.position);
                dot.rectTransform.anchoredPosition = m;
                if (dot == playerDot && playerDotRing != null) playerDotRing.rectTransform.anchoredPosition = m;
            }
            if (playerDot != null)
            {
                playerDotRing.transform.SetAsLastSibling();
                playerDot.transform.SetAsLastSibling();
            }
        }

        private void BuildMinimap(TrackPath track)
        {
            var pts = new List<Vector2>();
            mapMin = new Vector2(float.MaxValue, float.MaxValue);
            mapMax = new Vector2(float.MinValue, float.MinValue);
            for (float d = 0f; d < track.Length; d += 4f)
            {
                var p = track.GetPosition(d);
                var v = new Vector2(p.x, p.z);
                pts.Add(v);
                mapMin = Vector2.Min(mapMin, v);
                mapMax = Vector2.Max(mapMax, v);
            }
            Canvas.ForceUpdateCanvases();
            var size = minimap.rect.size;
            if (size.x <= 1f) size = new Vector2(290, 290);
            Vector2 extent = mapMax - mapMin;
            mapScale = Mathf.Min(size.x / Mathf.Max(1f, extent.x), size.y / Mathf.Max(1f, extent.y));
            mapOffset = (size - extent * mapScale) * 0.5f;
            var local = new List<Vector2>(pts.Count);
            Vector2 pivotShift = new Vector2(minimap.rect.xMin, minimap.rect.yMin);
            foreach (var v in pts) local.Add(ToMap(new Vector3(v.x, 0, v.y)) + pivotShift);
            mapLine.SetPoints(local);
            var link = mapLine.GetComponent<MapOutlineLink>();
            if (link != null && link.Outline != null) link.Outline.SetPoints(local);
            mapBuilt = true;
        }

        private Vector2 ToMap(Vector3 world) => (new Vector2(world.x, world.z) - mapMin) * mapScale + mapOffset;

        private Color ParticipantColor(RaceParticipant p)
        {
            if (p.TryGetComponent(out SledPaint paint)) return paint.CurrentColor;
            block ??= new MaterialPropertyBlock();
            foreach (var r in p.GetComponentsInChildren<Renderer>())
            {
                if (!r.gameObject.name.StartsWith("Body")) continue;
                r.GetPropertyBlock(block);
                var c = block.GetColor(BaseColorId);
                if (c.a > 0f) return c;
            }
            return p.IsBot ? new Color(0.75f, 0.78f, 0.85f) : UIStyle.Secondary;
        }

        // ------------------------------------------------------------------ Events

        private void OnCountdown(int tick)
        {
            countdown.gameObject.SetActive(true);
            countdown.text = tick > 0 ? tick.ToString() : "BAŞLA!";
            countdown.color = tick > 0 ? UIStyle.Text : UIStyle.Accent;
            countdownStart = Time.unscaledTime;
            countdownHide = Time.unscaledTime + (tick > 0 ? 0.95f : 1.2f);
        }

        private void OnLap(RaceParticipant p, int completedLap)
        {
            if (p != Player || p.Finished) return;
            if (completedLap == Manager.TotalLaps - 1) ShowNotice("SON TUR!", 2.5f, UIStyle.Yellow);
            else ShowSubNotice($"TUR {completedLap}  ·  {UIStyle.FormatTime(p.LastLapTime)}", UIStyle.Text);
        }

        private void OnParticipantFinished(RaceParticipant p)
        {
            if (p == Player) ShowNotice($"{p.FinishOrder}. OLDUN!", 3f, p.FinishOrder <= 3 ? UIStyle.Yellow : UIStyle.Text);
        }

        private void OnBoost(SledBoostKind kind)
        {
            if (kind == SledBoostKind.Start) ShowSubNotice("SÜPER ÇIKIŞ!", UIStyle.Accent);
        }

        private void ShowNotice(string text, float seconds, Color color)
        {
            notice.text = text;
            notice.color = color;
            noticeBg.gameObject.SetActive(true);
            noticeHide = Time.unscaledTime + seconds;
        }

        private void ShowSubNotice(string text, Color color)
        {
            subNotice.text = text;
            subNotice.color = color;
            subNoticeBg.rectTransform.sizeDelta = new Vector2(Mathf.Max(420, subNotice.GetPreferredValues(text).x + 60), 56);
            subNoticeBg.gameObject.SetActive(true);
            subNoticeHide = Time.unscaledTime + 2.2f;
        }

        // ------------------------------------------------------------------ Pause / results

        private void HandlePauseInput()
        {
            bool pressed = playerInput != null ? playerInput.ConsumePause() :
                (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            if (pressed && (resultsPanel == null || !resultsPanel.activeSelf)) SetPaused(!paused);
        }

        public void SetPaused(bool on)
        {
            if (paused == on) return;
            paused = on;
            bool mp = RaceFlow.IsMultiplayer;
            if (!mp) Time.timeScale = on ? 0f : 1f;
            pausePanel.SetActive(on);
            if (on)
            {
                var info = pausePanel.transform.Find("PausePanel/PauseInfo")?.GetComponent<TextMeshProUGUI>();
                if (info != null) info.text = mp ? "Çok oyunculu: yarış arka planda sürüyor." : "";
                pauseRestart.gameObject.SetActive(RaceFlow.CanRematch && !mp);
                UIFactory.LabelOf(pauseExit).text = RaceFlow.ExitLabel;
                UIFactory.ChainVertical(pauseResume, pauseRestart, pauseExit);
                EventSystem.current?.SetSelectedGameObject(pauseResume.gameObject);
            }
        }

        private void OnRaceFinished()
        {
            SetPaused(false);
            int i = 0;
            foreach (var p in Manager.Standings)
            {
                if (i >= resultRows.Count) break;
                var cells = resultRows[i];
                bool me = p == Player;
                cells[0].text = p.Position.ToString();
                cells[0].color = p.Position == 1 ? UIStyle.Yellow : UIStyle.Text;
                cells[1].text = p.DisplayName + (p.IsBot ? $" <size=70%><color=#{UIStyle.Hex(UIStyle.TextMuted)}>BOT</color></size>" : "");
                var stats = p.Controller != null ? p.Controller.Stats : null;
                cells[2].text = stats != null ? UIText.SledName(stats.Type) : "-";
                cells[3].text = p.Finished ? $"<mspace=0.56em>{UIStyle.FormatTime(p.TotalTime)}</mspace>" : $"<color=#{UIStyle.Hex(UIStyle.TextMuted)}>bitiremedi</color>";
                cells[4].text = $"<mspace=0.56em>{UIStyle.FormatTime(p.BestLapTime)}</mspace>";
                resultRowBgs[i].color = me ? new Color(0.45f, 0.22f, 0.06f, 0.95f) : (i % 2 == 0 ? UIStyle.Row : UIStyle.RowAlt);
                resultRowBgs[i].gameObject.SetActive(true);
                i++;
            }
            for (; i < resultRows.Count; i++) resultRowBgs[i].gameObject.SetActive(false);

            resultsSubtitle.text = Player == null ? "" :
                Player.Finished ? $"{Player.FinishOrder}. oldun!  Toplam {UIStyle.FormatTime(Player.TotalTime)}" : "Süre doldu, yarışı bitiremedin.";
            bool canRematch = RaceFlow.CanRematch;
            resultsRematch.gameObject.SetActive(canRematch);
            UIFactory.LabelOf(resultsExit).text = RaceFlow.ExitLabel;
            resultsWaiting.text = canRematch ? "" : "Yeni yarışı host başlatır.";
            resultsPanel.SetActive(true);
            EventSystem.current?.SetSelectedGameObject((canRematch ? resultsRematch : resultsExit).gameObject);
        }

        /// <summary>Automation / screenshots.</summary>
        public bool ResultsVisible => resultsPanel != null && resultsPanel.activeSelf;
    }

    /// <summary>Keeps the minimap's dark outline in sync with the track line (set once by RaceHud).</summary>
    public class MapOutlineLink : MonoBehaviour
    {
        public UILineGraphic Outline;
    }
}
