using TMPro;
using UnityEngine;

namespace LugeRush.UI
{
    /// <summary>Colours, sizes and generated sprites shared by every menu and the HUD (clean low-poly look).</summary>
    public static class UIStyle
    {
        public static readonly Color PanelBg = new Color(0.055f, 0.08f, 0.13f, 0.90f);
        public static readonly Color PanelBgLight = new Color(0.10f, 0.14f, 0.21f, 0.92f);
        public static readonly Color Row = new Color(0.12f, 0.16f, 0.24f, 0.95f);
        public static readonly Color RowAlt = new Color(0.09f, 0.13f, 0.20f, 0.95f);
        public static readonly Color Button = new Color(0.15f, 0.20f, 0.30f, 0.96f);
        public static readonly Color Accent = new Color(1.00f, 0.48f, 0.10f, 1f);       // sled orange
        public static readonly Color AccentDark = new Color(0.85f, 0.36f, 0.05f, 1f);
        public static readonly Color Secondary = new Color(0.12f, 0.76f, 0.80f, 1f);    // turquoise
        public static readonly Color Yellow = new Color(1.00f, 0.82f, 0.12f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.40f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.30f, 0.25f, 1f);
        public static readonly Color Text = new Color(0.96f, 0.97f, 1f, 1f);
        public static readonly Color TextMuted = new Color(0.62f, 0.69f, 0.80f, 1f);
        public static readonly Color TextDisabled = new Color(0.45f, 0.50f, 0.58f, 1f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);

        public const float ButtonHeight = 64f;
        public const float RowHeight = 58f;
        public const int TitleSize = 96;
        public const int HeaderSize = 46;
        public const int BodySize = 30;
        public const int SmallSize = 24;

        private static Sprite rounded, roundedSmall, circle, white;

        public static TMP_FontAsset BodyFont
        {
            get
            {
                var c = GameContent.Instance;
                return c != null && c.BodyFont != null ? c.BodyFont : TMP_Settings.defaultFontAsset;
            }
        }

        public static TMP_FontAsset DisplayFont
        {
            get
            {
                var c = GameContent.Instance;
                return c != null && c.DisplayFont != null ? c.DisplayFont : BodyFont;
            }
        }

        /// <summary>9-sliced rounded rectangle (radius ~14 px at reference resolution).</summary>
        public static Sprite Rounded => rounded != null ? rounded : rounded = MakeRounded(64, 18, "LR_Rounded");
        public static Sprite RoundedSmall => roundedSmall != null ? roundedSmall : roundedSmall = MakeRounded(32, 8, "LR_RoundedSmall");
        public static Sprite Circle => circle != null ? circle : circle = MakeRounded(64, 32, "LR_Circle", false);
        public static Sprite White => white != null ? white : white = MakeRounded(8, 0, "LR_White", false);

        private static Sprite MakeRounded(int size, int radius, string name, bool sliced = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float cx = Mathf.Clamp(fx, r, size - r);
                    float cy = Mathf.Clamp(fy, r, size - r);
                    float d = r <= 0f ? 0f : Vector2.Distance(new Vector2(fx, fy), new Vector2(cx, cy)) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = sliced ? Mathf.Max(1, radius) : 0f;
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            sprite.name = name;
            return sprite;
        }

        public static string FormatTime(float seconds)
        {
            if (seconds <= 0f || float.IsInfinity(seconds) || float.IsNaN(seconds)) return "--:--.--";
            int m = (int)(seconds / 60f);
            float s = seconds - m * 60;
            return m.ToString("00") + ":" + s.ToString("00.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
