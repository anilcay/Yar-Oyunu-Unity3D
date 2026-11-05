using System.Collections.Generic;
using LugeRush.Network;
using LugeRush.Race;
using LugeRush.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LugeRush.UI
{
    public class MainPanel : MenuPanel
    {
        public override string Id => "main";
        public override float Width => 620f;
        private TextMeshProUGUI profile;
        private Button single;

        protected override void Build(RectTransform c)
        {
            single = UIFactory.Button(c, "Tek Oyuncu", () => Menu.StartSingleSetup(), 76, true, 34);
            var multi = UIFactory.Button(c, "Çok Oyunculu", () => Menu.Show("multiplayer"), 76, false, 34);
            var settings = UIFactory.Button(c, "Ayarlar", () => Menu.Show("settings"), 76, false, 34);
            var quit = UIFactory.Button(c, "Çıkış", Quit, 76, false, 34);
            UIFactory.ChainVertical(single, multi, settings, quit);
            FlexSpacer(c);
            profile = UIFactory.Text(c, "", UIStyle.SmallSize, UIStyle.TextMuted);
            UIFactory.Layout(profile, -1, 34);
        }

        public override Selectable FirstSelected => single;

        public override void OnShow()
        {
            profile.text = $"Sürücü: <color=#{UIStyle.Hex(UIStyle.Text)}>{GameSettings.Current.PlayerName}</color>   ·   Kızak: {UIText.SledName(GameSettings.Current.Sled)}";
        }

        public override void OnBack() { }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>Sled type + colour with 1–5 stat bars. Used for single player and from the lobby.</summary>
    public class SledSelectPanel : MenuPanel
    {
        public override string Id => "sled";
        /// <summary>True when opened from the lobby: "Tamam" applies the choice to the lobby and returns.</summary>
        public bool FromLobby;

        private UIStepper typeStep, colorStep;
        private TextMeshProUGUI typeLabel, description;
        private Image[] speed, grip, handling, weight, drift;
        private readonly List<Image> swatches = new List<Image>();
        private Button confirm;

        protected override void Build(RectTransform c)
        {
            typeStep = UIFactory.Stepper(c, "Kızak", 66, 180);
            typeStep.Changed += _ => Refresh();
            typeLabel = UIFactory.Text(c, "", UIStyle.SmallSize, UIStyle.Secondary);
            UIFactory.Layout(typeLabel, -1, 30);
            description = UIFactory.Text(c, "", UIStyle.SmallSize, UIStyle.TextMuted);
            description.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Layout(description, -1, 64);

            var stats = UIFactory.Rect("Stats", c);
            UIFactory.VLayout(stats.gameObject, 6);
            UIFactory.Layout(stats, -1, 5 * 34 + 4 * 6);
            speed = UIFactory.StatBar(stats, "Hız", out _);
            grip = UIFactory.StatBar(stats, "Tutuş", out _);
            handling = UIFactory.StatBar(stats, "Dönüş", out _);
            weight = UIFactory.StatBar(stats, "Ağırlık", out _);
            drift = UIFactory.StatBar(stats, "Drift", out _);
            Spacer(c, 6);

            colorStep = UIFactory.Stepper(c, "Renk", 62, 180);
            colorStep.Changed += _ => Refresh();
            var swatchRow = Row(c, 34, 14);
            swatchRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            swatchRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(200, 0, 0, 0);
            var catalog = GameContent.Instance != null ? GameContent.Instance.Sleds : null;
            for (int i = 0; i < 4; i++)
            {
                var s = UIFactory.Image(swatchRow, "Swatch" + i, catalog != null ? catalog.GetBodyColor((SledColor)i) : Color.white, UIStyle.RoundedSmall);
                UIFactory.Layout(s, 70, 30);
                swatches.Add(s);
            }

            FlexSpacer(c);
            var buttons = Row(c, UIStyle.ButtonHeight);
            confirm = UIFactory.Button(buttons, "Devam", Confirm, UIStyle.ButtonHeight, true);
            var back = UIFactory.Button(buttons, "Geri", () => Menu.Back());
            UIFactory.ChainVertical(typeStep, colorStep, confirm);
            var nav = confirm.navigation; nav.selectOnRight = back; confirm.navigation = nav;
            var bnav = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirm, selectOnUp = colorStep, selectOnDown = typeStep };
            back.navigation = bnav;

            var names = new List<string>();
            for (int i = 0; i < 4; i++) names.Add(UIText.SledName((SledType)i));
            typeStep.SetOptions(names, 0);
            colorStep.SetOptions(UIText.ColorNames, 0);
        }

        public override Selectable FirstSelected => typeStep;

        public override void OnShow()
        {
            var gs = GameSettings.Current;
            SledType type = gs.Sled;
            SledColor color = gs.Color;
            if (FromLobby && NetworkLobby.Instance != null && NetworkLobby.Instance.TryGetLocal(out var me))
            {
                type = (SledType)me.Sled;
                color = (SledColor)me.Color;
            }
            typeStep.SetIndexWithoutNotify((int)type);
            colorStep.SetIndexWithoutNotify((int)color);
            UIFactory.LabelOf(confirm).text = FromLobby ? "Tamam" : "Devam";
            Refresh();
        }

        private void Refresh()
        {
            var type = (SledType)typeStep.Index;
            var color = (SledColor)colorStep.Index;
            var catalog = GameContent.Instance != null ? GameContent.Instance.Sleds : null;
            var stats = catalog != null ? catalog.GetStats(type) : null;
            typeLabel.text = UIText.SledTypeNames[(int)type].ToUpper(new System.Globalization.CultureInfo("tr-TR")) + " KIZAK";
            description.text = UIText.SledDescriptions[(int)type];
            UIFactory.SetBar(speed, stats != null ? stats.UiSpeed : 3, UIStyle.Accent);
            UIFactory.SetBar(grip, stats != null ? stats.UiGrip : 3, UIStyle.Secondary);
            UIFactory.SetBar(handling, stats != null ? stats.UiHandling : 3, UIStyle.Secondary);
            UIFactory.SetBar(weight, stats != null ? stats.UiWeight : 3, UIStyle.Yellow);
            UIFactory.SetBar(drift, stats != null ? stats.UiDrift : 3, UIStyle.Accent);
            for (int i = 0; i < swatches.Count; i++)
            {
                bool on = i == (int)color;
                swatches[i].GetComponent<LayoutElement>().preferredHeight = on ? 34 : 22;
                swatches[i].color = new Color(swatches[i].color.r, swatches[i].color.g, swatches[i].color.b, on ? 1f : 0.55f);
            }
            Menu.Stage?.Show(type, color);
        }

        private void Confirm()
        {
            var gs = GameSettings.Current;
            gs.Sled = (SledType)typeStep.Index;
            gs.Color = (SledColor)colorStep.Index;
            gs.Save();
            if (FromLobby)
            {
                NetworkLobby.Instance?.SetLocalSled(gs.Sled, gs.Color);
                Menu.Back();
            }
            else Menu.Show("raceoptions");
        }
    }

    /// <summary>Single player: map, laps (1–10), bots (0–7), difficulty.</summary>
    public class RaceOptionsPanel : MenuPanel
    {
        public override string Id => "raceoptions";
        private UIStepper map, laps, bots, difficulty;
        private TextMeshProUGUI mapInfo, hint;
        private Button start;

        protected override void Build(RectTransform c)
        {
            map = UIFactory.Stepper(c, "Harita", 66, 180);
            map.Changed += _ => Refresh();
            mapInfo = UIFactory.Text(c, "", UIStyle.SmallSize, UIStyle.TextMuted);
            mapInfo.textWrappingMode = TextWrappingModes.Normal;
            UIFactory.Layout(mapInfo, -1, 70);
            laps = UIFactory.Stepper(c, "Tur sayısı", UIStyle.RowHeight, 260);
            laps.Wrap = false;
            bots = UIFactory.Stepper(c, "Bot sayısı", UIStyle.RowHeight, 260);
            bots.Wrap = false;
            difficulty = UIFactory.Stepper(c, "Zorluk", UIStyle.RowHeight, 260);
            laps.SetRange(RaceConfig.MinLaps, RaceConfig.MaxLaps, 3);
            bots.SetRange(0, RaceConfig.MaxSleds - 1, 7);
            difficulty.SetOptions(UIText.DifficultyNames, 1);
            FlexSpacer(c);
            hint = UIFactory.Text(c, "", UIStyle.SmallSize, UIStyle.Yellow);
            UIFactory.Layout(hint, -1, 32);
            var buttons = Row(c, UIStyle.ButtonHeight);
            start = UIFactory.Button(buttons, "Yarışa Başla", StartRace, UIStyle.ButtonHeight, true);
            var back = UIFactory.Button(buttons, "Geri", () => Menu.Back());
            UIFactory.ChainVertical(map, laps, bots, difficulty, start);
            var nav = start.navigation; nav.selectOnRight = back; start.navigation = nav;
            back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = start, selectOnUp = difficulty, selectOnDown = map };
        }

        public override Selectable FirstSelected => map;

        public override void OnShow()
        {
            var tracks = GameContent.Instance != null ? GameContent.Instance.Tracks : null;
            var gs = GameSettings.Current;
            var names = new List<string>();
            if (tracks != null) foreach (var e in tracks.Tracks) names.Add(UIText.TrackLabel(e));
            int idx = tracks != null ? tracks.IndexOf(gs.TrackId) : 0;
            if (idx < 0 || (tracks != null && !TrackCatalog.IsAvailable(tracks.Get(idx)))) idx = tracks != null ? tracks.FirstAvailableIndex() : 0;
            map.SetOptions(names, idx, i => tracks != null && TrackCatalog.IsAvailable(tracks.Get(i)));
            laps.SetIndexWithoutNotify(gs.Laps - RaceConfig.MinLaps);
            bots.SetIndexWithoutNotify(gs.Bots);
            difficulty.SetIndexWithoutNotify((int)gs.Difficulty);
            Refresh();
        }

        private void Refresh()
        {
            var tracks = GameContent.Instance != null ? GameContent.Instance.Tracks : null;
            var e = tracks != null ? tracks.Get(map.Index) : null;
            bool available = TrackCatalog.IsAvailable(e);
            mapInfo.text = e != null ? e.Description : "";
            hint.text = available ? "" : "Bu harita henüz hazır değil. Şimdilik Geliştirme Pisti'ni seçin.";
            UIFactory.SetInteractable(start, available);
        }

        private void StartRace()
        {
            var tracks = GameContent.Instance.Tracks;
            var gs = GameSettings.Current;
            var e = tracks.Get(map.Index);
            if (!TrackCatalog.IsAvailable(e)) return;
            gs.TrackId = e.TrackId;
            gs.Laps = laps.Index + RaceConfig.MinLaps;
            gs.Bots = bots.Index;
            gs.Difficulty = (BotDifficulty)difficulty.Index;
            gs.Save();
            Menu.LaunchSinglePlayer();
        }
    }
}
