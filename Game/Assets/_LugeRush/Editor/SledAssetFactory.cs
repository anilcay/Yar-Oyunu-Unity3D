using LugeRush.AI;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEditor;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Generates the gameplay data assets (SledStats x4, AIDifficulty x3, SledCatalog) and the
    /// primitive placeholder prefabs (sled, guardrail module). Safe to re-run: assets are updated in place.
    /// </summary>
    public static class SledAssetFactory
    {
        public const string StatsFolder = EditorAssetUtil.Root + "/ScriptableObjects/Sleds";
        public const string AIFolder = EditorAssetUtil.Root + "/ScriptableObjects/AI";
        public const string SledPrefabPath = EditorAssetUtil.Root + "/Prefabs/Sleds/Sled_Placeholder.prefab";
        public const string GuardrailPrefabPath = EditorAssetUtil.Root + "/Prefabs/Track/Guardrail_Placeholder.prefab";
        public const string CatalogPath = StatsFolder + "/SledCatalog.asset";
        public const string InputActionsPath = EditorAssetUtil.Root + "/Scripts/Input/LugeRush.inputactions";

        public const int SledLayer = 9;

        // GDD §4 relative ratings: mass, speed, grip, handling, drift (1..5).
        private struct Rating
        {
            public SledType Type; public string Name; public int Mass, Speed, Grip, Handling, Drift;
        }

        private static readonly Rating[] Ratings =
        {
            new Rating { Type = SledType.Classic, Name = "Orman", Mass = 3, Speed = 3, Grip = 3, Handling = 3, Drift = 3 },
            new Rating { Type = SledType.Sport,   Name = "Ok",    Mass = 2, Speed = 3, Grip = 5, Handling = 4, Drift = 3 },
            new Rating { Type = SledType.Tandem,  Name = "İkiz",  Mass = 5, Speed = 5, Grip = 2, Handling = 2, Drift = 2 },
            new Rating { Type = SledType.Mini,    Name = "Fıstık", Mass = 1, Speed = 2, Grip = 4, Handling = 5, Drift = 5 },
        };

        /// <summary>Reference slope (rise/run) at which terminal speed equals MaxSpeed.</summary>
        public const float ReferenceSlope = 0.08f;

        public static SledCatalog CreateAll()
        {
            var catalog = CreateStatsAndCatalog();
            CreateDifficulties();
            CreateSledPrefab(catalog.GetStats(SledType.Classic));
            CreateGuardrailPrefab();
            AssetDatabase.SaveAssets();
            return catalog;
        }

        public static SledCatalog CreateStatsAndCatalog()
        {
            var catalog = EditorAssetUtil.LoadOrCreate<SledCatalog>(CatalogPath);
            var entries = new SledCatalog.Entry[Ratings.Length];
            for (int i = 0; i < Ratings.Length; i++)
            {
                var r = Ratings[i];
                var s = EditorAssetUtil.LoadOrCreate<SledStats>($"{StatsFolder}/Sled_{r.Type}.asset");
                ApplyRating(s, r);
                EditorUtility.SetDirty(s);
                entries[i] = new SledCatalog.Entry { Type = r.Type, Stats = s };
            }
            catalog.Sleds = entries;
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void ApplyRating(SledStats s, Rating r)
        {
            s.Type = r.Type;
            s.DisplayName = r.Name;
            s.UiWeight = r.Mass; s.UiSpeed = r.Speed; s.UiGrip = r.Grip; s.UiHandling = r.Handling; s.UiDrift = r.Drift;

            s.Mass = 60f + 25f * r.Mass;
            s.MaxSpeed = 20f + 1.4f * r.Speed;
            s.SlopeGravityMultiplier = 2.5f;
            s.RollingResistance = 0.015f;
            s.DragCoefficient = SledStats.DragForTerminalSpeed(s.MaxSpeed, ReferenceSlope, s.SlopeGravityMultiplier, s.RollingResistance);

            s.GripLateral = 3f + 1.5f * r.Grip;
            s.SteerRate = 70f + 10f * r.Handling;
            s.SteerSpeedFalloff = 0.04f;
            s.SteerResponse = 8f + r.Handling;
            s.BrakeForce = 7f - 0.3f * r.Mass;

            s.DriftChargeRate = 0.3f + 0.05f * r.Drift;
            s.DriftGripMultiplier = 0.45f - 0.03f * r.Drift;
            s.DriftSteerMultiplier = 1.15f + 0.05f * r.Drift;
            s.DriftMinSpeed = 9f;
        }

        public static AIDifficulty[] CreateDifficulties()
        {
            var easy = EditorAssetUtil.LoadOrCreate<AIDifficulty>($"{AIFolder}/AI_Easy.asset");
            Set(easy, "Kolay", line: 0.5f, wander: 0.5f, corner: 0.82f, brake: 0.65f, drag: 1.08f, drift: 0f, push: 0.35f, spread: 0.2f, rubber: 0.15f, gain: 2.0f);
            var normal = EditorAssetUtil.LoadOrCreate<AIDifficulty>($"{AIFolder}/AI_Normal.asset");
            Set(normal, "Normal", line: 0.75f, wander: 0.3f, corner: 0.9f, brake: 0.75f, drag: 1f, drift: 0.25f, push: 0.15f, spread: 0.1f, rubber: 0.12f, gain: 2.2f);
            var hard = EditorAssetUtil.LoadOrCreate<AIDifficulty>($"{AIFolder}/AI_Hard.asset");
            Set(hard, "Zor", line: 0.95f, wander: 0.12f, corner: 0.96f, brake: 0.82f, drag: 0.97f, drift: 0.6f, push: 0.05f, spread: 0.05f, rubber: 0.08f, gain: 2.4f);
            return new[] { easy, normal, hard };
        }

        private static void Set(AIDifficulty d, string name, float line, float wander, float corner, float brake, float drag,
            float drift, float push, float spread, float rubber, float gain)
        {
            d.DisplayName = name;
            d.LineAccuracy = line;
            d.LineWander = wander;
            d.CornerSpeedFactor = corner;
            d.BrakeEfficiency = brake;
            d.DragFactor = drag;
            d.DriftUsage = drift;
            d.PushReaction = push;
            d.PushReactionSpread = spread;
            d.RubberBand = rubber;
            d.SteerGain = gain;
            EditorUtility.SetDirty(d);
        }

        public static AIDifficulty LoadDifficulty(BotDifficulty d) =>
            AssetDatabase.LoadAssetAtPath<AIDifficulty>($"{AIFolder}/AI_{d}.asset");

        /// <summary>
        /// Placeholder sled: physics root (Rigidbody, BoxCollider, gameplay components) with a separate
        /// "SledVisual" child built from primitives (tub body, handlebar, 4 wheels, rider). Art replaces SledVisual.
        /// </summary>
        public static GameObject CreateSledPrefab(SledStats defaultStats)
        {
            EditorAssetUtil.EnsureFolder(EditorAssetUtil.Root + "/Prefabs/Sleds");
            string m = EditorAssetUtil.SledMaterials;
            var body = EditorAssetUtil.LitMaterial(m, "M_SledBody", Color.white, 0.65f);
            var dark = EditorAssetUtil.LitMaterial(m, "M_SledTrim", new Color(0.12f, 0.12f, 0.13f), 0.4f);
            var wheel = EditorAssetUtil.LitMaterial(m, "M_SledWheel", new Color(0.05f, 0.05f, 0.05f), 0.2f);
            var metal = EditorAssetUtil.LitMaterial(m, "M_SledMetal", new Color(0.7f, 0.7f, 0.72f), 0.7f, 0.9f);
            var rider = EditorAssetUtil.LitMaterial(m, "M_Rider", new Color(0.2f, 0.25f, 0.35f), 0.3f);
            var helmet = EditorAssetUtil.LitMaterial(m, "M_Helmet", Color.white, 0.8f);

            var root = new GameObject("Sled_Placeholder");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = defaultStats != null ? defaultStats.Mass : 135f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.42f, 0f);
            box.size = new Vector3(0.9f, 0.42f, 1.9f);

            var controller = root.AddComponent<SledController>();
            controller.Stats = defaultStats;
            root.AddComponent<TrackFollower>();
            root.AddComponent<SledVisuals>();
            root.AddComponent<SledAnimatorBridge>();
            root.AddComponent<RaceParticipant>();
            root.AddComponent<SledRecovery>();

            // Visual hierarchy (model pivot = ground contact plane).
            var visual = new GameObject(SledVisuals.VisualRootName).transform;
            visual.SetParent(root.transform, false);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Body_Tub", visual, new Vector3(0f, 0.22f, -0.05f), new Vector3(0.82f, 0.22f, 1.7f), body);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Body_Nose", visual, new Vector3(0f, 0.2f, 0.9f), new Vector3(0.62f, 0.16f, 0.4f), body, new Vector3(-12f, 0f, 0f));
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Body_RimL", visual, new Vector3(-0.42f, 0.38f, -0.1f), new Vector3(0.07f, 0.16f, 1.5f), body);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Body_RimR", visual, new Vector3(0.42f, 0.38f, -0.1f), new Vector3(0.07f, 0.16f, 1.5f), body);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Body_Back", visual, new Vector3(0f, 0.42f, -0.88f), new Vector3(0.82f, 0.24f, 0.08f), body);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Bumper", visual, new Vector3(0f, 0.14f, 1.1f), new Vector3(0.66f, 0.08f, 0.08f), dark);

            var handlebar = new GameObject("Handlebar").transform;
            handlebar.SetParent(visual, false);
            handlebar.localPosition = new Vector3(0f, 0.5f, 0.3f);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Handlebar_Stem", handlebar, new Vector3(0f, -0.12f, 0f), new Vector3(0.05f, 0.24f, 0.05f), metal);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Handlebar_Bar", handlebar, new Vector3(0f, 0.0f, 0f), new Vector3(0.62f, 0.045f, 0.045f), metal);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Handlebar_GripL", handlebar, new Vector3(-0.28f, 0f, 0f), new Vector3(0.1f, 0.06f, 0.06f), dark);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Handlebar_GripR", handlebar, new Vector3(0.28f, 0f, 0f), new Vector3(0.1f, 0.06f, 0.06f), dark);
            AddEmpty("Grip.L", handlebar, new Vector3(-0.28f, 0f, 0f));
            AddEmpty("Grip.R", handlebar, new Vector3(0.28f, 0f, 0f));

            string[] wheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            Vector3[] wheelPos = { new Vector3(-0.33f, 0.07f, 0.62f), new Vector3(0.33f, 0.07f, 0.62f), new Vector3(-0.33f, 0.07f, -0.62f), new Vector3(0.33f, 0.07f, -0.62f) };
            for (int i = 0; i < 4; i++)
            {
                var w = AddEmpty(wheelNames[i], visual, wheelPos[i]);
                EditorAssetUtil.Primitive(PrimitiveType.Cylinder, wheelNames[i] + "_Mesh", w, Vector3.zero, new Vector3(0.14f, 0.03f, 0.14f), wheel, new Vector3(0f, 0f, 90f));
                EditorAssetUtil.Primitive(PrimitiveType.Cube, wheelNames[i] + "_Spoke", w, Vector3.zero, new Vector3(0.065f, 0.12f, 0.025f), metal);
            }

            var riderRoot = new GameObject("Rider_Placeholder").transform;
            riderRoot.SetParent(visual, false);
            EditorAssetUtil.Primitive(PrimitiveType.Capsule, "Rider_Torso", riderRoot, new Vector3(0f, 0.62f, -0.38f), new Vector3(0.38f, 0.3f, 0.3f), rider, new Vector3(-15f, 0f, 0f));
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Rider_Legs", riderRoot, new Vector3(0f, 0.38f, 0.22f), new Vector3(0.34f, 0.14f, 0.75f), rider);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Rider_ArmL", riderRoot, new Vector3(-0.2f, 0.6f, -0.05f), new Vector3(0.08f, 0.08f, 0.6f), rider, new Vector3(18f, -8f, 0f));
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Rider_ArmR", riderRoot, new Vector3(0.2f, 0.6f, -0.05f), new Vector3(0.08f, 0.08f, 0.6f), rider, new Vector3(18f, 8f, 0f));
            EditorAssetUtil.Primitive(PrimitiveType.Sphere, "Helmet", riderRoot, new Vector3(0f, 0.98f, -0.36f), new Vector3(0.27f, 0.27f, 0.29f), helmet);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Helmet_Visor", riderRoot, new Vector3(0f, 0.98f, -0.23f), new Vector3(0.2f, 0.07f, 0.04f), dark);

            // Camera anchors live on the physics root (hover offset already subtracted).
            float hover = defaultStats != null ? defaultStats.RestHoverHeight : 0.16f;
            var fp = AddEmpty("Camera_FP", root.transform, new Vector3(0f, 0.98f - hover + 0.02f, -0.3f));
            fp.localEulerAngles = new Vector3(10f, 0f, 0f);
            AddEmpty("CameraTarget", root.transform, new Vector3(0f, 0.65f, 0.2f));

            EditorAssetUtil.SetLayerRecursive(root, SledLayer);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, SledPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static GameObject CreateGuardrailPrefab()
        {
            EditorAssetUtil.EnsureFolder(EditorAssetUtil.Root + "/Prefabs/Track");
            var metal = EditorAssetUtil.LitMaterial(EditorAssetUtil.TrackMaterials, "M_Guardrail", new Color(0.62f, 0.64f, 0.66f), 0.75f, 0.85f);
            var post = EditorAssetUtil.LitMaterial(EditorAssetUtil.TrackMaterials, "M_GuardrailPost", new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.6f);
            var root = new GameObject("Guardrail_Placeholder");
            // 4 m module, pivot at road level on the curb's outer edge, forward = +Z.
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Post_A", root.transform, new Vector3(0f, 0.45f, -1.9f), new Vector3(0.08f, 0.9f, 0.08f), post);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Post_B", root.transform, new Vector3(0f, 0.45f, 0.1f), new Vector3(0.08f, 0.9f, 0.08f), post);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Rail_Top", root.transform, new Vector3(0f, 0.75f, 0f), new Vector3(0.05f, 0.22f, 4f), metal);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Rail_Low", root.transform, new Vector3(0f, 0.4f, 0f), new Vector3(0.04f, 0.1f, 4f), metal);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, GuardrailPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static Transform AddEmpty(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }
    }
}
