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
            public float GroundSize = 700f;
            public Vector3 GroundCenter = Vector3.zero;
            public BotDifficulty DefaultDifficulty = BotDifficulty.Normal;
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
            BuildEnvironment(options);

            var track = BuildTrack(def, scene.name, guardrail, options);
            var path = track.GetComponent<TrackPath>();
            var grid = track.GetComponentInChildren<StartGrid>();

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

            var hud = new GameObject("HUD").AddComponent<DebugRaceHud>();
            hud.Manager = manager;
            hud.Player = roster[0];

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
            var road = EditorAssetUtil.LitMaterial(mats, "M_Asphalt", new Color(0.13f, 0.13f, 0.14f), 0.55f);
            var curbY = EditorAssetUtil.LitMaterial(mats, "M_CurbYellow", new Color(1f, 0.78f, 0.05f), 0.4f);
            var curbB = EditorAssetUtil.LitMaterial(mats, "M_CurbBlack", new Color(0.04f, 0.04f, 0.04f), 0.4f);
            var skirt = EditorAssetUtil.LitMaterial(mats, "M_Embankment", new Color(0.33f, 0.29f, 0.22f), 0.1f);
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
