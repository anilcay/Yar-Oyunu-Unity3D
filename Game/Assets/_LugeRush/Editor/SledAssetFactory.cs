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
        public const string PlaceholderPrefabPath = EditorAssetUtil.Root + "/Prefabs/Sleds/Sled_Placeholder.prefab";
        /// <summary>Default sled spawned in track scenes (bootstrap / SledVariant switch the type at runtime).</summary>
        public const string SledPrefabPath = EditorAssetUtil.Root + "/Prefabs/Sleds/Sled_Classic.prefab";
        public const string SledPrefabFolder = EditorAssetUtil.Root + "/Prefabs/Sleds";
        public const string RiderPrefabPath = EditorAssetUtil.Root + "/Prefabs/Characters/Rider.prefab";
        public static string SledPrefabPathFor(SledType t) => $"{SledPrefabFolder}/Sled_{t}.prefab";
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
            ArtMaterialFactory.CreateMaterials();
            ArtAnimatorFactory.Build();
            CreateRiderPrefab();
            CreateArtSledPrefabs(catalog);
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
            foreach (var e in entries) e.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SledPrefabPathFor(e.Type));
            catalog.BodyColors = (Color[])SledPaint.Colors.Clone();
            catalog.HelmetColors = (Color[])SledVisuals.HelmetPalette.Clone();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        /// <summary>
        /// Balance table (Yönetici decisions, tuned with BalanceTests on Dev_Greybox):
        /// Tandem = highest terminal speed but slow to accelerate (low slope pull, low drag) and wide turning;
        /// Sport = best cornering (yaw rate holds at speed, most grip) but slower than Tandem on straights;
        /// Mini = most agile at low speed, quickest off the line, lowest top speed, knocked around easily;
        /// Classic = balanced.
        /// </summary>
        private struct Tuning
        {
            public float Mass, MaxSpeed, SlopeGravity, Grip, SteerRate, Falloff, Response, Brake;
            public float DriftCharge, DriftGrip, DriftSteer, Knockback;
        }

        private static Tuning TuningFor(SledType t)
        {
            switch (t)
            {
                case SledType.Sport:
                    return new Tuning { Mass = 110f, MaxSpeed = 24.2f, SlopeGravity = 2.5f, Grip = 10.5f, SteerRate = 103f, Falloff = 0.030f, Response = 12f, Brake = 6.4f,
                        DriftCharge = 0.75f, DriftGrip = 0.36f, DriftSteer = 1.30f, Knockback = 0.60f };
                case SledType.Tandem:
                    return new Tuning { Mass = 185f, MaxSpeed = 34.0f, SlopeGravity = 2.15f, Grip = 7.5f, SteerRate = 101f, Falloff = 0.038f, Response = 9f, Brake = 5.5f,
                        DriftCharge = 0.65f, DriftGrip = 0.39f, DriftSteer = 1.25f, Knockback = 0.45f };
                case SledType.Mini:
                    return new Tuning { Mass = 85f, MaxSpeed = 22.8f, SlopeGravity = 2.8f, Grip = 9.0f, SteerRate = 125f, Falloff = 0.055f, Response = 13f, Brake = 6.7f,
                        DriftCharge = 0.90f, DriftGrip = 0.30f, DriftSteer = 1.40f, Knockback = 0.85f };
                default:
                    return new Tuning { Mass = 135f, MaxSpeed = 24.2f, SlopeGravity = 2.5f, Grip = 8.0f, SteerRate = 102f, Falloff = 0.038f, Response = 11f, Brake = 6.1f,
                        DriftCharge = 0.75f, DriftGrip = 0.36f, DriftSteer = 1.30f, Knockback = 0.60f };
            }
        }

        private static void ApplyRating(SledStats s, Rating r)
        {
            s.Type = r.Type;
            s.DisplayName = r.Name;
            s.UiWeight = r.Mass; s.UiSpeed = r.Speed; s.UiGrip = r.Grip; s.UiHandling = r.Handling; s.UiDrift = r.Drift;

            var t = TuningFor(r.Type);
            s.Mass = t.Mass;
            s.MaxSpeed = t.MaxSpeed;
            s.SlopeGravityMultiplier = t.SlopeGravity;
            s.RollingResistance = 0.015f;
            s.DragCoefficient = SledStats.DragForTerminalSpeed(s.MaxSpeed, ReferenceSlope, s.SlopeGravityMultiplier, s.RollingResistance);

            s.GripLateral = t.Grip;
            s.SteerRate = t.SteerRate;
            s.SteerSpeedFalloff = t.Falloff;
            s.SteerResponse = t.Response;
            s.BrakeForce = t.Brake;

            s.DriftChargeRate = t.DriftCharge;
            s.DriftGripMultiplier = t.DriftGrip;
            s.DriftSteerMultiplier = t.DriftSteer;
            s.DriftMinSpeed = 9f;
            s.CollisionKnockbackScale = t.Knockback;

            // Slipstream: was ~6-30 boosts per sled in 2 laps; target ~3-8 (long fill + cooldown).
            s.SlipstreamChargeTime = 3f;
            s.SlipstreamDecayRate = 1f;
            s.SlipstreamCooldown = 11.5f;
            s.SlipstreamDragReduction = 0.35f;
        }

        public static AIDifficulty[] CreateDifficulties()
        {
            var easy = EditorAssetUtil.LoadOrCreate<AIDifficulty>($"{AIFolder}/AI_Easy.asset");
            Set(easy, "Kolay", line: 0.5f, wander: 0.5f, corner: 0.82f, brake: 0.65f, drag: 1.08f, drift: 0.15f, push: 0.35f, spread: 0.2f, rubber: 0.15f, gain: 2.0f, super: 0f);
            var normal = EditorAssetUtil.LoadOrCreate<AIDifficulty>($"{AIFolder}/AI_Normal.asset");
            Set(normal, "Normal", line: 0.75f, wander: 0.3f, corner: 0.9f, brake: 0.75f, drag: 1f, drift: 0.6f, push: 0.15f, spread: 0.1f, rubber: 0.12f, gain: 2.2f, super: 0.7f);
            var hard = EditorAssetUtil.LoadOrCreate<AIDifficulty>($"{AIFolder}/AI_Hard.asset");
            Set(hard, "Zor", line: 0.95f, wander: 0.12f, corner: 0.96f, brake: 0.82f, drag: 0.97f, drift: 0.85f, push: 0.05f, spread: 0.05f, rubber: 0.08f, gain: 2.4f, super: 0.85f);
            return new[] { easy, normal, hard };
        }

        private static void Set(AIDifficulty d, string name, float line, float wander, float corner, float brake, float drag,
            float drift, float push, float spread, float rubber, float gain, float super)
        {
            d.DisplayName = name;
            d.LineAccuracy = line;
            d.LineWander = wander;
            d.CornerSpeedFactor = corner;
            d.BrakeEfficiency = brake;
            d.DragFactor = drag;
            d.DriftUsage = drift;
            d.SuperDriftUsage = super;
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
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlaceholderPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ---------- Art prefabs (rider + 4 sleds) ----------

        /// <summary>Physical dimensions per sled model (ARCHITECTURE: accepted lengths 1.53 / 1.75 / 2.70 / 1.43 m).</summary>
        private struct Dimensions
        {
            public float Length, Width, CenterZ, ProbeHalfLength;
        }

        private static Dimensions DimensionsFor(SledType t)
        {
            switch (t)
            {
                case SledType.Sport: return new Dimensions { Length = 1.75f, Width = 0.90f, CenterZ = -0.015f, ProbeHalfLength = 0.62f };
                case SledType.Tandem: return new Dimensions { Length = 2.70f, Width = 0.82f, CenterZ = 0.272f, ProbeHalfLength = 1.0f };
                case SledType.Mini: return new Dimensions { Length = 1.43f, Width = 0.90f, CenterZ = 0.102f, ProbeHalfLength = 0.46f };
                default: return new Dimensions { Length = 1.53f, Width = 0.82f, CenterZ = 0.007f, ProbeHalfLength = 0.55f };
            }
        }

        public static string SledModelPath(SledType t) => $"{EditorAssetUtil.Root}/Art/Models/Sleds/SM_Sled_{t}.fbx";

        /// <summary>
        /// Prefabs/Characters/Rider.prefab: SK_Rider (Humanoid) + AC_Rider + RiderAppearance + RiderIK.
        /// Saved in the Sit_Idle pose so edit-mode previews look seated.
        /// </summary>
        public static GameObject CreateRiderPrefab()
        {
            EditorAssetUtil.EnsureFolder(EditorAssetUtil.Root + "/Prefabs/Characters");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtImportPostprocessor.RiderPath);
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ArtAnimatorFactory.ControllerPath);
            if (model == null) { Debug.LogError("[SledAssetFactory] SK_Rider.fbx missing"); return null; }

            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name = "Rider";
            var animator = root.GetComponent<Animator>();
            if (animator == null) animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.Normal;
            var appearance = root.AddComponent<RiderAppearance>();
            root.AddComponent<RiderIK>();
            var idle = ArtAnimatorFactory.Clip("Sit_Idle");
            if (idle != null) idle.SampleAnimation(root, 0f);
            appearance.Apply();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, RiderPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        public static void CreateArtSledPrefabs(SledCatalog catalog)
        {
            foreach (SledType t in System.Enum.GetValues(typeof(SledType)))
                CreateArtSledPrefab(t, catalog);
            foreach (var e in catalog.Sleds) e.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SledPrefabPathFor(e.Type));
            EditorUtility.SetDirty(catalog);
        }

        /// <summary>
        /// Prefabs/Sleds/Sled_[Type].prefab: physics root (same components as the placeholder) with
        /// SledVisual/SM_Sled_[Type] (art) and Rider(s) seated on Seat (and Seat2 for the Tandem).
        /// </summary>
        public static GameObject CreateArtSledPrefab(SledType type, SledCatalog catalog)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(SledModelPath(type));
            var riderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RiderPrefabPath);
            var stats = catalog.GetStats(type);
            if (model == null || riderPrefab == null) { Debug.LogError($"[SledAssetFactory] Missing model/rider for {type}"); return null; }
            var dim = DimensionsFor(type);
            float hover = stats != null ? stats.RestHoverHeight : 0.16f;

            var root = new GameObject($"Sled_{type}");
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = stats != null ? stats.Mass : 135f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Body box: bottom 0.30 m above the road (clears curbs; suspension rays carry the sled), top at the tub rim.
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.32f, dim.CenterZ);
            box.size = new Vector3(dim.Width, 0.36f, dim.Length);

            var controller = root.AddComponent<SledController>();
            controller.Stats = stats;
            controller.ProbeHalfExtents = new Vector2(0.34f, dim.ProbeHalfLength);
            root.AddComponent<TrackFollower>();
            root.AddComponent<SledVisuals>();
            root.AddComponent<SledAnimatorBridge>();
            root.AddComponent<RaceParticipant>();
            root.AddComponent<SledRecovery>();
            root.AddComponent<SledPaint>();
            var variant = root.AddComponent<SledVariant>();
            variant.Catalog = catalog;
            variant.CurrentType = type;
            var fpAnchor = root.AddComponent<RiderFirstPersonAnchor>();

            // Anchors on the physics root: Camera_FP first (the 1st person camera looks it up by name, depth first).
            AddEmpty("Camera_FP", root.transform, Vector3.zero);
            AddEmpty("CameraTarget", root.transform, new Vector3(0f, 0.65f, 0.2f + dim.CenterZ));

            var visual = new GameObject(SledVisuals.VisualRootName).transform;
            visual.SetParent(root.transform, false);
            visual.localPosition = new Vector3(0f, -hover, 0f);
            var art = (GameObject)PrefabUtility.InstantiatePrefab(model, visual);
            art.transform.localPosition = Vector3.zero;
            art.transform.localRotation = Quaternion.identity;

            var driver = SeatRider(art.transform, riderPrefab, "Seat", "Grip.L", "Grip.R", fpAnchor);
            if (type == SledType.Tandem) SeatRider(art.transform, riderPrefab, "Seat2", "Grip2.L", "Grip2.R", null);

            fpAnchor.Rebind();
            fpAnchor.Snap();
            EditorAssetUtil.SetLayerRecursive(root, SledLayer);
            string path = SledPrefabPathFor(type);
            string info = $"driver={(driver != null)}, box {box.size}, probes {controller.ProbeHalfExtents}";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"[SledAssetFactory] Built {path} ({info}).");
            return prefab;
        }

        private static GameObject SeatRider(Transform art, GameObject riderPrefab, string seatName, string gripL, string gripR, RiderFirstPersonAnchor fpAnchor)
        {
            var seat = SledVisuals.FindDeep(art, seatName);
            if (seat == null) { Debug.LogError($"[SledAssetFactory] {art.name} has no {seatName}"); return null; }
            var rider = (GameObject)PrefabUtility.InstantiatePrefab(riderPrefab, seat);
            rider.name = seatName == "Seat" ? "Rider" : "Rider_Passenger";
            rider.transform.localPosition = Vector3.zero;
            rider.transform.localRotation = Quaternion.identity;
            rider.transform.localScale = Vector3.one;

            // Sit_Idle reference pose: wrist offsets for IK and the head rest rotation for the 1st person camera.
            var idle = ArtAnimatorFactory.Clip("Sit_Idle");
            if (idle != null) idle.SampleAnimation(rider, 0f);
            var ik = rider.GetComponent<RiderIK>();
            ik.LeftGrip = SledVisuals.FindDeep(art, gripL);
            ik.RightGrip = SledVisuals.FindDeep(art, gripR);
            var handL = SledVisuals.FindDeep(rider.transform, "Hand.L");
            var handR = SledVisuals.FindDeep(rider.transform, "Hand.R");
            if (ik.LeftGrip != null && handL != null) ik.LeftOffset = ik.LeftGrip.InverseTransformPoint(handL.position);
            if (ik.RightGrip != null && handR != null) ik.RightOffset = ik.RightGrip.InverseTransformPoint(handR.position);
            var appearance = rider.GetComponent<RiderAppearance>();
            if (seatName != "Seat") appearance.Set(2, 2, 1, 4);
            appearance.Apply();

            if (fpAnchor != null)
            {
                var head = SledVisuals.FindDeep(rider.transform, "Head");
                if (head != null) fpAnchor.HeadRestLocal = Quaternion.Inverse(fpAnchor.transform.rotation) * head.rotation;
            }
            return rider;
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
