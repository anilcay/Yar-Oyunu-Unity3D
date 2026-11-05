using LugeRush.Race;
using LugeRush.Vehicle;

namespace LugeRush.UI
{
    /// <summary>In-game strings (Turkish). English comes with the localisation phase.</summary>
    public static class UIText
    {
        public static readonly string[] SledTypeNames = { "Klasik", "Spor", "Tandem", "Mini" };
        public static readonly string[] SledDefaultNames = { "Orman", "Ok", "İkiz", "Fıstık" };
        public static readonly string[] SledDescriptions =
        {
            "Dengeli, yuvarlak hatlı küvet kızak. Yeni başlayanlar için ideal.",
            "Alçak ve uzun burunlu. Virajlarda en iyi tutuş.",
            "İki kişilik uzun kızak. Düzlükte en hızlısı, ama ağır.",
            "Kısa ve tombul. Çok çevik, drift ustası; kolay savrulur."
        };
        public static readonly string[] ColorNames = { "Turuncu", "Kırmızı", "Sarı", "Turkuaz" };
        public static readonly string[] DifficultyNames = { "Kolay", "Normal", "Zor" };

        public static string SledName(SledType t)
        {
            var catalog = GameContent.Instance != null ? GameContent.Instance.Sleds : null;
            var stats = catalog != null ? catalog.GetStats(t) : null;
            return stats != null && !string.IsNullOrEmpty(stats.DisplayName) ? stats.DisplayName : SledDefaultNames[(int)t % 4];
        }

        public static string SledLabel(SledType t) => $"{SledName(t)} ({SledTypeNames[(int)t % 4]})";
        public static string ColorName(SledColor c) => ColorNames[(int)c % 4];
        public static string DifficultyName(BotDifficulty d) => DifficultyNames[(int)d % 3];

        public static string TrackLabel(TrackCatalog.Entry e)
        {
            if (e == null) return "-";
            return TrackCatalog.IsAvailable(e) ? e.DisplayName : $"{e.DisplayName}  <size=70%><color=#{UIStyle.Hex(UIStyle.Yellow)}>YAKINDA</color></size>";
        }

        public const string ControlsTable =
            "<b>Eylem</b><pos=42%><b>Klavye</b><pos=76%><b>Gamepad</b>\n" +
            "Dön<pos=42%>A / D, ← / →<pos=76%>Sol çubuk\n" +
            "Fren<pos=42%>S, ↓, Boşluk<pos=76%>LT\n" +
            "Drift<pos=42%>Shift<pos=76%>RB\n" +
            "İtiş (başlangıç)<pos=42%>W, ↑<pos=76%>A / Çarpı\n" +
            "Kamera<pos=42%>C<pos=76%>Y / Üçgen\n" +
            "Geriye bak<pos=42%>Q<pos=76%>RS tık\n" +
            "Reset<pos=42%>R<pos=76%>Back\n" +
            "Duraklat<pos=42%>Esc<pos=76%>Start";
    }
}
