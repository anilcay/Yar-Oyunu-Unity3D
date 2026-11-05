using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// SK_Rider look: one body + one head variant active, skin tone and helmet colour chosen by shifting the palette
    /// UVs (<c>_BaseMap_ST.z = k/16</c>, ANIMATION.md) on the M_Skin / M_Helmet material slots only.
    /// Also hides the own head + helmet for the 1st person camera (shadows stay).
    /// </summary>
    [DisallowMultipleComponent]
    public class RiderAppearance : MonoBehaviour
    {
        public static readonly string[] BodyNames =
        {
            "Body_A1_TeeJeans", "Body_A2_TeeJeans", "Body_B1_WindbreakerTights",
            "Body_B2_WindbreakerTights", "Body_C1_HoodieShorts", "Body_C2_HoodieShorts"
        };
        public static readonly string[] HeadNames = { "Head_A", "Head_B", "Head_C" };
        public const string HelmetName = "Helmet";
        public const int SkinTones = 6;
        public const int HelmetColors = 8; // yellow, blue, green, black, white, pink, red, purple

        public const string SkinMaterialName = "M_Skin";
        public const string HelmetMaterialName = "M_Helmet";

        [Range(0, 5)] public int Body;
        [Range(0, 2)] public int Head;
        [Range(0, SkinTones - 1)] public int Skin;
        [Range(0, HelmetColors - 1)] public int Helmet;
        [Tooltip("Pick a deterministic variant from the sled's grid slot at start (bots look different).")]
        public bool VaryByGridSlot = true;

        private readonly List<Renderer> renderers = new List<Renderer>();
        private MaterialPropertyBlock block;
        private bool firstPerson;
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        public bool FirstPersonHidden => firstPerson;

        private void Awake()
        {
            renderers.Clear();
            GetComponentsInChildren(true, renderers);
            Apply();
        }

        /// <summary>Deterministic look from a seed (e.g. grid slot / player id).</summary>
        public void ApplySeed(int seed)
        {
            var rng = new System.Random(seed * 7919 + 17);
            Body = rng.Next(BodyNames.Length);
            Head = rng.Next(HeadNames.Length);
            Skin = rng.Next(SkinTones);
            Apply();
        }

        public void Set(int body, int head, int skin, int helmet)
        {
            Body = body; Head = head; Skin = skin; Helmet = helmet;
            Apply();
        }

        public void SetHelmet(int index)
        {
            Helmet = ((index % HelmetColors) + HelmetColors) % HelmetColors;
            Apply();
        }

        /// <summary>Picks the helmet palette colour closest to <paramref name="c"/> (menu / catalog colours).</summary>
        public void SetHelmetColor(Color c, Color[] helmetPalette)
        {
            if (helmetPalette == null || helmetPalette.Length == 0) return;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < Mathf.Min(helmetPalette.Length, HelmetColors); i++)
            {
                Color p = helmetPalette[i];
                float d = (p.r - c.r) * (p.r - c.r) + (p.g - c.g) * (p.g - c.g) + (p.b - c.b) * (p.b - c.b);
                if (d < bestD) { bestD = d; best = i; }
            }
            SetHelmet(best);
        }

        /// <summary>1st person: own head and helmet render shadows only.</summary>
        public void SetFirstPerson(bool hidden)
        {
            firstPerson = hidden;
            Apply();
        }

        public void Apply()
        {
            if (renderers.Count == 0) GetComponentsInChildren(true, renderers);
            block ??= new MaterialPropertyBlock();
            Body = Mathf.Clamp(Body, 0, BodyNames.Length - 1);
            Head = Mathf.Clamp(Head, 0, HeadNames.Length - 1);
            Skin = Mathf.Clamp(Skin, 0, SkinTones - 1);
            Helmet = Mathf.Clamp(Helmet, 0, HelmetColors - 1);

            foreach (var r in renderers)
            {
                if (r == null) continue;
                string n = r.gameObject.name;
                int bi = System.Array.IndexOf(BodyNames, n);
                int hi = System.Array.IndexOf(HeadNames, n);
                bool isHelmet = n == HelmetName;
                if (bi >= 0) r.gameObject.SetActive(bi == Body);
                if (hi >= 0) r.gameObject.SetActive(hi == Head);
                if (hi >= 0 || isHelmet)
                    r.shadowCastingMode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;

                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null) continue;
                    int k = mats[m].name == SkinMaterialName ? Skin : mats[m].name == HelmetMaterialName ? Helmet : -1;
                    if (k < 0) continue;
                    r.GetPropertyBlock(block, m);
                    block.SetVector(BaseMapStId, new Vector4(1f, 1f, k / 16f, 0f));
                    r.SetPropertyBlock(block, m);
                }
            }
        }
    }
}
