using System.Collections.Generic;
using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Sled body colour. The art model's "Body" mesh uses the shared LR_Palette_Tint material (grey tint cells);
    /// the colour is multiplied in through <c>_BaseColor</c> with a MaterialPropertyBlock, so all sleds share one material.
    /// </summary>
    [DisallowMultipleComponent]
    public class SledPaint : MonoBehaviour
    {
        public const string BodyObjectName = "Body";

        /// <summary>ART_GUIDE sled colours (same order as <see cref="SledColor"/>).</summary>
        public static readonly Color[] Colors =
        {
            new Color(0.949f, 0.475f, 0.169f), // #F2792B orange
            new Color(0.851f, 0.227f, 0.227f), // #D93A3A red
            new Color(0.949f, 0.761f, 0.188f), // #F2C230 yellow
            new Color(0.231f, 0.722f, 0.769f), // #3BB8C4 turquoise
        };

        [SerializeField] private SledColor color = SledColor.Orange;
        [Tooltip("Overrides the preset colour when alpha > 0.")]
        [SerializeField] private Color customColor = new Color(0f, 0f, 0f, 0f);

        private readonly List<Renderer> bodyRenderers = new List<Renderer>();
        private MaterialPropertyBlock block;
        private bool bound;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Color CurrentColor => customColor.a > 0f ? customColor : Colors[(int)color % Colors.Length];
        public SledColor Preset => color;

        private void Awake() { Rebind(); Apply(); }

        public void SetColor(SledColor preset)
        {
            color = preset;
            customColor = new Color(0f, 0f, 0f, 0f);
            Apply();
        }

        public void SetColor(Color c)
        {
            c.a = 1f;
            customColor = c;
            Apply();
        }

        /// <summary>Finds the tinted body renderers (call after swapping the model).</summary>
        public void Rebind()
        {
            bodyRenderers.Clear();
            var visual = transform.Find(SledVisuals.VisualRootName);
            Collect(visual != null ? visual : transform);
            bound = true;
            Apply();
        }

        private void Collect(Transform t)
        {
            // Riders live under Seat; their "Body_*" meshes are skinned and must not be painted.
            if (t.GetComponent<Animator>() != null) return;
            if (t.name == BodyObjectName && t.TryGetComponent(out Renderer r) && !(r is SkinnedMeshRenderer)) bodyRenderers.Add(r);
            foreach (Transform c in t) Collect(c);
        }

        public void Apply()
        {
            if (!bound) Rebind();
            block ??= new MaterialPropertyBlock();
            Color c = CurrentColor;
            foreach (var r in bodyRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, c);
                r.SetPropertyBlock(block);
            }
        }
    }
}
