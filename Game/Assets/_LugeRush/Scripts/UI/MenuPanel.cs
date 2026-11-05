using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>One screen of the main menu. Panels are built in code by <see cref="MainMenuController"/>.</summary>
    public abstract class MenuPanel
    {
        public RectTransform Root { get; private set; }
        public RectTransform Content { get; private set; }
        public TextMeshProUGUI Header { get; private set; }
        protected MainMenuController Menu { get; private set; }

        public abstract string Id { get; }
        /// <summary>Show the rotating sled preview behind this panel.</summary>
        public virtual bool ShowsSled => true;
        public virtual float Width => 780f;
        /// <summary>Tall panels hide the game title and use the full height.</summary>
        public virtual bool HidesTitle => false;
        public bool IsVisible => Root != null && Root.gameObject.activeSelf;

        public void Create(MainMenuController menu, Transform parent, string title)
        {
            Menu = menu;
            var bg = UIFactory.Panel(parent, "Panel_" + Id, UIStyle.PanelBg);
            Root = bg.rectTransform;
            Root.anchorMin = new Vector2(0, 0);
            Root.anchorMax = new Vector2(0, 1);
            Root.pivot = new Vector2(0, 0.5f);
            Root.offsetMin = new Vector2(90, 120);
            Root.offsetMax = new Vector2(90 + Width, -210);

            Header = UIFactory.Text(Root, title, UIStyle.HeaderSize, UIStyle.Text, TextAlignmentOptions.MidlineLeft, true, "Header");
            UIFactory.Place(Header.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -22), new Vector2(Width - 80, 64));
            var accent = UIFactory.Image(Root, "HeaderAccent", UIStyle.Accent, UIStyle.RoundedSmall);
            UIFactory.Place(accent.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -92), new Vector2(90, 6));

            Content = UIFactory.Rect("Content", Root);
            UIFactory.Stretch(Content, 40, 118, 40, 34);
            UIFactory.VLayout(Content.gameObject, 14);
            Build(Content);
            Root.gameObject.SetActive(false);
        }

        protected abstract void Build(RectTransform content);

        public virtual void OnShow() { }
        public virtual void OnHide() { }
        /// <summary>Cancel / Esc. Default: go back one panel.</summary>
        public virtual void OnBack() => Menu.Back();
        public virtual Selectable FirstSelected => Content != null ? Content.GetComponentInChildren<Selectable>() : null;
        public virtual void Tick() { }

        protected static RectTransform Spacer(Transform parent, float height)
        {
            var rt = UIFactory.Rect("Spacer", parent);
            UIFactory.Layout(rt, -1, height);
            return rt;
        }

        protected static RectTransform FlexSpacer(Transform parent)
        {
            var rt = UIFactory.Rect("Flex", parent);
            UIFactory.Layout(rt, -1, -1, -1, 1);
            return rt;
        }

        protected static RectTransform Row(Transform parent, float height, float spacing = 12f)
        {
            var rt = UIFactory.Rect("Row", parent);
            var h = UIFactory.HLayout(rt.gameObject, spacing);
            h.childForceExpandWidth = true;
            UIFactory.Layout(rt, -1, height, -1, 0); // 0: force-expanding rows must not steal flexible height
            return rt;
        }
    }
}
