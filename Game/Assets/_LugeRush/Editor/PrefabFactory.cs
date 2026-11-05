using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.Editor
{
    /// <summary>
    /// Builds one static prefab (model prefab variant) per environment / track kit FBX with fitting colliders:
    /// big rocks, walls, cliffs, tunnels, arches and buildings get MeshColliders, tree trunks capsules, small
    /// props boxes / convex hulls, plants and decals nothing. Water faces (palette row 9) are split into a
    /// "Water" child with M_Water. Safe to re-run.
    /// Batch: -executeMethod LugeRush.Editor.PrefabFactory.BuildAll
    /// </summary>
    public static class PrefabFactory
    {
        public const string EnvModels = EditorAssetUtil.Root + "/Art/Models/Environment";
        public const string TrackModels = EditorAssetUtil.Root + "/Art/Models/Track";
        public const string EnvPrefabs = EditorAssetUtil.Root + "/Prefabs/Environment";
        public const string TrackPrefabs = EditorAssetUtil.Root + "/Prefabs/Track";
        public const string GeneratedMeshes = EnvPrefabs + "/Meshes";

        public enum ColliderKind { None, Mesh, Convex, Box, Trunk, Pole }

        private const StaticEditorFlags StaticFlags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic
                                                      | StaticEditorFlags.OccluderStatic | StaticEditorFlags.ReflectionProbeStatic
                                                      | StaticEditorFlags.ContributeGI;

        [MenuItem("Luge Rush/Art/Build Environment + Track Prefabs")]
        public static void BuildAll()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.Palette) == null) ArtMaterialFactory.BuildAll();
            int env = BuildFolder(EnvModels, EnvPrefabs, true);
            int track = BuildTrackPrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log($"[PrefabFactory] Built {env} environment and {track} track prefabs.");
        }

        public static int BuildTrackPrefabs() => BuildFolder(TrackModels, TrackPrefabs, false);

        /// <summary>Prefab path for an FBX file name, e.g. SM_Track_Rail_4m -> Prefabs/Track/Track_Rail_4m.prefab.</summary>
        public static string TrackPrefabPath(string modelName) => $"{TrackPrefabs}/{PrefabName(modelName)}.prefab";

        public static GameObject LoadTrack(string modelName)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(TrackPrefabPath(modelName));
            if (p != null) return p;
            BuildTrackPrefabs();
            return AssetDatabase.LoadAssetAtPath<GameObject>(TrackPrefabPath(modelName));
        }

        private static string PrefabName(string modelName) => modelName.StartsWith("SM_") ? modelName.Substring(3) : modelName;

        private static int BuildFolder(string modelFolder, string prefabFolder, bool environment)
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { modelFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) continue;
                string sub = environment ? Path.GetFileName(Path.GetDirectoryName(path)) : "";
                string folder = environment ? $"{prefabFolder}/{sub}" : prefabFolder;
                EditorAssetUtil.EnsureFolder(folder);
                BuildPrefab(model, $"{folder}/{PrefabName(model.name)}.prefab");
                n++;
            }
            return n;
        }

        public static ColliderKind Classify(string name)
        {
            bool Has(params string[] keys) => keys.Any(k => name.IndexOf(k, System.StringComparison.Ordinal) >= 0);
            if (Has("MountainCard", "SnowPeaks", "MesaCard")) return ColliderKind.None; // far background cards
            if (Has("Grass", "Fern", "Flower", "Banana", "DryBush", "Tumbleweed", "VineCurtain", "PricklyPear", "Gravel",
                    "Stream", "Waterfall", "MineRail"))
                return ColliderKind.None;
            // Track kit: the invisible barrier walls already contain the sleds; edge modules / decals are visual only.
            if (Has("_Curb_", "_Rail_", "_Net_", "RampRail", "Lift_Cable", "Lift_Hook", "CablePost", "GridSlot",
                    "StartLine", "FinishLine", "Sign_", "MarkerPost",
                    // structures spanning the lane (pulleys, lamp housings, roofs): the sleds pass through/under them
                    "Lift_Station", "StartGate", "FinishArch", "FinishCanopy"))
                return ColliderKind.None;
            if (Has("TallTree", "GiantTree", "Pine_", "Saguaro", "Bamboo")) return ColliderKind.Trunk;
            if (Has("LightPole")) return ColliderKind.Pole;
            if (Has("Fence_", "MineCart", "TrashBin")) return ColliderKind.Box;
            if (Has("RockMedium", "RockSmall", "Log_", "Stump_", "FallenPine", "MossRock_01")) return ColliderKind.Convex;
            return ColliderKind.Mesh; // big rocks, cliffs, walls, mesas, arches, tunnels, buildings, gates, stations, slopes
        }

        private static GameObject BuildPrefab(GameObject model, string prefabPath)
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                SplitWater(root);
                AddColliders(root, Classify(model.name));
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticFlags);
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    // Far background cards don't need to cast shadows across the whole valley.
                    if (Classify(model.name) == ColliderKind.None && model.name.Contains("Card")) r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveGI = ReceiveGI.LightProbes;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------- Colliders ----------

        private static void AddColliders(GameObject root, ColliderKind kind)
        {
            if (kind == ColliderKind.None) return;
            var mf = root.GetComponent<MeshFilter>();
            if (mf == null) mf = root.GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return;
            var host = mf.gameObject;
            var mesh = mf.sharedMesh;
            switch (kind)
            {
                case ColliderKind.Mesh:
                    host.AddComponent<MeshCollider>().sharedMesh = mesh;
                    break;
                case ColliderKind.Convex:
                    var mc = host.AddComponent<MeshCollider>();
                    mc.sharedMesh = mesh;
                    mc.convex = true;
                    break;
                case ColliderKind.Box:
                    var box = host.AddComponent<BoxCollider>();
                    box.center = mesh.bounds.center;
                    box.size = mesh.bounds.size;
                    break;
                case ColliderKind.Pole:
                {
                    var cap = host.AddComponent<CapsuleCollider>();
                    cap.direction = 1;
                    cap.radius = 0.12f;
                    cap.height = mesh.bounds.size.y;
                    cap.center = new Vector3(0f, mesh.bounds.size.y * 0.5f, 0f);
                    break;
                }
                case ColliderKind.Trunk:
                    AddTrunk(host, mesh);
                    break;
            }
        }

        /// <summary>Capsule around the trunk: centroid / spread of the vertices between 0.2 and 2 m height.</summary>
        private static void AddTrunk(GameObject host, Mesh mesh)
        {
            var verts = mesh.vertices;
            var low = verts.Where(v => v.y > 0.2f && v.y < 2.0f).ToList();
            if (low.Count < 4) low = verts.Where(v => v.y < mesh.bounds.min.y + 2f).ToList();
            if (low.Count == 0) return;
            // Trunk = vertices near the pivot axis (ignores low leaves / roots further out).
            var nearAxis = low.Where(v => new Vector2(v.x, v.z).magnitude < 3f).ToList();
            if (nearAxis.Count >= 4) low = nearAxis;
            Vector2 c = Vector2.zero;
            foreach (var v in low) c += new Vector2(v.x, v.z);
            c /= low.Count;
            var dists = low.Select(v => (new Vector2(v.x, v.z) - c).magnitude).OrderBy(d => d).ToList();
            float radius = Mathf.Clamp(dists[Mathf.Clamp((int)(dists.Count * 0.7f), 0, dists.Count - 1)], 0.15f, 2.0f);
            float height = Mathf.Clamp(mesh.bounds.size.y * 0.5f, 2f, 8f);
            var cap = host.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = radius;
            cap.height = Mathf.Max(height, radius * 2f);
            cap.center = new Vector3(c.x, cap.height * 0.5f, c.y);
        }

        // ---------- Water split ----------

        /// <summary>Palette row 9 columns 0-3 = water_light/base/dark/deep (LR_Palette.json).</summary>
        private static bool IsWaterUv(Vector2 uv) => uv.x >= 0f && uv.x < 4f / 16f && uv.y > 1f - 10f / 16f && uv.y < 1f - 9f / 16f;

        private static void SplitWater(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || mesh.subMeshCount != 1) continue;
                var uv = mesh.uv;
                if (uv == null || uv.Length == 0) continue;
                var tris = mesh.triangles;
                var dry = new List<int>();
                var wet = new List<int>();
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector2 c = (uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f;
                    var list = IsWaterUv(c) ? wet : dry;
                    list.Add(tris[i]); list.Add(tris[i + 1]); list.Add(tris[i + 2]);
                }
                if (wet.Count == 0) continue;

                var dryMesh = SubsetMesh(mesh, dry, mesh.name + "_Solid");
                var wetMesh = SubsetMesh(mesh, wet, mesh.name + "_Water");
                mf.sharedMesh = EditorAssetUtil.SaveMesh(dryMesh, GeneratedMeshes, dryMesh.name);
                var water = new GameObject("Water");
                water.transform.SetParent(mf.transform, false);
                water.AddComponent<MeshFilter>().sharedMesh = EditorAssetUtil.SaveMesh(wetMesh, GeneratedMeshes, wetMesh.name);
                var wr = water.AddComponent<MeshRenderer>();
                wr.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtMaterialFactory.Water);
                wr.shadowCastingMode = ShadowCastingMode.Off;
                Debug.Log($"[PrefabFactory] {root.name}: split {wet.Count / 3} water triangles into a Water child.");
            }
        }

        private static Mesh SubsetMesh(Mesh src, List<int> tris, string name)
        {
            var srcVerts = src.vertices;
            var srcNormals = src.normals;
            var srcUv = src.uv;
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var outTris = new List<int>(tris.Count);
            foreach (int t in tris)
            {
                if (!map.TryGetValue(t, out int ni))
                {
                    ni = verts.Count;
                    map[t] = ni;
                    verts.Add(srcVerts[t]);
                    if (srcNormals.Length > 0) normals.Add(srcNormals[t]);
                    uvs.Add(srcUv[t]);
                }
                outTris.Add(ni);
            }
            var m = new Mesh { name = name };
            if (verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(verts);
            if (normals.Count == verts.Count) m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetTriangles(outTris, 0);
            if (normals.Count != verts.Count) m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
