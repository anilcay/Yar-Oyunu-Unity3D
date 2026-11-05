using System.Collections.Generic;
using LugeRush.Network;
using LugeRush.Race;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>
    /// Main menu scene: builds every panel in code (uGUI + TMP), handles keyboard/gamepad navigation, the back
    /// stack, toasts and the hand-off to the network session (single player and lobby).
    /// </summary>
    public class MainMenuController : MonoBehaviour
    {
        public static MainMenuController Instance { get; private set; }

        public SledPreviewStage Stage;

        private readonly Dictionary<string, MenuPanel> panels = new Dictionary<string, MenuPanel>();
        private readonly Stack<MenuPanel> history = new Stack<MenuPanel>();
        private MenuPanel current;
        private Canvas canvas;
        private TextMeshProUGUI footer, toastText, title, subtitle;
        private Image toast;
        private float toastHide;
        private EventSystem eventSystem;
        private InputSystemUIInputModule module;
        private float nextTick;
        private bool booting;

        public MenuPanel Current => current;

        private void Awake()
        {
            // Played directly in the editor: go through Boot first (services, NetworkManager).
            if (NetworkSessionService.Instance == null && GameContent.Instance != null &&
                Application.CanStreamedLevelBeLoaded(GameContent.Instance.BootScene))
            {
                booting = true;
                SceneManager.LoadScene(GameContent.Instance.BootScene);
                return;
            }
            Instance = this;
            Time.timeScale = 1f;
            if (Stage == null) Stage = FindFirstObjectByType<SledPreviewStage>();
            if (Stage == null) Stage = new GameObject("PreviewStage").AddComponent<SledPreviewStage>();
            Stage.Build();
            BuildUi();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            var svc = NetworkSessionService.Instance;
            if (svc != null) svc.Notice -= ShowNotice;
        }

        private void Start()
        {
            if (booting) return;
            var svc = NetworkSessionService.Instance;
            if (svc != null) svc.Notice += ShowNotice;
            var gs = GameSettings.Current;
            Stage.Show(gs.Sled, gs.Color);

            if (svc != null && svc.InSession && NetworkLobby.Instance != null)
            {
                // Back from a race to the lobby.
                history.Push(panels["main"]);
                Show("lobby", false);
            }
            else ShowRoot();

            if (svc != null && !string.IsNullOrEmpty(svc.PendingMenuMessage))
            {
                ShowNotice(svc.PendingMenuMessage);
                svc.PendingMenuMessage = null;
            }
        }

        private void BuildUi()
        {
            eventSystem = UIFactory.EnsureEventSystem();
            module = eventSystem.GetComponent<InputSystemUIInputModule>();
            canvas = UIFactory.CreateCanvas("MenuCanvas", 0, transform);
            var root = canvas.transform;

            title = UIFactory.Text(root, "LUGE <color=#" + UIStyle.Hex(UIStyle.Accent) + ">RUSH</color>", UIStyle.TitleSize, UIStyle.Text, TextAlignmentOptions.MidlineLeft, true, "Title");
            title.fontStyle = FontStyles.Bold | FontStyles.Italic;
            title.outlineWidth = 0.12f;
            title.outlineColor = new Color32(10, 16, 28, 200);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(88, -40), new Vector2(1000, 130));
            subtitle = UIFactory.Text(root, "yaz kızağı yarışı", UIStyle.SmallSize + 2, UIStyle.TextMuted, TextAlignmentOptions.MidlineLeft, false, "Subtitle");
            subtitle.characterSpacing = 12f;
            UIFactory.Place(subtitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(96, -160), new Vector2(800, 36));

            Add(new MainPanel(), "ANA MENÜ");
            Add(new SledSelectPanel(), "KIZAK SEÇ");
            Add(new RaceOptionsPanel(), "YARIŞ AYARLARI");
            Add(new MultiplayerPanel(), "ÇOK OYUNCULU");
            Add(new LobbyPanel(), "LOBİ");
            Add(new SettingsPanel(), "AYARLAR");

            footer = UIFactory.Text(root, "", UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.MidlineLeft, false, "Footer");
            footer.outlineWidth = 0.2f;
            footer.outlineColor = new Color32(8, 12, 20, 200);
            UIFactory.Place(footer.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(92, 44), new Vector2(1740, 40));
            var help = UIFactory.Text(root, "↑↓ Seç    ←→ Değiştir    Enter / A Onayla    Esc / B Geri", UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.MidlineRight, false, "Help");
            help.outlineWidth = 0.2f;
            help.outlineColor = new Color32(8, 12, 20, 200);
            help.color = UIStyle.Text;
            UIFactory.Place(help.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-90, 44), new Vector2(1000, 40));

            toast = UIFactory.Panel(root, "Toast", new Color(0.10f, 0.12f, 0.18f, 0.96f));
            UIFactory.Place(toast.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -50), new Vector2(880, 90));
            var bar = UIFactory.Image(toast.transform, "Bar", UIStyle.Accent, UIStyle.RoundedSmall);
            UIFactory.Place(bar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(8, 46));
            toastText = UIFactory.Text(toast.transform, "", UIStyle.SmallSize + 2, UIStyle.Text, TextAlignmentOptions.MidlineLeft);
            toastText.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Stretch(toastText.rectTransform, 40, 6, 24, 6);
            toast.gameObject.SetActive(false);
        }

        private void Add(MenuPanel panel, string header)
        {
            panel.Create(this, canvas.transform, header);
            panels[panel.Id] = panel;
        }

        // ------------------------------------------------------------------ Navigation

        public void ShowRoot()
        {
            history.Clear();
            Show("main", false);
        }

        public void Show(string id, bool pushHistory = true)
        {
            if (!panels.TryGetValue(id, out var next)) return;
            if (current != null && current != next)
            {
                if (pushHistory) history.Push(current);
                current.OnHide();
                current.Root.gameObject.SetActive(false);
            }
            current = next;
            current.Root.gameObject.SetActive(true);
            current.OnShow();
            Stage.SledVisible = current.ShowsSled;
            title.gameObject.SetActive(!current.HidesTitle);
            subtitle.gameObject.SetActive(!current.HidesTitle);
            if (current.HidesTitle) current.Root.offsetMax = new Vector2(current.Root.offsetMax.x, -60);
            Select(current.FirstSelected);
        }

        public void Back()
        {
            if (history.Count == 0) { ShowRoot(); return; }
            var prev = history.Pop();
            if (current != null)
            {
                current.OnHide();
                current.Root.gameObject.SetActive(false);
            }
            current = null;
            Show(prev.Id, false);
        }

        public void OpenSledSelect(bool fromLobby)
        {
            ((SledSelectPanel)panels["sled"]).FromLobby = fromLobby;
            Show("sled");
        }

        public void StartSingleSetup() => OpenSledSelect(false);

        private void Select(Selectable s)
        {
            if (eventSystem == null) return;
            eventSystem.SetSelectedGameObject(null);
            if (s != null) eventSystem.SetSelectedGameObject(s.gameObject);
        }

        private void Update()
        {
            if (booting) return;
            if (current == null) return;
            if (module != null && module.cancel != null && module.cancel.action != null && module.cancel.action.WasPressedThisFrame())
            {
                var sel = eventSystem.currentSelectedGameObject;
                bool typing = sel != null && sel.TryGetComponent(out TMP_InputField f) && f.isFocused;
                if (!typing) current.OnBack();
            }
            // Keep a selection for keyboard/gamepad users after mouse clicks on empty space.
            var selected = eventSystem.currentSelectedGameObject;
            if ((selected == null || !selected.activeInHierarchy) && module != null && module.move != null &&
                module.move.action != null && module.move.action.ReadValue<Vector2>().sqrMagnitude > 0.25f)
                Select(current.FirstSelected);

            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + 0.25f;
                current.Tick();
                UpdateFooter();
                // Session ended while in the lobby (host left / kicked).
                var svc = NetworkSessionService.Instance;
                if (current.Id == "lobby" && (svc == null || !svc.InSession)) ShowRoot();
            }
            if (toast.gameObject.activeSelf && Time.unscaledTime > toastHide) toast.gameObject.SetActive(false);
        }

        private void UpdateFooter()
        {
            var svc = NetworkSessionService.Instance;
            string net = svc == null ? "ağ: yok" :
                svc.Services == ServicesState.Ready ? $"<color=#{UIStyle.Hex(UIStyle.Good)}>●</color> çevrimiçi servisler hazır" :
                svc.Services == ServicesState.Initializing ? "○ çevrimiçi servisler başlatılıyor..." :
                $"<color=#{UIStyle.Hex(UIStyle.Yellow)}>●</color> doğrudan IP modu";
            footer.text = $"v{Application.version}   ·   {net}";
        }

        public void ShowNotice(string message)
        {
            if (toast == null || string.IsNullOrEmpty(message)) return;
            toastText.text = message;
            toast.gameObject.SetActive(true);
            toastHide = Time.unscaledTime + 5f;
        }

        // ------------------------------------------------------------------ Actions

        /// <summary>Single player uses the same network path: local host, lobby with only us, race starts at once.</summary>
        public void LaunchSinglePlayer()
        {
            var svc = NetworkSessionService.Instance;
            var gs = GameSettings.Current;
            gs.ApplyToRaceSession();
            if (svc == null)
            {
                // No Boot (should not happen in builds): load the scene offline.
                var tracks = GameContent.Instance.Tracks;
                RaceSession.NetworkRoster = false;
                SceneManager.LoadScene(tracks.Find(gs.TrackId).SceneName);
                return;
            }
            if (!svc.StartSinglePlayer()) return;
            StartCoroutine(StartWhenLobbyReady(svc));
        }

        private System.Collections.IEnumerator StartWhenLobbyReady(NetworkSessionService svc)
        {
            float t = 0f;
            while ((NetworkLobby.Instance == null || NetworkLobby.Instance.Players.Count == 0) && t < 5f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!svc.StartRace()) svc.Leave("Yarış başlatılamadı.");
        }
    }
}
