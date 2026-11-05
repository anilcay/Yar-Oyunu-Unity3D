using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>
    /// "Label   ‹ value ›" row. Left/right (keys, d-pad, stick) or clicking the arrows changes the value;
    /// up/down navigate like any Selectable. Submit steps forward. Works for numbers and option lists.
    /// </summary>
    public class UIStepper : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public TextMeshProUGUI Label;
        public TextMeshProUGUI ValueText;
        public TextMeshProUGUI LeftArrow;
        public TextMeshProUGUI RightArrow;
        public bool Wrap = true;

        private readonly List<string> options = new List<string>();
        private Func<int, bool> isOptionEnabled;
        private int index;
        private bool readOnly;

        public event Action<int> Changed;

        public int Index => index;
        public int Count => options.Count;

        public bool ReadOnly
        {
            get => readOnly;
            set
            {
                readOnly = value;
                RefreshText();
            }
        }

        public void SetOptions(IList<string> values, int selected, Func<int, bool> enabledPredicate = null)
        {
            options.Clear();
            options.AddRange(values);
            isOptionEnabled = enabledPredicate;
            index = Mathf.Clamp(selected, 0, Mathf.Max(0, options.Count - 1));
            RefreshText();
        }

        public void SetRange(int min, int max, int value, Func<int, string> format = null)
        {
            var list = new List<string>();
            for (int v = min; v <= max; v++) list.Add(format != null ? format(v) : v.ToString());
            SetOptions(list, value - min);
        }

        /// <summary>Changes the shown value without raising <see cref="Changed"/>.</summary>
        public void SetIndexWithoutNotify(int i)
        {
            index = Mathf.Clamp(i, 0, Mathf.Max(0, options.Count - 1));
            RefreshText();
        }

        public void Step(int dir)
        {
            if (readOnly || !IsInteractable() || options.Count == 0) return;
            int next = index + dir;
            if (Wrap) next = (next % options.Count + options.Count) % options.Count;
            else next = Mathf.Clamp(next, 0, options.Count - 1);
            if (next == index) return;
            index = next;
            RefreshText();
            Changed?.Invoke(index);
        }

        public void RefreshText()
        {
            if (ValueText == null) return;
            string v = options.Count > 0 ? options[index] : "-";
            bool enabledOption = isOptionEnabled == null || isOptionEnabled(index);
            ValueText.text = v;
            ValueText.color = enabledOption ? UIStyle.Text : UIStyle.TextMuted;
            bool arrows = !readOnly && options.Count > 1;
            if (LeftArrow != null) LeftArrow.enabled = arrows;
            if (RightArrow != null) RightArrow.enabled = arrows;
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left) { Step(-1); eventData.Use(); return; }
            if (eventData.moveDir == MoveDirection.Right) { Step(1); eventData.Use(); return; }
            base.OnMove(eventData);
        }

        public void OnSubmit(BaseEventData eventData) => Step(1);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            var rt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out var local);
            float valueCenter = ValueText != null ? ((RectTransform)ValueText.transform).localPosition.x : 0f;
            Step(local.x < valueCenter ? -1 : 1);
        }
    }
}
