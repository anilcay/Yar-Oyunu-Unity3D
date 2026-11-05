using System.Collections.Generic;
using System.Linq;
using LugeRush.AI;
using LugeRush.CameraSystem;
using LugeRush.Input;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.UI;
using LugeRush.Vehicle;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace LugeRush.Editor
{
    /// <summary>
    /// Builds a complete playable race scene from a TrackDefinition: environment, track (spline, meshes,
    /// barriers, guardrails, lift, grid), race logic, player + bots, cameras and HUD.
    /// Level designers call <see cref="BuildScene"/> with their own definitions.
    /// </summary>
    public static class TrackSceneBuilder
    {
        public class Options
        {
            public int SledCount = 8;
            public bool PlaceGuardrails = true;
            public float GuardrailCurvature = 0.02f;
            [Tooltip("Use the art track kit (rails, nets, gates, lift) instead of primitive placeholders.")]
            public bool UseArtKit = true;
            [Tooltip("Outer side of curves sharper than this (1/m) gets catch nets instead of rails.")]
            public float NetCurvature = 0.035f;
            public float GroundSize = 700f;
            public Vector3 GroundCenter = Vector3.zero;
            public BotDifficulty DefaultDifficulty = BotDifficulty.Normal;

            // --- Level builder hooks (defaults keep the Dev_Greybox behaviour) ---
            [Tooltip("Skip the default sun, sky, fog and ground plane: the caller builds its own environment.")]
            public bool CustomEnvironment;
            [Tooltip("Finish arch exactly on the lap line with the canopy behind it; start gate at the head of the grid.")]
            public bool FinishArchOnLapLine;
            [Tooltip("StartGrid.FirstRowOffset override (metres behind the lap line), <= 0 keeps the default.")]
            public float GridFirstRowOffset;
            [Tooltip("(distance, side -1/+1) -> may an edge module (rail/net) stand here? Null = everywhere.")]
            public System.Func<float, int, bool> EdgeFilter;
            [Tooltip("Distance -> may a warning sign stand here? Null = everywhere.")]
            public System.Func<float, bool> SignFilter;
            [Tooltip("Called after the track is built, before the sleds, cameras and the scene save.")]
            public System.Action<TrackPath, TrackMeshBuilder, Transform> PostTrack;
            [Tooltip("Called after the whole scene exists, right before it is saved.")]
            public System.Action<Scene> PreSave;
        }

        public static Scene BuildScene(TrackDefinition def, string scenePath, Options options = null)
        {
            options ??= new Options();
            var catalog = SledAssetFactory.CreateAll();
            var difficulties = SledAssetFactory.CreateDifficulties();
            var sledPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SledAssetFactory.SledPrefabPath);
            var guardrail = AssetDatabase.LoadAssetAtPath<GameObject>(SledAssetFactory.GuardrailPrefabPath);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(SledAssetFactory.InputActionsPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!options.CustomEnvironment) BuildEnvironment(options);

            var track = BuildTrack(def, scene.name, guardrail, options);
            var path = track.GetComponent<TrackPath>();
            var grid = track.GetComponentInChildren<StartGrid>();
            options.PostTrack?.Invoke(path, track.GetComponent<TrackMeshBuilder>(), track.transform);

            // Race logic
            var raceGo = new GameObject("Race");
            var manager = raceGo.AddComponent<RaceManager>();
            manager.Track = path;
            manager.Grid = grid;
            var bootstrap = raceGo.AddComponent<RaceBootstrap>();
            bootstrap.Manager = manager;
            bootstrap.Catalog = catalog;
            bootstrap.Difficulties = difficulties;
            bootstrap.AutopilotDifficulty = difficulties[(int)BotDifficulty.Normal];

            // Sleds
            var sledsRoot = new GameObject("Sleds").transform;
            var roster = new List<RaceParticipant>();
            for (int i = 0; i < Mathf.Min(options.SledCount, StartGrid.MaxSlots); i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(sledPrefab, sledsRoot);
                var sled = go.GetComponent<SledController>();
                var pose = grid.GetSlot(i, sled.RestHoverHeight);
                go.transform.SetPositionAndRotation(pose.position, pose.rotation);
                go.GetComponent<TrackFollower>().Track = path;
                var participant = go.GetComponent<RaceParticipant>();
                participant.GridSlot = i;
                if (i == 0)
                {
                    go.name = "Sled_Player";
                    participant.IsLocalPlayer = true;
                    participant.DisplayName = "Oyuncu";
                    var input = go.AddComponent<PlayerSledInput>();
                    input.Actions = actions;
                }
                else
                {
                    go.name = $"Sled_Bot_{i}";
                    participant.IsBot = true;
                    var ai = go.AddComponent<AIDriver>();
                    ai.Track = path;
                    ai.Difficulty = difficulties[(int)options.DefaultDifficulty];
                }
                go.GetComponent<SledVisuals>().ApplyColors(catalog.GetBodyColor((SledColor)(i % 4)), catalog.GetHelmetColor(i));
                roster.Add(participant);
            }
            bootstrap.Roster = roster;

            BuildCameras(roster[0].GetComponent<SledController>());

            var hud = new GameObject("HUD").AddComponent<RaceHud>();
            hud.Manager = manager;
            hud.Player = roster[0];

            options.PreSave?.Invoke(scene);
            EditorAssetUtil.EnsureFolder(System.IO.Path.GetDirectoryName(scenePath));
            EditorSceneManager.SaveScene(scene, scenePath);
            AddToBuildSettings(scenePath);
            AssetDatabase.SaveAssets();
            return scene;
        }

        private static void BuildEnvironment(Options options)
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 700f;
            RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.85f);

            var groundMat = EditorAssetUtil.LitMaterial(EditorAssetUtil.TrackMaterials, "M_Ground", new Color(0.26f, 0.42f, 0.2f), 0.1f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = options.GroundCenter;
            ground.transform.localScale = Vector3.one * (options.GroundSize / 10f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.layer = 8; // Track layer so a sled that falls off still lands (and is recovered)
        }

        private static GameObject BuildTrack(TrackDefinition def, string sceneName, GameObject guardrail, Options options)
        {
            string mats = EditorAssetUtil.TrackMaterials;
            ArtMaterialFactory.CreateMaterials();
            // Wet asphalt (smoothness variation map) + palette-matched curbs (ART_GUIDE track colours).
            var road = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.TrackAsphalt);
            var curbY = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.TrackCurbYellow);
            var curbB = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.TrackCurbBlack);
            var skirt = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.TrackSkirt);
            var white = EditorAssetUtil.LitMaterial(mats, "M_LineWhite", new Color(0.95f, 0.95f, 0.95f), 0.3f);
            var arch = EditorAssetUtil.LitMaterial(mats, "M_ArchGreen", new Color(0.1f, 0.55f, 0.2f), 0.4f);
            var cable = EditorAssetUtil.LitMaterial(mats, "M_Cable", new Color(0.08f, 0.08f, 0.08f), 0.6f, 0.8f);
            var pylon = EditorAssetUtil.LitMaterial(mats, "M_Pylon", new Color(0.45f, 0.45f, 0.48f), 0.4f, 0.5f);

            var go = new GameObject($"Track_{def.TrackId}");
            go.AddComponent<SplineContainer>();
            var path = go.AddComponent<TrackPath>();
            path.BuildFromDefinition(def);

            var builder = go.AddComponent<TrackMeshBuilder>();
            builder.Track = path;
            builder.RoadMaterial = road;
            builder.CurbYellowMaterial = curbY;
            builder.CurbBlackMaterial = curbB;
            builder.SkirtMaterial = skirt;
            builder.CurbWidth = def.CurbWidth;
            builder.CurbHeight = def.CurbHeight;
            builder.BarrierHeight = def.BarrierHeight;
            builder.BuildSkirt = def.BuildSkirt;
            builder.SkirtGroundY = def.SkirtGroundY;
            builder.Build();
            builder.BuildOnAwake = false;
            SaveTrackMeshes(go, sceneName);

            var lift = go.AddComponent<LiftSection>();
            lift.Track = path;
            lift.Speed = def.LiftSpeed;
            lift.ExitSpeed = def.LiftExitSpeed;

            var gridGo = new GameObject("StartGrid");
            gridGo.transform.SetParent(go.transform, false);
            var grid = gridGo.AddComponent<StartGrid>();
            grid.Track = path;
            if (options.GridFirstRowOffset > 0f) grid.FirstRowOffset = options.GridFirstRowOffset;

            if (options.UseArtKit && PrefabFactory.LoadTrack("SM_Track_StartGate") != null)
            {
                if (options.FinishArchOnLapLine) BuildArtStartFinishOnLine(path, grid);
                else BuildArtStartFinish(path, grid, builder);
                BuildArtLift(path, builder, go.transform);
                if (options.PlaceGuardrails) BuildArtEdges(path, builder, go.transform, options);
                BuildArtSigns(path, builder, go.transform, options);
                return go;
            }

            BuildStartLine(path, grid, white, arch, builder);
            BuildLiftVisuals(path, cable, pylon, go.transform);

            if (options.PlaceGuardrails && guardrail != null)
            {
                var railRoot = new GameObject("Guardrails").transform;
                railRoot.SetParent(go.transform, false);
                System.Func<GameObject, Transform, GameObject> inst = (p, parent) => (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
                float k = options.GuardrailCurvature;
                // Outer side of curves: right turns (k > 0) -> left side.
                builder.PlaceAlong(guardrail, 4f, -1, 0.15f, 0f, path.Length, railRoot, d => !path.IsInLift(d) && MaxCurvature(path, d) > k, inst);
                builder.PlaceAlong(guardrail, 4f, +1, 0.15f, 0f, path.Length, railRoot, d => !path.IsInLift(d) && MinCurvature(path, d) < -k, inst);
            }
            return go;
        }

        // ---------- Art track kit ----------

        private static GameObject Place(string model, Transform parent, Vector3 pos, Quaternion rot, Vector3? scale = null)
        {
            var prefab = PrefabFactory.LoadTrack(model);
            if (prefab == null) { Debug.LogWarning($"[TrackSceneBuilder] Missing track prefab for {model}"); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return go;
        }

        /// <summary>
        /// Kit modules have their pivot at the start end and run along +Z; PlaceAlong centres modules on the sample.
        /// Right side: shift back half a module. Left side: turn 180° around Y (outer side +X stays outside, no
        /// negative scale - ARCHITECTURE decision) and shift forward half a module.
        /// </summary>
        private static void ToStartPivot(List<GameObject> placed, float length, int side)
        {
            foreach (var go in placed)
            {
                var t = go.transform;
                Vector3 f = t.forward;
                if (side > 0) t.position -= f * (length * 0.5f);
                else t.SetPositionAndRotation(t.position + f * (length * 0.5f), t.rotation * Quaternion.Euler(0f, 180f, 0f));
            }
        }

        private static GameObject Inst(GameObject p, Transform parent) => (GameObject)PrefabUtility.InstantiatePrefab(p, parent);

        /// <summary>Start line + start gate (lights) on the timing line, finish arch and covered finish canopy after it, grid slot marks.</summary>
        private static void BuildArtStartFinish(TrackPath path, StartGrid grid, TrackMeshBuilder builder)
        {
            var root = new GameObject("StartFinish").transform;
            root.SetParent(path.transform, false);
            float d0 = path.StartLineDistance;
            float w = path.GetWidth(d0);
            Place("SM_Track_StartLine", root, path.GetPosition(d0) - path.GetTangent(d0) * 0.2f, path.GetRotation(d0), new Vector3(w / 4f, 1f, 1f));
            Place("SM_Track_StartGate", root, path.GetPosition(d0), path.GetRotation(d0));
            Place("SM_Track_FinishArch", root, path.GetPosition(d0 + FinishArchOffset), path.GetRotation(d0 + FinishArchOffset));
            for (int i = 0; i < FinishCanopyModules; i++)
            {
                float d = d0 + FinishArchOffset + 2f + i * 4f;
                Vector3 a = path.GetPosition(d), b = path.GetPosition(d + 4f);
                Place("SM_Track_FinishCanopy_4m", root, a, Quaternion.LookRotation(b - a, path.GetUp(d)));
            }
            for (int i = 0; i < StartGrid.MaxSlots; i++)
            {
                var slot = grid.GetSlot(i, 0.004f);
                Place("SM_Track_GridSlot", root, slot.position, slot.rotation);
            }
        }

        /// <summary>Lap line + finish arch at the same spot, canopy behind them, start gate at the head of the grid (ARCHITECTURE).</summary>
        private static void BuildArtStartFinishOnLine(TrackPath path, StartGrid grid)
        {
            var root = new GameObject("StartFinish").transform;
            root.SetParent(path.transform, false);
            float d0 = path.StartLineDistance;
            float w = path.GetWidth(d0);
            Place("SM_Track_FinishLine", root, path.GetPosition(d0) - path.GetTangent(d0) * 0.3f, path.GetRotation(d0), new Vector3(w / 5f, 1f, 1f));
            Place("SM_Track_FinishArch", root, path.GetPosition(d0), path.GetRotation(d0));
            float gate = d0 - grid.FirstRowOffset + GateAheadOfGrid;
            Place("SM_Track_StartGate", root, path.GetPosition(gate), path.GetRotation(gate));
            for (int i = 0; i < FinishCanopyModules; i++)
            {
                float d = d0 + 1.5f + i * 4f;
                Vector3 a = path.GetPosition(d), b = path.GetPosition(d + 4f);
                Place("SM_Track_FinishCanopy_4m", root, a, Quaternion.LookRotation(b - a, path.GetUp(d)));
            }
            for (int i = 0; i < StartGrid.MaxSlots; i++)
            {
                var slot = grid.GetSlot(i, 0.004f);
                Place("SM_Track_GridSlot", root, slot.position, slot.rotation);
            }
        }

        private const float GateAheadOfGrid = 3f;
        private const float FinishArchOffset = 8f;
        private const int FinishCanopyModules = 4;

        /// <summary>Lift stations, cable posts and the cable itself (4 m modules following the spline).</summary>
        private static void BuildArtLift(TrackPath path, TrackMeshBuilder builder, Transform parent)
        {
            if (!path.HasLift) return;
            var root = new GameObject("LiftVisuals").transform;
            root.SetParent(parent, false);
            float s0 = path.LiftStart, s1 = path.LiftStart + path.LiftLength;
            Place("SM_Track_Lift_StationBottom", root, path.GetPosition(s0), path.GetRotation(s0));
            Place("SM_Track_Lift_StationTop", root, path.GetPosition(s1 - 6f), path.GetRotation(s1 - 6f));

            // The cable runs beside the sled lane (right of the centre line) so it does not cut through the tubs.
            for (float s = s0 + 3f; s + 4f <= s1 - 3f + 0.01f; s += 4f)
            {
                Vector3 a = CableLine(path, s), b = CableLine(path, s + 4f);
                Place("SM_Track_Lift_Cable_4m", root, a, Quaternion.LookRotation(b - a, path.GetUp(s)));
            }
            for (float s = s0 + 6f; s <= s1 - 6f; s += 6f)
                Place("SM_Track_Lift_CablePost", root, CableLine(path, s), path.GetRotation(s));

            var rail = PrefabFactory.LoadTrack("SM_Track_Lift_RampRail_4m");
            if (rail != null)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    var placed = builder.PlaceAlong(rail, 4f, side, 0.05f, s0 + 6f, s1 - 6f, root, d => path.IsInLift(d), Inst);
                    ToStartPivot(placed, 4f, side);
                }
            }
        }

        public const float CableLateral = 0.75f;

        private static Vector3 CableLine(TrackPath path, float d) => path.GetPosition(d) + path.GetRight(d) * CableLateral;

        /// <summary>Rails along both edges, catch nets on the outside of sharp curves (lift and gates excluded).</summary>
        private static void BuildArtEdges(TrackPath path, TrackMeshBuilder builder, Transform parent, Options options)
        {
            var rail = PrefabFactory.LoadTrack("SM_Track_Rail_4m");
            var net = PrefabFactory.LoadTrack("SM_Track_Net_4m");
            if (rail == null) return;
            var root = new GameObject("Guardrails").transform;
            root.SetParent(parent, false);
            float d0 = path.StartLineDistance;
            var startGrid = path.GetComponentInChildren<StartGrid>();
            float grid0FirstRow = startGrid != null ? startGrid.FirstRowOffset : 4f;
            bool NearGates(float d)
            {
                float rel = path.SignedDelta(d0, d);
                if (options.FinishArchOnLapLine)
                {
                    float gate = -grid0FirstRow + GateAheadOfGrid;
                    return (rel > -2.5f && rel < 2.5f + FinishCanopyModules * 4f) || (rel > gate - 2.5f && rel < gate + 2.5f);
                }
                return (rel > -2.5f && rel < 2.5f) || (rel > FinishArchOffset - 2.5f && rel < FinishArchOffset + 2.5f);
            }
            System.Func<float, int, bool> allowed = options.EdgeFilter ?? ((d, s) => true);
            foreach (int side in new[] { -1, 1 })
            {
                // Outer side of a right turn (k > 0) is the left edge.
                System.Func<float, bool> outerSharp = d => side < 0 ? MaxCurvature(path, d) > options.NetCurvature : MinCurvature(path, d) < -options.NetCurvature;
                if (net != null)
                    ToStartPivot(builder.PlaceAlong(net, 4f, side, 0.15f, 0f, path.Length, root, d => !path.IsInLift(d) && !NearGates(d) && outerSharp(d) && allowed(d, side), Inst), 4f, side);
                ToStartPivot(builder.PlaceAlong(rail, 4f, side, 0.15f, 0f, path.Length, root,
                    d => !path.IsInLift(d) && !NearGates(d) && !(net != null && outerSharp(d)) && allowed(d, side), Inst), 4f, side);
            }
        }

        /// <summary>Warning signs ~25 m before each corner entry on the right edge (Slow for hairpins).</summary>
        private static void BuildArtSigns(TrackPath path, TrackMeshBuilder builder, Transform parent, Options options)
        {
            var root = new GameObject("Signs").transform;
            root.SetParent(parent, false);
            float straightRun = 0f;
            float lastSign = -1000f;
            for (float d = 0f; d < path.Length; d += 2f)
            {
                float k = path.GetCurvature(d);
                if (Mathf.Abs(k) < 0.012f) { straightRun += 2f; continue; }
                if (Mathf.Abs(k) > 0.03f && straightRun >= 20f && d - lastSign > 40f)
                {
                    float ds = d - 25f;
                    if (!path.IsInLift(ds) && Mathf.Abs(path.SignedDelta(path.StartLineDistance, ds)) > 15f && (options.SignFilter == null || options.SignFilter(ds)))
                    {
                        float peak = 0f;
                        for (float o = 0f; o < 30f; o += 2f) peak = Mathf.Max(peak, Mathf.Abs(path.GetCurvature(d + o)));
                        string model = peak > 0.06f ? "SM_Track_Sign_Slow" : k > 0f ? "SM_Track_Sign_CurveRight" : "SM_Track_Sign_CurveLeft";
                        float off = path.GetWidth(ds) * 0.5f + builder.CurbWidth + 1.1f;
                        Vector3 pos = path.GetPosition(ds) + path.GetRight(ds) * off;
                        Vector3 flatFwd = Vector3.ProjectOnPlane(path.GetTangent(ds), Vector3.up).normalized;
                        Place(model, root, pos, Quaternion.LookRotation(flatFwd, Vector3.up));
                        lastSign = d;
                    }
                }
                straightRun = 0f;
            }
        }


        private static float MaxCurvature(TrackPath p, float d) => Mathf.Max(p.GetCurvature(d - 3f), p.GetCurvature(d), p.GetCurvature(d + 3f));
        private static float MinCurvature(TrackPath p, float d) => Mathf.Min(p.GetCurvature(d - 3f), p.GetCurvature(d), p.GetCurvature(d + 3f));

        private static void SaveTrackMeshes(GameObject track, string sceneName)
        {
            string folder = EditorAssetUtil.TrackGenerated;
            var road = track.transform.Find("Road");
            var barrier = track.transform.Find("Barriers");
            var roadMesh = EditorAssetUtil.SaveMesh(road.GetComponent<MeshFilter>().sharedMesh, folder, $"{sceneName}_Road");
            road.GetComponent<MeshFilter>().sharedMesh = roadMesh;
            var roadCol = EditorAssetUtil.SaveMesh(road.GetComponent<MeshCollider>().sharedMesh, folder, $"{sceneName}_RoadCollider");
            road.GetComponent<MeshCollider>().sharedMesh = roadCol;
            var barrierMesh = EditorAssetUtil.SaveMesh(barrier.GetComponent<MeshCollider>().sharedMesh, folder, $"{sceneName}_Barrier");
            barrier.GetComponent<MeshCollider>().sharedMesh = barrierMesh;
        }

        private static void BuildStartLine(TrackPath path, StartGrid grid, Material white, Material arch, TrackMeshBuilder builder)
        {
            var root = new GameObject("StartFinish").transform;
            root.SetParent(path.transform, false);
            float d = path.StartLineDistance;
            float w = path.GetWidth(d);
            var rot = path.GetRotation(d);
            Vector3 p = path.GetPosition(d);
            Vector3 up = path.GetUp(d);
            var line = EditorAssetUtil.Primitive(PrimitiveType.Cube, "StartLine", root, Vector3.zero, new Vector3(w, 0.02f, 0.5f), white);
            line.transform.SetPositionAndRotation(p + up * 0.01f, rot);

            float postX = w * 0.5f + builder.CurbWidth + 0.6f;
            var archRoot = new GameObject("FinishArch").transform;
            archRoot.SetParent(root, false);
            archRoot.SetPositionAndRotation(p, rot);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Arch_PostL", archRoot, new Vector3(-postX, 2.2f, 0f), new Vector3(0.5f, 4.4f, 0.5f), arch);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Arch_PostR", archRoot, new Vector3(postX, 2.2f, 0f), new Vector3(0.5f, 4.4f, 0.5f), arch);
            EditorAssetUtil.Primitive(PrimitiveType.Cube, "Arch_Banner", archRoot, new Vector3(0f, 4.4f, 0f), new Vector3(postX * 2f + 0.5f, 0.9f, 0.4f), arch);

            for (int i = 0; i < StartGrid.MaxSlots; i++)
            {
                var slot = grid.GetSlot(i, 0.012f);
                var mark = EditorAssetUtil.Primitive(PrimitiveType.Cube, $"GridMark_{i}", root, Vector3.zero, new Vector3(1.1f, 0.01f, 0.12f), white);
                mark.transform.SetPositionAndRotation(slot.position + slot.rotation * new Vector3(0f, 0f, 1.1f), slot.rotation);
            }
        }

        private static void BuildLiftVisuals(TrackPath path, Material cable, Material pylon, Transform parent)
        {
            if (!path.HasLift) return;
            var root = new GameObject("LiftVisuals").transform;
            root.SetParent(parent, false);
            var lr = root.gameObject.AddComponent<LineRenderer>();
            lr.sharedMaterial = cable;
            lr.widthMultiplier = 0.06f;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            var pts = new List<Vector3>();
            for (float s = 0f; s <= path.LiftLength; s += 2f)
            {
                float d = path.LiftStart + s;
                pts.Add(path.GetPosition(d) + path.GetUp(d) * 0.08f);
            }
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());

            for (float s = 0f; s <= path.LiftLength; s += 25f)
            {
                float d = path.LiftStart + s;
                Vector3 p = path.GetPosition(d) + path.GetRight(d) * (path.GetWidth(d) * 0.5f + 1.2f);
                var post = EditorAssetUtil.Primitive(PrimitiveType.Cylinder, "LiftPylon", root, Vector3.zero, new Vector3(0.3f, 2.5f, 0.3f), pylon);
                post.transform.position = p + Vector3.up * 2.5f;
            }
        }

        private static void BuildCameras(SledController player)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 1500f;
            camGo.AddComponent<AudioListener>();
            var brain = camGo.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.6f);
            camGo.transform.SetPositionAndRotation(player.transform.TransformPoint(new Vector3(0f, 2f, -5f)), player.transform.rotation);

            var noise = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset");
            var root = new GameObject("Cameras").transform;

            var third = NewVcam("CM_ThirdPerson", root, 60f);
            var follow = third.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(0f, 2f, -4.8f);
            follow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            follow.TrackerSettings.PositionDamping = new Vector3(0.2f, 0.3f, 0.4f);
            follow.TrackerSettings.RotationDamping = new Vector3(0.2f, 0.6f, 0.2f);
            var compose = third.gameObject.AddComponent<CinemachineRotationComposer>();
            compose.Damping = new Vector2(0.3f, 0.3f);
            compose.TargetOffset = new Vector3(0f, 0.3f, 1.5f);
            AddNoise(third, noise, 0f);

            var first = NewVcam("CM_FirstPerson", root, 70f);
            first.gameObject.AddComponent<CinemachineHardLockToTarget>();
            first.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();
            AddNoise(first, noise, 0f);

            var back = NewVcam("CM_LookBack", root, 65f);
            var backFollow = back.gameObject.AddComponent<CinemachineFollow>();
            backFollow.FollowOffset = new Vector3(0f, 1.6f, 3.2f);
            backFollow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            backFollow.TrackerSettings.PositionDamping = Vector3.zero;
            var backAim = back.gameObject.AddComponent<CinemachineRotationComposer>();
            backAim.TargetOffset = new Vector3(0f, 0.6f, -6f);
            backAim.Damping = Vector2.zero;

            var lift = NewVcam("CM_Lift", root, 55f);
            var liftFollow = lift.gameObject.AddComponent<CinemachineFollow>();
            liftFollow.FollowOffset = new Vector3(6f, 2.5f, 2f);
            liftFollow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            liftFollow.TrackerSettings.PositionDamping = new Vector3(1f, 1f, 1f);
            var liftAim = lift.gameObject.AddComponent<CinemachineRotationComposer>();
            liftAim.Damping = new Vector2(0.8f, 0.8f);

            var orbitCam = NewVcam("CM_FinishOrbit", root, 55f);
            var orbit = orbitCam.gameObject.AddComponent<CinemachineOrbitalFollow>();
            orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            orbit.Radius = 7f;
            orbit.TrackerSettings.BindingMode = BindingMode.LazyFollow;
            orbit.VerticalAxis.Value = 15f;
            orbitCam.gameObject.AddComponent<CinemachineRotationComposer>();

            var director = root.gameObject.AddComponent<SledCameraDirector>();
            director.ThirdPerson = third;
            director.FirstPerson = first;
            director.LookBack = back;
            director.LiftCam = lift;
            director.FinishOrbit = orbitCam;
            director.Target = player;
            director.PlayerInput = player.GetComponent<PlayerSledInput>();
            third.Priority = 20;
        }

        private static CinemachineCamera NewVcam(string name, Transform parent, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var vcam = go.AddComponent<CinemachineCamera>();
            vcam.Lens.FieldOfView = fov;
            vcam.Lens.NearClipPlane = 0.05f;
            vcam.Lens.FarClipPlane = 1500f;
            vcam.Priority = 0;
            return vcam;
        }

        private static void AddNoise(CinemachineCamera cam, NoiseSettings profile, float amplitude)
        {
            if (profile == null) return;
            var n = cam.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            n.NoiseProfile = profile;
            n.AmplitudeGain = amplitude;
            n.FrequencyGain = 1.5f;
        }

        public static void AddToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath && System.IO.File.Exists(s.path)).ToList();
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
