using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>Batch entry points for the art integration pipeline (one Unity run per call).</summary>
    public static class ArtBatch
    {
        /// <summary>Re-imports every model with the current import rules, builds materials and dumps diagnostics.</summary>
        public static void ImportAndDiagnose()
        {
            ReimportModels();
            ArtMaterialFactory.BuildAll();
            ArtDiagnostics.CompareRiderRigs();
            ArtDiagnostics.DumpModels();
        }

        /// <summary>
        /// Whole art integration pipeline: import rules, shared materials + FBX remap, rider animator,
        /// environment/track prefabs, rider + sled prefabs and the Dev_Greybox scene, then edit-mode renders.
        /// </summary>
        public static void BuildEverything()
        {
            ReimportModels();
            ArtMaterialFactory.BuildAll();
            ArtAnimatorFactory.Build();
            PrefabFactory.BuildAll();
            GreyboxBuilder.Build(); // runs SledAssetFactory.CreateAll (stats, rider + sled prefabs, catalog)
            AssetDatabase.SaveAssets();
            Debug.Log("[ArtBatch] BuildEverything done.");
        }

        /// <summary>Same as BuildEverything without forcing a re-import of every model.</summary>
        public static void BuildQuick()
        {
            ArtMaterialFactory.BuildAll();
            ArtAnimatorFactory.Build();
            PrefabFactory.BuildAll();
            GreyboxBuilder.Build();
            AssetDatabase.SaveAssets();
            GreyboxScreenshot.Capture();
            Debug.Log("[ArtBatch] BuildQuick done.");
        }

        public static void BuildAndCapture()
        {
            BuildEverything();
            GreyboxScreenshot.Capture();
        }

        public static void ReimportModels()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { EditorAssetUtil.Root + "/Art/Models" }))
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.ImportAsset(ArtMaterialFactory.PalettePath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[ArtBatch] Models re-imported.");
        }
    }
}
