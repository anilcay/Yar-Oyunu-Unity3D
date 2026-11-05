using System;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Import rules for everything under Art/Models (Blender FBX output) and the palette textures.
    /// Deterministic: the same settings are forced on every import so a re-export from Blender needs no manual steps.
    /// Material remapping to the shared materials is stored in the .meta (externalObjects) by
    /// <see cref="ArtMaterialFactory.RemapModelMaterials"/>.
    /// </summary>
    public class ArtImportPostprocessor : AssetPostprocessor
    {
        public const string ModelsRoot = EditorAssetUtil.Root + "/Art/Models/";
        public const string RiderPath = ModelsRoot + "Characters/SK_Rider.fbx";
        public const string PaletteTexturePrefix = EditorAssetUtil.Root + "/Art/Textures/LR_Palette";

        /// <summary>
        /// Rider rig type. Humanoid measured against Generic with ArtDiagnostics.CompareRiderRigs: the seated hips stay
        /// exactly on Seat and hands/feet differ by at most ~2 mm, so Humanoid is used (OnAnimatorIK, retargeting).
        /// </summary>
        public static ModelImporterAnimationType RiderRig = ModelImporterAnimationType.Human;

        // Clip name -> loop (ANIMATION.md).
        private static readonly string[] LoopClips =
            { "Sit_Idle", "Steer_Left", "Steer_Right", "Brake", "Drift_Left", "Drift_Right", "Lift_Relax", "Win_Cheer", "Wave" };

        /// <summary>Models with MeshColliders (runtime scaling/cooking) or analysed by PrefabFactory keep Read/Write enabled.</summary>
        public static bool NeedsMeshCollider(string fileName)
        {
            string[] keys =
            {
                "RockLarge", "MossRock", "Cliff", "_Wall_", "GorgeWall", "Mesa_", "Arch_", "TunnelMouth", "TunnelSegment",
                "Tunnel_4m", "Chalet", "Footbridge", "ViewPlatform", "WaterTower", "Slope_", "StartGate", "FinishArch",
                "FinishCanopy", "Lift_Station", "RockMedium", "RockSmall", "FallenPine", "Log_", "Stump_", "SledStack",
                // read by PrefabFactory (water split, trunk capsule fit)
                "Waterfall", "Stream_", "TallTree", "GiantTree", "Pine_", "Saguaro", "Bamboo"
            };
            foreach (var k in keys) if (fileName.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot, StringComparison.Ordinal)) return;
            var mi = (ModelImporter)assetImporter;
            bool rider = assetPath == RiderPath;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.importBlendShapes = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.None; // no normal maps (flat palette shading)
            mi.generateSecondaryUV = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.isReadable = !rider && NeedsMeshCollider(file);
            mi.addCollider = false;
            mi.sortHierarchyByName = false; // keep Blender order (Body, Trim, Chassis, ...)

            if (rider)
            {
                mi.animationType = RiderRig;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.optimizeGameObjects = false; // bones must stay exposed (IK, 1st person head)
                mi.importAnimation = true;
                mi.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                mi.resampleCurves = true;
            }
            else
            {
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
            }
        }

        private void OnPreprocessAnimation()
        {
            if (assetPath != RiderPath) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return;
            foreach (var c in clips)
            {
                bool loop = Array.IndexOf(LoopClips, c.name) >= 0;
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;      // Bake Into Pose
                c.lockRootHeightY = true;
                c.lockRootPositionXZ = true;
                c.keepOriginalOrientation = true; // Based Upon: Original
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
                c.heightFromFeet = false;
                // Bump is used on an additive layer: reference pose = its own first frame (hands on the bar).
                c.hasAdditiveReferencePose = c.name == "Bump";
                c.additiveReferencePoseFrame = c.firstFrame;
            }
            mi.clipAnimations = clips;
        }

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(PaletteTexturePrefix, StringComparison.Ordinal)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Point;
            ti.wrapMode = TextureWrapMode.Repeat; // skin/helmet colour offsets may wrap
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
        }
    }
}
