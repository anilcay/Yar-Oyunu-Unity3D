using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Creates the shared art materials (Art/Materials) and remaps the materials embedded in every FBX under
    /// Art/Models to them (stored as externalObjects in the .meta files). Safe to re-run.
    /// Batch: -executeMethod LugeRush.Editor.ArtMaterialFactory.BuildAll
    /// </summary>
    public static class ArtMaterialFactory
    {
        public const string Folder = EditorAssetUtil.Root + "/Art/Materials";
        public const string TextureFolder = Folder + "/Textures";
        public const string ShaderFolder = EditorAssetUtil.Root + "/Art/Shaders";
        public const string PalettePath = EditorAssetUtil.Root + "/Art/Textures/LR_Palette.png";

        public const string Palette = Folder + "/M_Palette.mat";
        public const string PaletteTint = Folder + "/M_PaletteTint.mat";
        public const string Emissive = Folder + "/M_Emissive.mat";
        public const string Skin = Folder + "/M_Skin.mat";
        public const string Helmet = Folder + "/M_Helmet.mat";
        public const string Water = Folder + "/M_Water.mat";
        public const string TrackAsphalt = Folder + "/M_TrackAsphalt.mat";
        public const string TrackCurbYellow = Folder + "/M_TrackCurbYellow.mat";
        public const string TrackCurbBlack = Folder + "/M_TrackCurbBlack.mat";
        public const string TrackSkirt = Folder + "/M_TrackSkirt.mat";

        /// <summary>FBX material name (Blender) -> shared material path.</summary>
        public static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "LR_Palette", Palette },
            { "LR_Palette_Tint", PaletteTint },
            { "LR_Palette_Emissive", Emissive },
            { "LR_Skin", Skin },
            { "LR_Helmet", Helmet },
            { "LR_Water", Water },
            { "Water", Water },
        };

        [MenuItem("Luge Rush/Art/Build Materials + Remap FBX")]
        public static void BuildAll()
        {
            CreateMaterials();
            RemapModelMaterials();
            AssetDatabase.SaveAssets();
        }

        public static void CreateMaterials()
        {
            EditorAssetUtil.EnsureFolder(Folder);
            AssetDatabase.ImportAsset(PalettePath, ImportAssetOptions.ForceUpdate); // palette import rules
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);

            PaletteLit(Palette, palette, Color.white, 0.12f);
            PaletteLit(PaletteTint, palette, Color.white, 0.22f); // body colour comes from _BaseColor (SledPaint)
            PaletteLit(Skin, palette, Color.white, 0.18f);
            PaletteLit(Helmet, palette, Color.white, 0.45f);
            var emissive = PaletteLit(Emissive, palette, Color.white, 0.3f);
            emissive.SetTexture("_EmissionMap", palette);
            emissive.SetColor("_EmissionColor", new Color(1.6f, 1.6f, 1.6f));
            emissive.EnableKeyword("_EMISSION");
            emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            CreateWater();
            CreateTrackMaterials();
        }

        private static Material LoadOrCreate(string path, Shader shader)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>URP Lit, palette atlas, flat look: low smoothness, no specular highlights except a hint.</summary>
        private static Material PaletteLit(string path, Texture2D palette, Color color, float smoothness)
        {
            var mat = LoadOrCreate(path, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", palette);
            mat.SetTexture("_MainTex", palette);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_SpecularHighlights", smoothness > 0.3f ? 1f : 0f);
            if (smoothness > 0.3f) mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            else mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetFloat("_Cull", 2f);
            return mat;
        }

        private static void CreateWater()
        {
            AssetDatabase.ImportAsset(ShaderFolder + "/LR_Water.shader", ImportAssetOptions.ForceUpdate);
            var shader = Shader.Find("LugeRush/Water");
            if (shader == null)
            {
                Debug.LogError("[ArtMaterialFactory] LugeRush/Water shader not found");
                return;
            }
            var mat = LoadOrCreate(Water, shader);
            mat.SetColor("_ShallowColor", new Color(0.42f, 0.74f, 0.86f, 0.55f)); // water_light/base (#4FA3C7)
            mat.SetColor("_DeepColor", new Color(0.16f, 0.42f, 0.58f, 0.85f));
            mat.SetColor("_FoamColor", new Color(0.93f, 0.97f, 1f, 0.95f));
            mat.renderQueue = 3000;
        }

        // ---------- Track ----------

        private static void CreateTrackMaterials()
        {
            EditorAssetUtil.EnsureFolder(TextureFolder);
            var albedo = WriteTexture(TextureFolder + "/T_Asphalt_Albedo.png", 256, true, (x, y) =>
            {
                float wet = Wetness(x, y);
                float grain = (TileNoise(x, y, 64, 7) - 0.5f) * 0.05f + (TileNoise(x, y, 128, 3) - 0.5f) * 0.05f;
                var dry = new Color(0.290f, 0.306f, 0.322f);   // #4A4E52
                var mid = new Color(0.227f, 0.239f, 0.251f);   // #3A3D40
                var wetC = new Color(0.169f, 0.184f, 0.200f);  // #2B2F33
                Color c = Color.Lerp(Color.Lerp(dry, mid, 0.5f + 0.5f * Mathf.Clamp01(wet * 2f)), wetC, 0.6f * Mathf.Clamp01(wet * 2f - 1f));
                return new Color(c.r + grain, c.g + grain, c.b + grain, 1f);
            });
            // Metallic (R) = 0, smoothness (A) = 0.25 dry .. 0.85 wet patches.
            var gloss = WriteTexture(TextureFolder + "/T_Asphalt_MetalGloss.png", 256, false, (x, y) =>
            {
                float wet = Wetness(x, y);
                float s = Mathf.Lerp(0.35f, 0.9f, wet) + (TileNoise(x, y, 64, 11) - 0.5f) * 0.08f;
                return new Color(0f, 0f, 0f, Mathf.Clamp01(s));
            });

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var road = LoadOrCreate(TrackAsphalt, lit);
            road.SetTexture("_BaseMap", albedo);
            road.SetColor("_BaseColor", Color.white);
            road.SetTexture("_MetallicGlossMap", gloss);
            road.SetFloat("_Metallic", 0f);
            road.SetFloat("_Smoothness", 0.75f); // multiplier on the gloss map alpha (ENVIRONMENT.md: ~0.6 average)
            road.SetFloat("_SmoothnessTextureChannel", 0f);
            road.EnableKeyword("_METALLICSPECGLOSSMAP");
            road.SetFloat("_EnvironmentReflections", 1f);
            road.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            road.SetFloat("_SpecularHighlights", 1f);
            road.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            Plain(TrackCurbYellow, lit, new Color(0.949f, 0.761f, 0.188f), 0.35f); // #F2C230
            Plain(TrackCurbBlack, lit, new Color(0.118f, 0.118f, 0.118f), 0.35f);  // #1E1E1E
            Plain(TrackSkirt, lit, new Color(0.36f, 0.30f, 0.22f), 0.05f);
        }

        private static void Plain(string path, Shader lit, Color c, float smoothness)
        {
            var m = LoadOrCreate(path, lit);
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
        }

        private static float Wetness(int x, int y)
        {
            float n = TileNoise(x, y, 4, 1) * 0.65f + TileNoise(x, y, 8, 2) * 0.35f;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, n));
        }

        /// <summary>Tileable value noise on a 256 grid with <paramref name="cells"/> cells per side.</summary>
        private static float TileNoise(int x, int y, int cells, int seed)
        {
            float fx = x / 256f * cells, fy = y / 256f * cells;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float H(int i, int j)
            {
                i = ((i % cells) + cells) % cells; j = ((j % cells) + cells) % cells;
                uint h = (uint)(i * 374761393 + j * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
        }

        private static Texture2D WriteTexture(string assetPath, int size, bool srgb, System.Func<int, int, Color> f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f(x, y);
            tex.SetPixels(px);
            tex.Apply();
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            ti.sRGBTexture = srgb;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // ---------- FBX remapping ----------

        public static string SharedPathFor(string fbxMaterialName)
        {
            string n = fbxMaterialName;
            int dot = n.LastIndexOf('.');
            if (dot > 0 && int.TryParse(n.Substring(dot + 1), out _)) n = n.Substring(0, dot); // Blender ".001"
            if (Map.TryGetValue(n, out var p)) return p;
            if (n.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0) return Water;
            if (n.IndexOf("Emissive", System.StringComparison.OrdinalIgnoreCase) >= 0) return Emissive;
            if (n.IndexOf("Tint", System.StringComparison.OrdinalIgnoreCase) >= 0) return PaletteTint;
            return Palette;
        }

        /// <summary>Remaps every embedded FBX material under Art/Models to the shared materials.</summary>
        public static int RemapModelMaterials()
        {
            int changed = 0;
            var guids = AssetDatabase.FindAssets("t:Model", new[] { EditorAssetUtil.Root + "/Art/Models" });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;

                var names = new HashSet<string>();
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>()) names.Add(m.name);
                foreach (var kv in mi.GetExternalObjectMap())
                    if (kv.Key.type == typeof(Material)) names.Add(kv.Key.name);
                var so = new SerializedObject(mi);
                var src = so.FindProperty("m_Materials");
                if (src != null)
                    for (int i = 0; i < src.arraySize; i++)
                    {
                        var nameProp = src.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                        if (nameProp != null && !string.IsNullOrEmpty(nameProp.stringValue)) names.Add(nameProp.stringValue);
                    }

                bool dirty = false;
                var map = mi.GetExternalObjectMap();
                foreach (var n in names)
                {
                    var target = AssetDatabase.LoadAssetAtPath<Material>(SharedPathFor(n));
                    if (target == null) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                    if (map.TryGetValue(id, out var existing) && existing == target) continue;
                    mi.RemoveRemap(id);
                    mi.AddRemap(id, target);
                    dirty = true;
                }
                if (dirty)
                {
                    mi.SaveAndReimport();
                    changed++;
                }
            }
            Debug.Log($"[ArtMaterialFactory] Remapped materials in {changed} model(s) of {guids.Length}.");
            return changed;
        }
    }
}
