using System.Collections.Generic;
using System.IO;
using System.Linq;
using LugeRush.Network;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.UI;
using LugeRush.Vehicle;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LugeRush.Editor
{
    /// <summary>
    /// Builds the front end: GameContent + TrackCatalog assets, network prefabs (lobby, race authority,
    /// network sled carrier) and their NetworkPrefabsList, Boot.unity (NetworkManager + UnityTransport +
    /// session service), MainMenu.unity, and the build scene order (Boot, MainMenu, tracks).
    /// Batch: -executeMethod LugeRush.Editor.MenuSceneBuilder.BuildAll
    /// </summary>
    public static class MenuSceneBuilder
    {
        public const string UiFolder = EditorAssetUtil.Root + "/Prefabs/UI";
        public const string NetworkFolder = EditorAssetUtil.Root + "/Prefabs/Network";
        public const string ContentPath = UiFolder + "/Resources/" + GameContent.ResourceName + ".asset";
        public const string TrackCatalogPath = UiFolder + "/TrackCatalog.asset";
        public const string LobbyPrefabPath = NetworkFolder + "/NetworkLobby.prefab";
        public const string RacePrefabPath = NetworkFolder + "/NetworkRace.prefab";
        public const string SledPrefabPath = NetworkFolder + "/NetworkSled.prefab";
        public const string PrefabListPath = NetworkFolder + "/LugeRushNetworkPrefabs.asset";
        public const string BootScenePath = EditorAssetUtil.Root + "/Scenes/Boot.unity";
        public const string MenuScenePath = EditorAssetUtil.Root + "/Scenes/MainMenu.unity";
        public const string ScenesFolder = EditorAssetUtil.Root + "/Scenes";

        /// <summary>Fonts + everything below (single batch launch).</summary>
        public static void BuildEverything()
        {
            UIFontBuilder.BuildFonts();
            BuildAll();
        }

        [MenuItem("Luge Rush/UI/Build Menus + Network")]
        public static void BuildAll()
        {
            var content = BuildContent();
            var prefabs = BuildNetworkPrefabs();
            BuildBootScene(prefabs);
            BuildMenuScene();
            ApplyBuildOrder();
            AssetDatabase.SaveAssets();
            Debug.Log($"[MenuSceneBuilder] Done. Tracks: {string.Join(", ", content.Tracks.Tracks.Select(t => t.TrackId))}");
        }

        // ------------------------------------------------------------------ Assets

        public static GameContent BuildContent()
        {
            EditorAssetUtil.EnsureFolder(UiFolder + "/Resources");
            var tracks = EditorAssetUtil.LoadOrCreate<TrackCatalog>(TrackCatalogPath);
            var greybox = AssetDatabase.LoadAssetAtPath<TrackDefinition>(GreyboxBuilder.DefinitionPath);
            tracks.Tracks = new[]
            {
                Track("forest", "Yeşil Vadi", "Track_Forest", "Tropik orman: bambu koruları, ıslak asfalt, ahşap köprü ve flamalı bitiş alanı. Öğle güneşi, hafif sis.", new Color(0.2f, 0.7f, 0.3f), null),
                Track("alpine", "Çam Zirvesi", "Track_Alpine", "Alp dağı: çam ormanı, şelale ve dere, uçurum kenarında panoramik viraj, kaya tüneli.", new Color(0.3f, 0.55f, 0.9f), null),
                Track("canyon", "Kızıl Kanyon", "Track_Canyon", "Çöl kanyonu: kızıl kaya kuleleri, dar geçit, doğal kaya kemeri. Gün batımı.", new Color(0.9f, 0.4f, 0.2f), null),
                Track("dev_greybox", "Geliştirme Pisti", "Dev_Greybox", "Test pisti (≈1.15 km): uzun düzlük, eğimli 180°, S-viraj, iki saç tokası ve ≈150 m lift.", new Color(0.6f, 0.6f, 0.65f), greybox),
            };
            EditorUtility.SetDirty(tracks);

            var content = EditorAssetUtil.LoadOrCreate<GameContent>(ContentPath);
            content.BodyFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.BodyFontPath);
            content.DisplayFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UIFontBuilder.DisplayFontPath);
            content.Sleds = AssetDatabase.LoadAssetAtPath<SledCatalog>(SledAssetFactory.CatalogPath);
            content.Tracks = tracks;
            content.SledPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SledAssetFactory.SledPrefabPath);
            content.InputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(SledAssetFactory.InputActionsPath);
            content.Difficulties = new[]
            {
                SledAssetFactory.LoadDifficulty(BotDifficulty.Easy),
                SledAssetFactory.LoadDifficulty(BotDifficulty.Normal),
                SledAssetFactory.LoadDifficulty(BotDifficulty.Hard),
            };
            content.AutopilotDifficulty = content.Difficulties[1];
            content.LiftHookPrefab = PrefabFactory.LoadTrack("SM_Track_Lift_Hook");
            EditorUtility.SetDirty(content);
            if (content.BodyFont == null) Debug.LogWarning("[MenuSceneBuilder] UI fonts missing: run UIFontBuilder.BuildFonts.");
            if (content.Sleds == null || content.SledPrefab == null) Debug.LogWarning("[MenuSceneBuilder] Sled catalog/prefab missing.");
            AssetDatabase.SaveAssets();
            return content;
        }

        private static TrackCatalog.Entry Track(string id, string name, string scene, string desc, Color color, TrackDefinition def) =>
            new TrackCatalog.Entry { TrackId = id, DisplayName = name, SceneName = scene, Description = desc, ThemeColor = color, Definition = def };

        public struct NetPrefabs
        {
            public GameObject Lobby, Race, Sled;
            public NetworkPrefabsList List;
        }

        public static NetPrefabs BuildNetworkPrefabs()
        {
            EditorAssetUtil.EnsureFolder(NetworkFolder);
            var r = new NetPrefabs
            {
                Lobby = SavePrefab("NetworkLobby", LobbyPrefabPath, go => go.AddComponent<NetworkLobby>(), false),
                Race = SavePrefab("NetworkRace", RacePrefabPath, go => go.AddComponent<NetworkRaceAuthority>(), false),
                Sled = SavePrefab("NetworkSled", SledPrefabPath, go =>
                {
                    var nt = go.AddComponent<NetworkTransform>();
                    nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                    nt.Interpolate = true;
                    nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                    nt.InLocalSpace = false;
                    nt.UseHalfFloatPrecision = false;
                    nt.PositionThreshold = 0.005f;
                    go.AddComponent<NetworkSled>();
                }, true),
            };

            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, PrefabListPath);
            }
            foreach (var p in list.PrefabList.ToList()) list.Remove(p);
            foreach (var go in new[] { r.Lobby, r.Race, r.Sled }) list.Add(new NetworkPrefab { Prefab = go });
            EditorUtility.SetDirty(list);
            r.List = list;
            AssetDatabase.SaveAssets();
            return r;
        }

        private static GameObject SavePrefab(string name, string path, System.Action<GameObject> setup, bool dontDestroyWithOwner)
        {
            var go = new GameObject(name);
            var no = go.AddComponent<NetworkObject>();
            no.DontDestroyWithOwner = dontDestroyWithOwner;
            no.SynchronizeTransform = true;
            no.ActiveSceneSynchronization = false;
            no.SceneMigrationSynchronization = true;
            setup(go);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            // The GlobalObjectIdHash comes from the asset GUID, which did not exist before the first save.
            var asset = prefab.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                ?.Invoke(asset, null);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            if (asset.PrefabIdHash == 0) Debug.LogError($"[MenuSceneBuilder] {name}: GlobalObjectIdHash not generated.");
            else Debug.Log($"[MenuSceneBuilder] {name}: GlobalObjectIdHash {asset.PrefabIdHash}");
            return prefab;
        }

        // ------------------------------------------------------------------ Scenes

        public static void BuildBootScene(NetPrefabs prefabs)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Boot Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.08f, 0.13f);

            var boot = new GameObject("Boot");
            var nm = boot.AddComponent<NetworkManager>();
            var transport = boot.AddComponent<UnityTransport>();
            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.EnableSceneManagement = true;
            nm.NetworkConfig.TickRate = 30;
            nm.NetworkConfig.ClientConnectionBufferTimeout = 15;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { prefabs.List };
            transport.SetConnectionData("127.0.0.1", (ushort)NetworkSessionService.DefaultPort, "0.0.0.0");

            var svc = boot.AddComponent<NetworkSessionService>();
            var so = new SerializedObject(svc);
            so.FindProperty("lobbyPrefab").objectReferenceValue = prefabs.Lobby.GetComponent<NetworkObject>();
            so.FindProperty("raceAuthorityPrefab").objectReferenceValue = prefabs.Race.GetComponent<NetworkObject>();
            so.FindProperty("sledPrefab").objectReferenceValue = prefabs.Sled.GetComponent<NetworkObject>();
            so.ApplyModifiedPropertiesWithoutUndo();
            boot.AddComponent<AppBoot>();
            boot.AddComponent<RaceAutomation>();

            EditorAssetUtil.EnsureFolder(ScenesFolder);
            EditorSceneManager.SaveScene(scene, BootScenePath);
        }

        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(38f, -140f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.ambientMode = AmbientMode.Skybox;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();

            var stageGo = new GameObject("PreviewStage");
            var stage = stageGo.AddComponent<SledPreviewStage>();
            stage.StageCamera = cam;

            var menuGo = new GameObject("MainMenu");
            var menu = menuGo.AddComponent<MainMenuController>();
            menu.Stage = stage;

            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        /// <summary>Boot, MainMenu, then every track scene that exists (order of the catalog, then the rest).</summary>
        /// <summary>Batch: points GameContent at the lift hook prefab without rebuilding the menus.</summary>
        public static void AssignLiftHook()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPath);
            content.LiftHookPrefab = PrefabFactory.LoadTrack("SM_Track_Lift_Hook");
            EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MenuSceneBuilder] LiftHookPrefab = {content.LiftHookPrefab}");
        }

        public static void ApplyBuildOrder()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(MenuScenePath, true)
            };
            var catalog = AssetDatabase.LoadAssetAtPath<TrackCatalog>(TrackCatalogPath);
            if (catalog != null)
            {
                foreach (var e in catalog.Tracks)
                {
                    string path = $"{ScenesFolder}/{e.SceneName}.unity";
                    if (File.Exists(path) && list.All(s => s.path != path)) list.Add(new EditorBuildSettingsScene(path, true));
                }
            }
            foreach (var s in EditorBuildSettings.scenes)
                if (File.Exists(s.path) && list.All(x => x.path != s.path)) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[MenuSceneBuilder] Build scenes: " + string.Join(", ", list.Select(s => Path.GetFileNameWithoutExtension(s.path))));
        }
    }
}
