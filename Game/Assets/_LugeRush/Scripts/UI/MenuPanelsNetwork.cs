using System.Collections.Generic;
using LugeRush.Network;
using LugeRush.Race;
using LugeRush.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>Host / join by code / quick join list (Sessions + Relay) and direct IP fallback.</summary>
    public class MultiplayerPanel : MenuPanel
    {
        public override string Id => "multiplayer";
        public override float Width => 900f;
        public override bool HidesTitle => true;

        private TextMeshProUGUI status, roomsInfo;
        private Button host, joinCode, refresh, joinIp, hostIp, back;
        private TMP_InputField codeInput, ipInput;
        private RectTransform roomList;
        private readonly List<Button> roomButtons = new List<Button>();
        private bool querying;
        private int navigationMask = -1;

        protected override void Build(RectTransform c)
        {
            status = UIFactory.Text(c, "", UIStyle.SmallSize, UIStyle.TextMuted);
            status.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Layout(status, -1, 62);

            host = UIFactory.Button(c, "Oda Kur", HostOnline, UIStyle.ButtonHeight, true);

            var codeRow = Row(c, UIStyle.RowHeight);
            codeInput = UIFactory.Input(codeRow, "Oda kodu", UIStyle.RowHeight, 12);
            codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            UIFactory.Layout(codeInput, 420, UIStyle.RowHeight, 1);
            joinCode = UIFactory.Button(codeRow, "Kodla Katıl", JoinByCode, UIStyle.RowHeight);
            UIFactory.Layout(joinCode, 300, UIStyle.RowHeight);

            var quickRow = Row(c, 40);
            roomsInfo = UIFactory.Text(quickRow, "Hızlı katıl: açık odalar", UIStyle.SmallSize, UIStyle.TextMuted);
            UIFactory.Layout(roomsInfo, -1, 40, 1);
            refresh = UIFactory.Button(quickRow, "Yenile", Refresh, 40, false, UIStyle.SmallSize);
            UIFactory.Layout(refresh, 160, 40);

            roomList = UIFactory.Rect("Rooms", c);
            UIFactory.VLayout(roomList.gameObject, 6);
            UIFactory.Layout(roomList, -1, 3 * 46 + 2 * 6);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var b = UIFactory.Button(roomList, "-", () => JoinRoom(index), 46, false, UIStyle.SmallSize);
                b.gameObject.SetActive(false);
                roomButtons.Add(b);
            }

            Spacer(c, 4);
            var ipHeader = UIFactory.Text(c, "DOĞRUDAN IP", UIStyle.SmallSize, UIStyle.Secondary);
            ipHeader.fontStyle = FontStyles.Bold;
            UIFactory.Layout(ipHeader, -1, 30);
            var ipRow = Row(c, UIStyle.RowHeight);
            ipInput = UIFactory.Input(ipRow, "IP:port (ör. 192.168.1.20:7777)", UIStyle.RowHeight, 32);
            UIFactory.Layout(ipInput, 420, UIStyle.RowHeight, 1);
            joinIp = UIFactory.Button(ipRow, "IP ile Katıl", JoinIp, UIStyle.RowHeight);
            UIFactory.Layout(joinIp, 300, UIStyle.RowHeight);
            hostIp = UIFactory.Button(c, $"IP ile Oda Kur (port {NetworkSessionService.DefaultPort})", HostIp, UIStyle.RowHeight);

            FlexSpacer(c);
            back = UIFactory.Button(c, "Geri", () => Menu.Back());
        }

        public override Selectable FirstSelected => host;

        public override void OnShow()
        {
            ipInput.text = GameSettings.Current.LastJoinAddress;
            var svc = NetworkSessionService.Instance;
            if (svc != null && svc.Services == ServicesState.NotStarted) _ = svc.InitializeServicesAsync();
            Tick();
            if (svc != null && svc.Services == ServicesState.Ready) Refresh();
        }

        public override void Tick()
        {
            var svc = NetworkSessionService.Instance;
            bool ready = svc != null && svc.Services == ServicesState.Ready;
            bool busy = svc == null || svc.IsBusy || svc.InSession;
            string state = svc == null ? "Ağ servisi yok (Boot sahnesinden başlatın)." : svc.ServicesMessage;
            status.text = ready ? $"<color=#{UIStyle.Hex(UIStyle.Good)}>●</color> {state}" : $"<color=#{UIStyle.Hex(UIStyle.Yellow)}>●</color> {state}";
            UIFactory.SetInteractable(host, !busy);
            UIFactory.LabelOf(host).text = ready ? "Oda Kur (çevrimiçi)" : "Oda Kur (doğrudan IP)";
            UIFactory.SetInteractable(joinCode, ready && !busy);
            UIFactory.SetInteractable(codeInput, ready && !busy);
            UIFactory.SetInteractable(refresh, ready && !busy && !querying);
            UIFactory.SetInteractable(joinIp, !busy);
            UIFactory.SetInteractable(hostIp, !busy);
            if (!ready) roomsInfo.text = "Hızlı katıl: çevrimiçi servis gerekli";
            ChainNavigation();
        }

        /// <summary>Keyboard/gamepad order over everything usable right now (disabled items must be skipped explicitly).</summary>
        private void ChainNavigation()
        {
            var all = new List<Selectable> { host, codeInput, joinCode, refresh };
            all.AddRange(roomButtons);
            all.AddRange(new Selectable[] { ipInput, joinIp, hostIp, back });
            int mask = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i].gameObject.activeSelf && all[i].interactable) mask |= 1 << i;
            if (mask == navigationMask) return;
            navigationMask = mask;
            UIFactory.ChainVertical(all.FindAll(s => s.interactable).ToArray());
        }

        private async void HostOnline()
        {
            var svc = NetworkSessionService.Instance;
            if (svc == null) return;
            bool ok = await svc.HostOnlineAsync($"{GameSettings.Current.PlayerName} odası");
            if (ok) Menu.Show("lobby", false);
        }

        private async void JoinByCode()
        {
            var svc = NetworkSessionService.Instance;
            if (svc == null) return;
            bool ok = await svc.JoinByCodeAsync(codeInput.text);
            if (ok) Menu.Show("lobby", false);
        }

        private List<OpenRoom> rooms = new List<OpenRoom>();

        private async void Refresh()
        {
            var svc = NetworkSessionService.Instance;
            if (svc == null || querying) return;
            querying = true;
            roomsInfo.text = "Odalar aranıyor...";
            rooms = await svc.QueryOpenRoomsAsync();
            querying = false;
            roomsInfo.text = rooms.Count == 0 ? "Açık oda yok. Yeni bir oda kurun!" : $"Hızlı katıl: {rooms.Count} açık oda";
            for (int i = 0; i < roomButtons.Count; i++)
            {
                bool on = i < rooms.Count;
                roomButtons[i].gameObject.SetActive(on);
                if (on) UIFactory.LabelOf(roomButtons[i]).text = $"{rooms[i].Name}   <color=#{UIStyle.Hex(UIStyle.TextMuted)}>{rooms[i].Players}/{rooms[i].MaxPlayers}</color>";
            }
        }

        private async void JoinRoom(int index)
        {
            var svc = NetworkSessionService.Instance;
            if (svc == null || index >= rooms.Count) return;
            bool ok = await svc.JoinByIdAsync(rooms[index].Id);
            if (ok) Menu.Show("lobby", false);
        }

        private void JoinIp()
        {
            var svc = NetworkSessionService.Instance;
            if (svc == null) return;
            if (!LaunchArgs.TryParseAddress(ipInput.text, NetworkSessionService.DefaultPort, out string ip, out ushort port))
            {
                Menu.ShowNotice("Geçersiz adres. Biçim: 192.168.1.20:7777");
                return;
            }
            GameSettings.Current.LastJoinAddress = $"{ip}:{port}";
            GameSettings.Current.Save();
            if (svc.JoinDirect(ip, port)) Menu.Show("lobby", false);
        }

        private void HostIp()
        {
            var svc = NetworkSessionService.Instance;
            if (svc != null && svc.HostDirect(NetworkSessionService.DefaultPort)) Menu.Show("lobby", false);
        }
    }

    /// <summary>Lobby: players (name, sled, colour, ready), host settings, start.</summary>
    public class LobbyPanel : MenuPanel
    {
        public override string Id => "lobby";
        public override bool ShowsSled => false;
        public override bool HidesTitle => true;
        public override float Width => 1740f;

        private TextMeshProUGUI roomLine, readyLine;
        private readonly List<(Image bg, Image swatch, TextMeshProUGUI name, TextMeshProUGUI sled, TextMeshProUGUI state)> rows =
            new List<(Image, Image, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI)>();
        private UIStepper map, laps, bots, difficulty;
        private TextMeshProUGUI settingsNote;
        private Button changeSled, ready, start, leave;
        private bool suppress;

        protected override void Build(RectTransform c)
        {
            roomLine = UIFactory.Text(c, "", UIStyle.BodySize, UIStyle.Text);
            UIFactory.Layout(roomLine, -1, 44);

            var columns = UIFactory.Rect("Columns", c);
            var h = UIFactory.HLayout(columns.gameObject, 40);
            h.childForceExpandHeight = true;
            UIFactory.Layout(columns, -1, -1, -1, 1);

            // Players
            var list = UIFactory.Rect("Players", columns);
            UIFactory.VLayout(list.gameObject, 6);
            UIFactory.Layout(list, 980, -1);
            var head = UIFactory.Text(list, "OYUNCULAR", UIStyle.SmallSize, UIStyle.Secondary);
            head.fontStyle = FontStyles.Bold;
            UIFactory.Layout(head, -1, 30);
            for (int i = 0; i < LobbyRules.Capacity; i++)
            {
                var bg = UIFactory.Image(list, "Row" + i, i % 2 == 0 ? UIStyle.Row : UIStyle.RowAlt, UIStyle.RoundedSmall);
                UIFactory.Layout(bg, -1, 50);
                var hl = UIFactory.HLayout(bg.gameObject, 16, new RectOffset(16, 20, 0, 0));
                hl.childForceExpandHeight = false;
                hl.childAlignment = TextAnchor.MiddleLeft;
                var num = UIFactory.Text(bg.transform, (i + 1).ToString(), UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.Center, true);
                UIFactory.Layout(num, 30, 40);
                var sw = UIFactory.Image(bg.transform, "Swatch", Color.white, UIStyle.Circle);
                UIFactory.Layout(sw, 26, 26);
                var name = UIFactory.Text(bg.transform, "", UIStyle.BodySize, UIStyle.Text);
                UIFactory.Layout(name, -1, 40, 1);
                var sled = UIFactory.Text(bg.transform, "", UIStyle.SmallSize, UIStyle.TextMuted);
                UIFactory.Layout(sled, 260, 40);
                var state = UIFactory.Text(bg.transform, "", UIStyle.SmallSize, UIStyle.Text, TextAlignmentOptions.MidlineRight);
                state.fontStyle = FontStyles.Bold;
                UIFactory.Layout(state, 150, 40);
                rows.Add((bg, sw, name, sled, state));
            }

            // Settings + actions
            var side = UIFactory.Rect("Side", columns);
            UIFactory.VLayout(side.gameObject, 12);
            UIFactory.Layout(side, 640, -1, 1);
            var sh = UIFactory.Text(side, "YARIŞ AYARLARI", UIStyle.SmallSize, UIStyle.Secondary);
            sh.fontStyle = FontStyles.Bold;
            UIFactory.Layout(sh, -1, 30);
            map = UIFactory.Stepper(side, "Harita", UIStyle.RowHeight, 150);
            laps = UIFactory.Stepper(side, "Tur", UIStyle.RowHeight, 150);
            laps.Wrap = false;
            bots = UIFactory.Stepper(side, "Bot", UIStyle.RowHeight, 150);
            bots.Wrap = false;
            difficulty = UIFactory.Stepper(side, "Zorluk", UIStyle.RowHeight, 150);
            laps.SetRange(RaceConfig.MinLaps, RaceConfig.MaxLaps, 3);
            bots.SetRange(0, RaceConfig.MaxSleds - 1, 7);
            difficulty.SetOptions(UIText.DifficultyNames, 1);
            map.Changed += _ => PushSettings();
            laps.Changed += _ => PushSettings();
            bots.Changed += _ => PushSettings();
            difficulty.Changed += _ => PushSettings();
            settingsNote = UIFactory.Text(side, "", UIStyle.SmallSize, UIStyle.TextMuted);
            settingsNote.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Layout(settingsNote, -1, 60);
            FlexSpacer(side);
            readyLine = UIFactory.Text(side, "", UIStyle.SmallSize, UIStyle.TextMuted);
            UIFactory.Layout(readyLine, -1, 32);
            changeSled = UIFactory.Button(side, "Kızak Değiştir", ChangeSled);
            ready = UIFactory.Button(side, "Hazır", ToggleReady);
            start = UIFactory.Button(side, "Yarışı Başlat", StartRace, UIStyle.ButtonHeight, true);
            leave = UIFactory.Button(side, "Odadan Ayrıl", Leave);
            UIFactory.ChainVertical(map, laps, bots, difficulty, changeSled, ready, start, leave);
        }

        public override Selectable FirstSelected
        {
            get
            {
                var svc = NetworkSessionService.Instance;
                if (svc != null && svc.IsHost) return start.interactable ? start : changeSled;
                return ready;
            }
        }

        public override void OnShow()
        {
            NetworkLobby.Changed += Refresh;
            Refresh();
        }

        public override void OnHide() => NetworkLobby.Changed -= Refresh;

        public override void OnBack() => Leave();

        public override void Tick() => Refresh();

        private void Refresh()
        {
            if (!IsVisible) return;
            var svc = NetworkSessionService.Instance;
            var lobby = NetworkLobby.Instance;
            bool host = svc != null && svc.IsHost;

            if (svc == null || !svc.InSession)
            {
                roomLine.text = "Bağlantı yok.";
                return;
            }
            if (lobby == null)
            {
                roomLine.text = "Odaya bağlanılıyor...";
                for (int i = 0; i < rows.Count; i++) SetRow(i, "", "", "", Color.clear, Color.clear, false);
                foreach (var b in new[] { changeSled, ready, start }) UIFactory.SetInteractable(b, false);
                return;
            }

            string accent = UIStyle.Hex(UIStyle.Accent);
            if (!string.IsNullOrEmpty(svc.SessionCode))
                roomLine.text = $"ODA KODU  <color=#{accent}><b>{svc.SessionCode}</b></color>   <size=80%><color=#{UIStyle.Hex(UIStyle.TextMuted)}>arkadaşlarınız bu kodla katılabilir (Relay)</color></size>";
            else
                roomLine.text = host
                    ? $"DOĞRUDAN IP  <color=#{accent}><b>{svc.DirectAddressInfo}</b></color>   <size=80%><color=#{UIStyle.Hex(UIStyle.TextMuted)}>aynı ağdakiler bu adresle katılır</color></size>"
                    : $"BAĞLANILAN HOST  <color=#{accent}><b>{svc.DirectAddressInfo}</b></color>";

            var members = lobby.Members();
            var catalog = GameContent.Instance != null ? GameContent.Instance.Sleds : null;
            int bots = lobby.EffectiveBots;
            ulong local = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < members.Count)
                {
                    var m = members[i];
                    string stateText = m.IsHost ? $"<color=#{UIStyle.Hex(UIStyle.Yellow)}>HOST</color>" :
                        m.Ready ? $"<color=#{UIStyle.Hex(UIStyle.Good)}>HAZIR</color>" : $"<color=#{UIStyle.Hex(UIStyle.TextMuted)}>BEKLİYOR</color>";
                    string name = m.ClientId == local ? $"{m.Name} <color=#{UIStyle.Hex(UIStyle.TextMuted)}>(sen)</color>" : m.Name;
                    SetRow(i, name, UIText.SledLabel(m.Sled), stateText, catalog != null ? catalog.GetBodyColor(m.Color) : Color.white,
                        m.ClientId == local ? new Color(0.20f, 0.17f, 0.14f, 0.98f) : (i % 2 == 0 ? UIStyle.Row : UIStyle.RowAlt), true);
                }
                else if (i < members.Count + bots)
                {
                    int b = i - members.Count;
                    SetRow(i, $"<color=#{UIStyle.Hex(UIStyle.TextMuted)}>{LobbyRules.BotNames[b % LobbyRules.BotNames.Length]}</color>",
                        "rastgele kızak", $"<color=#{UIStyle.Hex(UIStyle.Secondary)}>BOT</color>", new Color(0.5f, 0.55f, 0.62f), i % 2 == 0 ? UIStyle.Row : UIStyle.RowAlt, true);
                }
                else SetRow(i, $"<color=#{UIStyle.Hex(UIStyle.TextDisabled)}>Boş</color>", "", "", Color.clear, new Color(0.08f, 0.11f, 0.17f, 0.6f), false);
            }

            // Settings
            var s = lobby.Settings.Value;
            var tracks = GameContent.Instance != null ? GameContent.Instance.Tracks : null;
            suppress = true;
            var names = new List<string>();
            if (tracks != null) foreach (var e in tracks.Tracks) names.Add(UIText.TrackLabel(e));
            if (map.Count != names.Count) map.SetOptions(names, s.TrackIndex, i => tracks != null && TrackCatalog.IsAvailable(tracks.Get(i)));
            else map.SetIndexWithoutNotify(s.TrackIndex);
            laps.SetIndexWithoutNotify(s.Laps - RaceConfig.MinLaps);
            this.bots.SetIndexWithoutNotify(s.Bots);
            difficulty.SetIndexWithoutNotify(s.Difficulty);
            suppress = false;
            foreach (var st in new[] { map, laps, this.bots, difficulty })
            {
                st.ReadOnly = !host;
                st.interactable = host;
            }
            int effective = lobby.EffectiveBots;
            settingsNote.text = host
                ? (effective < s.Bots ? $"Boş yerler botla dolar: {effective} bot (oda dolu)." : "Boş yerler botla dolar. Kapasite 8 kızak.")
                : "Ayarları host belirler.";

            int readyCount = LobbyRules.ReadyCount(members);
            readyLine.text = $"Hazır: {readyCount}/{members.Count}   ·   Kızak: {members.Count + effective}/{LobbyRules.Capacity}";

            bool inRace = lobby.InRace.Value;
            UIFactory.SetInteractable(changeSled, !inRace);
            bool meReady = lobby.TryGetLocal(out var me) && me.Ready;
            UIFactory.LabelOf(ready).text = meReady ? "Hazır Değilim" : "Hazırım";
            ready.gameObject.SetActive(!host);
            start.gameObject.SetActive(host);
            bool canStart = svc.CanStartRace(out string reason);
            UIFactory.SetInteractable(start, canStart && !inRace);
            UIFactory.LabelOf(start).text = canStart ? "Yarışı Başlat" : $"Yarışı Başlat <size=70%>({reason})</size>";
            UIFactory.SetInteractable(ready, !inRace);
            UIFactory.ChainVertical(host ? new Selectable[] { map, laps, this.bots, difficulty, changeSled, start, leave } : new Selectable[] { changeSled, ready, leave });
        }

        private void SetRow(int i, string name, string sled, string state, Color swatch, Color bg, bool on)
        {
            var r = rows[i];
            r.name.text = name;
            r.sled.text = sled;
            r.state.text = state;
            r.swatch.color = swatch;
            r.bg.color = bg;
            r.swatch.enabled = on;
        }

        private void PushSettings()
        {
            if (suppress) return;
            var lobby = NetworkLobby.Instance;
            var svc = NetworkSessionService.Instance;
            if (lobby == null || svc == null || !svc.IsHost) return;
            lobby.ServerSetSettings(new LobbySettingsData
            {
                TrackIndex = map.Index,
                Laps = laps.Index + RaceConfig.MinLaps,
                Bots = bots.Index,
                Difficulty = difficulty.Index
            });
            var gs = GameSettings.Current;
            var tracks = GameContent.Instance != null ? GameContent.Instance.Tracks : null;
            if (tracks != null && tracks.Get(map.Index) != null) gs.TrackId = tracks.Get(map.Index).TrackId;
            gs.Laps = laps.Index + RaceConfig.MinLaps;
            gs.Bots = bots.Index;
            gs.Difficulty = (BotDifficulty)difficulty.Index;
            gs.Save();
        }

        private void ChangeSled()
        {
            Menu.OpenSledSelect(true);
        }

        private void ToggleReady()
        {
            var lobby = NetworkLobby.Instance;
            if (lobby == null) return;
            bool meReady = lobby.TryGetLocal(out var me) && me.Ready;
            lobby.SetLocalReady(!meReady);
        }

        private void StartRace() => NetworkSessionService.Instance?.StartRace();

        private void Leave()
        {
            NetworkSessionService.Instance?.Leave();
            Menu.ShowRoot();
        }
    }

    /// <summary>Graphics, audio, language, name, controls. Every change is applied and saved immediately.</summary>
    public class SettingsPanel : MenuPanel
    {
        public override string Id => "settings";
        public override bool ShowsSled => false;
        public override bool HidesTitle => true;
        public override float Width => 1740f;

        private TMP_InputField nameInput;
        private UIStepper resolution, screenMode, quality, master, music, sfx, language;
        private readonly List<Vector2Int> resolutions = new List<Vector2Int>();
        private Button back;

        protected override void Build(RectTransform c)
        {
            var columns = UIFactory.Rect("Columns", c);
            var h = UIFactory.HLayout(columns.gameObject, 50);
            h.childForceExpandHeight = true;
            UIFactory.Layout(columns, -1, -1, -1, 1);

            var left = UIFactory.Rect("Left", columns);
            UIFactory.VLayout(left.gameObject, 12);
            UIFactory.Layout(left, 690, -1);
            var nameRow = Row(left, UIStyle.RowHeight);
            var nameLabel = UIFactory.Text(nameRow, "Oyuncu adı", UIStyle.BodySize, UIStyle.TextMuted);
            UIFactory.Layout(nameLabel, 300, UIStyle.RowHeight);
            nameInput = UIFactory.Input(nameRow, "Adınız", UIStyle.RowHeight, LobbyRules.MaxNameLength);
            UIFactory.Layout(nameInput, -1, UIStyle.RowHeight, 1);
            nameInput.onEndEdit.AddListener(v =>
            {
                GameSettings.Current.PlayerName = LobbyRules.SanitizeName(v);
                nameInput.SetTextWithoutNotify(GameSettings.Current.PlayerName);
                Save();
            });

            resolution = UIFactory.Stepper(left, "Çözünürlük");
            screenMode = UIFactory.Stepper(left, "Ekran");
            quality = UIFactory.Stepper(left, "Grafik kalitesi");
            master = UIFactory.Stepper(left, "Ana ses");
            music = UIFactory.Stepper(left, "Müzik");
            sfx = UIFactory.Stepper(left, "Efektler");
            language = UIFactory.Stepper(left, "Dil");
            foreach (var st in new[] { master, music, sfx }) st.Wrap = false;

            screenMode.SetOptions(new[] { "Tam ekran", "Pencere" }, 0);
            quality.SetOptions(QualitySettings.names, QualitySettings.GetQualityLevel());
            master.SetRange(0, 10, 8, v => v == 0 ? "Kapalı" : $"%{v * 10}");
            music.SetRange(0, 10, 7, v => v == 0 ? "Kapalı" : $"%{v * 10}");
            sfx.SetRange(0, 10, 8, v => v == 0 ? "Kapalı" : $"%{v * 10}");
            language.SetOptions(new[] { "Türkçe", "English (yakında)" }, 0, i => i == 0);

            resolution.Changed += i =>
            {
                if (i < resolutions.Count) { GameSettings.Current.ResolutionWidth = resolutions[i].x; GameSettings.Current.ResolutionHeight = resolutions[i].y; }
                Save();
            };
            screenMode.Changed += i => { GameSettings.Current.Fullscreen = i == 0; Save(); };
            quality.Changed += i => { GameSettings.Current.QualityLevel = i; Save(); };
            master.Changed += i => { GameSettings.Current.MasterVolume = i / 10f; Save(); };
            music.Changed += i => { GameSettings.Current.MusicVolume = i / 10f; Save(); };
            sfx.Changed += i => { GameSettings.Current.SfxVolume = i / 10f; Save(); };
            language.Changed += i =>
            {
                GameSettings.Current.Language = (GameLanguage)i;
                if (i != 0) Menu.ShowNotice("English is coming with the localisation update. Oyun şimdilik Türkçe.");
                Save();
            };

            FlexSpacer(left);
            back = UIFactory.Button(left, "Geri", () => Menu.Back());

            var right = UIFactory.Panel(columns, "Controls", UIStyle.PanelBgLight);
            UIFactory.Layout(right, -1, -1, 1);
            UIFactory.VLayout(right.gameObject, 12, UIFactory.Pad(28, 22));
            var ch = UIFactory.Text(right.transform, "KONTROLLER", UIStyle.SmallSize, UIStyle.Secondary);
            ch.fontStyle = FontStyles.Bold;
            UIFactory.Layout(ch, -1, 30);
            var table = UIFactory.Text(right.transform, UIText.ControlsTable, UIStyle.SmallSize, UIStyle.Text, TextAlignmentOptions.TopLeft);
            table.lineSpacing = 18f;
            UIFactory.Layout(table, -1, 420, -1, 1);

            UIFactory.ChainVertical(nameInput, resolution, screenMode, quality, master, music, sfx, language, back);
        }

        public override Selectable FirstSelected => resolution;

        public override void OnShow()
        {
            var gs = GameSettings.Current;
            nameInput.SetTextWithoutNotify(gs.PlayerName);
            resolutions.Clear();
            var labels = new List<string>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (resolutions.Contains(v) || r.width < 1024) continue;
                resolutions.Add(v);
                labels.Add($"{r.width} × {r.height}");
            }
            var cur = new Vector2Int(gs.ResolutionWidth > 0 ? gs.ResolutionWidth : Screen.width, gs.ResolutionHeight > 0 ? gs.ResolutionHeight : Screen.height);
            if (!resolutions.Contains(cur)) { resolutions.Add(cur); labels.Add($"{cur.x} × {cur.y}"); }
            resolution.SetOptions(labels, resolutions.IndexOf(cur));
            screenMode.SetIndexWithoutNotify(gs.Fullscreen ? 0 : 1);
            quality.SetIndexWithoutNotify(gs.QualityLevel >= 0 ? gs.QualityLevel : QualitySettings.GetQualityLevel());
            master.SetIndexWithoutNotify(Mathf.RoundToInt(gs.MasterVolume * 10));
            music.SetIndexWithoutNotify(Mathf.RoundToInt(gs.MusicVolume * 10));
            sfx.SetIndexWithoutNotify(Mathf.RoundToInt(gs.SfxVolume * 10));
            language.SetIndexWithoutNotify((int)gs.Language);
        }

        private static void Save()
        {
            var gs = GameSettings.Current;
            gs.Save();
            gs.Apply();
        }
    }
}
