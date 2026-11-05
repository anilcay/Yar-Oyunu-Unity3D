using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace LugeRush.Editor
{
    /// <summary>
    /// TextMeshPro setup: imports TMP Essentials (Assets/TextMesh Pro) and creates the two UI font assets with
    /// full Turkish coverage — Inter SemiBold (body) and Oswald Bold (display), both SIL OFL 1.1.
    /// Batch: -executeMethod LugeRush.Editor.UIFontBuilder.InstallTmpEssentials   (run once, separate launch)
    ///        -executeMethod LugeRush.Editor.UIFontBuilder.BuildFonts
    /// </summary>
    public static class UIFontBuilder
    {
        public const string FontFolder = EditorAssetUtil.Root + "/Prefabs/UI/Fonts";
        public const string BodyFontPath = FontFolder + "/Inter-SemiBold SDF.asset";
        public const string DisplayFontPath = FontFolder + "/Oswald-Bold SDF.asset";
        public const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        /// <summary>Every character the UI uses up front (dynamic atlas adds anything else at runtime).</summary>
        public const string Charset =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "ğĞüÜşŞıİöÖçÇâÂîÎûÛéÉ" +
            "‹›«»←→↑↓•·–—…✓●○×°€";

        [MenuItem("Luge Rush/UI/Install TMP Essentials")]
        public static void InstallTmpEssentials()
        {
            if (File.Exists(TmpSettingsPath))
            {
                Debug.Log("[UIFontBuilder] TMP Essentials already installed.");
                return;
            }
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string pkg = info != null ? Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage") : null;
            if (pkg == null || !File.Exists(pkg))
            {
                Debug.LogError($"[UIFontBuilder] TMP Essential Resources.unitypackage not found ({pkg}).");
                return;
            }
            if (Application.isBatchMode)
            {
                // Import is asynchronous: run without -quit and exit when it completes.
                AssetDatabase.importPackageCompleted += _ =>
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[UIFontBuilder] Imported {pkg}");
                    EditorApplication.Exit(File.Exists(TmpSettingsPath) ? 0 : 1);
                };
                AssetDatabase.importPackageFailed += (_, err) =>
                {
                    Debug.LogError($"[UIFontBuilder] TMP import failed: {err}");
                    EditorApplication.Exit(1);
                };
            }
            AssetDatabase.ImportPackage(pkg, false);
        }

        [MenuItem("Luge Rush/UI/Build UI Fonts")]
        public static void BuildFonts()
        {
            var body = CreateFont(FontFolder + "/Inter-SemiBold.ttf", BodyFontPath, 64, 8);
            var display = CreateFont(FontFolder + "/Oswald-Bold.ttf", DisplayFontPath, 72, 8);
            if (body == null || display == null) return;

            // Oswald lacks arrows / check marks: fall back to Inter.
            display.fallbackFontAssetTable ??= new System.Collections.Generic.List<TMP_FontAsset>();
            if (!display.fallbackFontAssetTable.Contains(body)) display.fallbackFontAssetTable.Add(body);
            EditorUtility.SetDirty(display);

            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings != null)
            {
                var so = new SerializedObject(settings);
                so.FindProperty("m_defaultFontAsset").objectReferenceValue = body;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            else Debug.LogWarning("[UIFontBuilder] TMP Settings not found: install TMP Essentials first.");
            AssetDatabase.SaveAssets();
        }

        private static TMP_FontAsset CreateFont(string ttfPath, string assetPath, int pointSize, int padding)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogError($"[UIFontBuilder] Font file missing: {ttfPath}");
                return null;
            }
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);

            var fa = TMP_FontAsset.CreateFontAsset(font, pointSize, padding, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            string name = Path.GetFileNameWithoutExtension(assetPath);
            fa.name = name;
            AssetDatabase.CreateAsset(fa, assetPath);
            fa.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            bool all = fa.TryAddCharacters(Charset, out string missing);
            if (!all && !string.IsNullOrEmpty(missing))
            {
                var codes = new System.Text.StringBuilder();
                foreach (char c in missing) codes.Append($"U+{(int)c:X4} ");
                Debug.Log($"[UIFontBuilder] {name}: glyphs not in font (fallback used): {codes}");
            }
            string turkish = "ğĞüÜşŞıİöÖçÇ";
            foreach (char c in turkish)
                if (!fa.HasCharacter(c)) Debug.LogError($"[UIFontBuilder] {name} is missing Turkish glyph U+{(int)c:X4}");
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UIFontBuilder] Created {assetPath}: {fa.characterTable.Count} characters.");
            return fa;
        }
    }
}
