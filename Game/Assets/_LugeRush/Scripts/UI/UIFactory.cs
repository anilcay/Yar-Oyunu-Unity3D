using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>Builds uGUI + TextMeshPro widgets in code with the shared <see cref="UIStyle"/>.</summary>
    public static class UIFactory
    {
        /// <summary>GetComponent or AddComponent (avoids "??" on Unity's fake-null objects in the editor).</summary>
        public static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>EventSystem with the Input System UI module (keyboard, gamepad and mouse navigation).</summary>
        public static EventSystem EnsureEventSystem()
        {
            var es = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (es != null) return es;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            es = go.GetComponent<EventSystem>();
            es.sendNavigationEvents = true;
            return es;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = 5; // UI
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Image(Transform parent, string name, Color color, Sprite sprite = null, bool raycast = false)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        public static Image Panel(Transform parent, string name, Color color, float cornerScale = 1f)
        {
            var img = Image(parent, name, color, UIStyle.Rounded, true);
            img.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.1f, cornerScale);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool display = false, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = display ? UIStyle.DisplayFont : UIStyle.BodyFont;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow; // Ellipsis hides text whose line height exceeds the box
            return t;
        }

        public static VerticalLayoutGroup VLayout(GameObject go, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperCenter)
        {
            var g = GetOrAdd<VerticalLayoutGroup>(go);
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset(0, 0, 0, 0);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup HLayout(GameObject go, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var g = GetOrAdd<HorizontalLayoutGroup>(go);
            g.spacing = spacing;
            g.padding = padding ?? new RectOffset(0, 0, 0, 0);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = true;
            return g;
        }

        public static LayoutElement Layout(Component c, float preferredWidth = -1, float preferredHeight = -1, float flexibleWidth = -1, float flexibleHeight = -1)
        {
            var le = GetOrAdd<LayoutElement>(c.gameObject);
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            le.flexibleHeight = flexibleHeight;
            if (preferredHeight > 0) le.minHeight = preferredHeight;
            return le;
        }

        public static RectOffset Pad(int all) => new RectOffset(all, all, all, all);
        public static RectOffset Pad(int horizontal, int vertical) => new RectOffset(horizontal, horizontal, vertical, vertical);

        public static ColorBlock Colors(Color normal, Color highlight, Color pressed)
        {
            var cb = ColorBlock.defaultColorBlock;
            cb.normalColor = normal;
            cb.highlightedColor = highlight;
            cb.selectedColor = highlight;
            cb.pressedColor = pressed;
            cb.disabledColor = new Color(normal.r * 0.7f, normal.g * 0.7f, normal.b * 0.7f, normal.a * 0.55f);
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            return cb;
        }

        public static Button Button(Transform parent, string label, Action onClick, float height = UIStyle.ButtonHeight, bool primary = false, float fontSize = UIStyle.BodySize)
        {
            var img = Image(parent, "Btn_" + label, Color.white, UIStyle.Rounded, true);
            img.pixelsPerUnitMultiplier = 1.4f;
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = primary
                ? Colors(UIStyle.AccentDark, new Color(1f, 0.62f, 0.25f), UIStyle.Accent)
                : Colors(UIStyle.Button, UIStyle.Accent, UIStyle.AccentDark);
            var text = Text(img.transform, label, fontSize, UIStyle.Text, TextAlignmentOptions.Center, false, "Label");
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform, 16, 0, 16, 0);
            btn.onClick.AddListener(() => LugeRush.Audio.GameAudio.PlayUi(LugeRush.Audio.UiSound.Confirm));
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            Layout(btn, -1, height);
            return btn;
        }

        public static TextMeshProUGUI LabelOf(Button b) => b.GetComponentInChildren<TextMeshProUGUI>(true);

        public static void SetInteractable(Selectable s, bool on)
        {
            if (s == null) return;
            s.interactable = on;
            foreach (var t in s.GetComponentsInChildren<TextMeshProUGUI>(true))
                t.alpha = on ? 1f : 0.45f;
        }

        public static UIStepper Stepper(Transform parent, string label, float height = UIStyle.RowHeight, float labelWidth = 300f)
        {
            var img = Image(parent, "Step_" + label, Color.white, UIStyle.Rounded, true);
            img.pixelsPerUnitMultiplier = 1.4f;
            var st = img.gameObject.AddComponent<UIStepper>();
            st.targetGraphic = img;
            st.Changed += _ => LugeRush.Audio.GameAudio.PlayUi(LugeRush.Audio.UiSound.Nav);
            st.colors = Colors(UIStyle.Row, new Color(0.24f, 0.30f, 0.42f, 1f), UIStyle.AccentDark);

            st.Label = Text(img.transform, label, UIStyle.BodySize, UIStyle.TextMuted, TextAlignmentOptions.MidlineLeft, false, "Label");
            var lrt = st.Label.rectTransform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(22, 0); lrt.sizeDelta = new Vector2(labelWidth, 0);

            st.ValueText = Text(img.transform, "", UIStyle.BodySize, UIStyle.Text, TextAlignmentOptions.Center, false, "Value");
            st.ValueText.fontStyle = FontStyles.Bold;
            var vrt = st.ValueText.rectTransform;
            vrt.anchorMin = new Vector2(0, 0); vrt.anchorMax = new Vector2(1, 1);
            vrt.offsetMin = new Vector2(labelWidth + 70, 0); vrt.offsetMax = new Vector2(-70, 0);

            st.LeftArrow = Text(img.transform, "‹", 46, UIStyle.Accent, TextAlignmentOptions.Center, false, "Left");
            Place(st.LeftArrow.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(labelWidth + 45, 2), new Vector2(50, height));
            st.RightArrow = Text(img.transform, "›", 46, UIStyle.Accent, TextAlignmentOptions.Center, false, "Right");
            Place(st.RightArrow.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40, 2), new Vector2(50, height));

            Layout(st, -1, height);
            return st;
        }

        public static TMP_InputField Input(Transform parent, string placeholder, float height = UIStyle.RowHeight, int charLimit = 32)
        {
            var img = Image(parent, "Input_" + placeholder, Color.white, UIStyle.Rounded, true);
            img.pixelsPerUnitMultiplier = 1.4f;
            img.gameObject.SetActive(false);
            var area = Rect("TextArea", img.transform);
            Stretch(area, 20, 6, 20, 6);
            area.gameObject.AddComponent<RectMask2D>();
            var ph = Text(area, placeholder, UIStyle.BodySize, UIStyle.TextDisabled, TextAlignmentOptions.MidlineLeft, false, "Placeholder");
            ph.fontStyle = FontStyles.Italic;
            Stretch(ph.rectTransform);
            var txt = Text(area, "", UIStyle.BodySize, UIStyle.Text, TextAlignmentOptions.MidlineLeft, false, "Text");
            Stretch(txt.rectTransform);
            var input = img.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = img;
            input.colors = Colors(UIStyle.Row, new Color(0.24f, 0.30f, 0.42f, 1f), UIStyle.AccentDark);
            input.textViewport = area;
            input.textComponent = txt;
            input.placeholder = ph;
            input.fontAsset = UIStyle.BodyFont;
            input.pointSize = UIStyle.BodySize;
            input.characterLimit = charLimit;
            input.caretColor = UIStyle.Accent;
            input.customCaretColor = true;
            input.selectionColor = new Color(1f, 0.48f, 0.1f, 0.35f);
            img.gameObject.SetActive(true);
            Layout(input, -1, height);
            return input;
        }

        /// <summary>Label + five segments; returns the segment images (fill with <see cref="SetBar"/>).</summary>
        public static Image[] StatBar(Transform parent, string label, out TextMeshProUGUI labelText, float height = 34f)
        {
            var row = Rect("Stat_" + label, parent);
            HLayout(row.gameObject, 8, null, TextAnchor.MiddleLeft);
            Layout(row, -1, height, -1, 0);
            labelText = Text(row, label, UIStyle.SmallSize, UIStyle.TextMuted, TextAlignmentOptions.MidlineLeft);
            Layout(labelText, 150, height);
            var segs = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                segs[i] = Image(row, "Seg" + i, UIStyle.Row, UIStyle.RoundedSmall);
                Layout(segs[i], 56, 18);
            }
            return segs;
        }

        public static void SetBar(Image[] segments, int value, Color on)
        {
            for (int i = 0; i < segments.Length; i++) segments[i].color = i < value ? on : new Color(1f, 1f, 1f, 0.12f);
        }

        /// <summary>Horizontal fill bar (background + fill), anchored in its parent; returns the fill rect.</summary>
        public static RectTransform FillBar(Transform parent, string name, Color fill, out Image background)
        {
            background = Image(parent, name, new Color(0f, 0f, 0f, 0.45f), UIStyle.RoundedSmall);
            var f = Image(background.transform, "Fill", fill, UIStyle.RoundedSmall);
            var rt = f.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.offsetMin = new Vector2(3, 3);
            rt.offsetMax = new Vector2(-3, -3);
            return rt;
        }

        public static void SetFill(RectTransform fill, float value01)
        {
            value01 = Mathf.Clamp01(value01);
            fill.anchorMax = new Vector2(value01, 1);
            fill.gameObject.SetActive(value01 > 0.005f);
        }

        /// <summary>Sets explicit up/down navigation through a vertical list of selectables.</summary>
        public static void ChainVertical(params Selectable[] items)
        {
            var list = new System.Collections.Generic.List<Selectable>();
            foreach (var s in items) if (s != null && s.gameObject.activeSelf) list.Add(s);
            for (int i = 0; i < list.Count; i++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit, wrapAround = false };
                nav.selectOnUp = i > 0 ? list[i - 1] : list[list.Count - 1];
                nav.selectOnDown = i < list.Count - 1 ? list[i + 1] : list[0];
                if (list[i] is not UIStepper)
                {
                    nav.selectOnLeft = null;
                    nav.selectOnRight = null;
                }
                list[i].navigation = nav;
            }
        }
    }
}
