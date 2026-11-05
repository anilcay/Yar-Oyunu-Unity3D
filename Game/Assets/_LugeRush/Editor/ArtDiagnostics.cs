using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace LugeRush.Editor
{
    /// <summary>
    /// Integration diagnostics: dumps the imported FBX hierarchies (names, local transforms, materials, bounds)
    /// and compares the rider seat pose under Humanoid vs Generic rigs. Output: Logs/art_*.txt.
    /// </summary>
    public static class ArtDiagnostics
    {
        private static string LogDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Logs"));

        [MenuItem("Luge Rush/Art/Dump Model Hierarchies")]
        public static void DumpModels()
        {
            var sb = new StringBuilder();
            var guids = AssetDatabase.FindAssets("t:Model", new[] { EditorAssetUtil.Root + "/Art/Models" });
            foreach (var g in guids.OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                bool detailed = !path.Contains("/Environment/");
                var b = Bounds(go);
                int tris = go.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0)
                         + go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(f => f.sharedMesh != null ? f.sharedMesh.triangles.Length / 3 : 0);
                sb.AppendLine($"## {Path.GetFileNameWithoutExtension(path)} tris={tris} bounds.center={V(b.center)} size={V(b.size)}");
                Dump(go.transform, 1, sb, detailed);
            }
            File.WriteAllText(Path.Combine(LogDir, "art_dump.txt"), sb.ToString());
            Debug.Log("[ArtDiagnostics] wrote art_dump.txt");
        }

        private static void Dump(Transform t, int depth, StringBuilder sb, bool detailed)
        {
            var pad = new string(' ', depth * 2);
            string mats = "";
            var r = t.GetComponent<Renderer>();
            if (r != null) mats = " mats=[" + string.Join(",", r.sharedMaterials.Select(m => m != null ? m.name : "null")) + "]";
            string mesh = "";
            var mf = t.GetComponent<MeshFilter>();
            Mesh m0 = mf != null ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
            if (m0 != null) mesh = $" mesh={m0.name} sub={m0.subMeshCount} mb={V(m0.bounds.center)}/{V(m0.bounds.size)}";
            if (detailed || depth <= 1)
                sb.AppendLine($"{pad}{t.name} p={V(t.localPosition)} r={V(t.localEulerAngles)} s={V(t.localScale)}{mats}{mesh}");
            if (!detailed && depth >= 1) return;
            foreach (Transform c in t) Dump(c, depth + 1, sb, detailed);
        }

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        public static string V(Vector3 v) => $"({v.x:0.###},{v.y:0.###},{v.z:0.###})";

        /// <summary>Imports SK_Rider as Humanoid, then as Generic, and logs seated bone positions for Sit_Idle.</summary>
        [MenuItem("Luge Rush/Art/Compare Rider Rigs")]
        public static void CompareRiderRigs()
        {
            var sb = new StringBuilder();
            var keep = ArtImportPostprocessor.RiderRig;
            foreach (var rig in new[] { ModelImporterAnimationType.Human, ModelImporterAnimationType.Generic })
            {
                ArtImportPostprocessor.RiderRig = rig;
                AssetDatabase.ImportAsset(ArtImportPostprocessor.RiderPath, ImportAssetOptions.ForceUpdate);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ArtImportPostprocessor.RiderPath);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(ArtImportPostprocessor.RiderPath).OfType<Avatar>().FirstOrDefault();
                sb.AppendLine($"# rig={rig} avatar={(avatar != null ? avatar.name : "none")} valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman}");
                var clips = AssetDatabase.LoadAllAssetsAtPath(ArtImportPostprocessor.RiderPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                sb.AppendLine("  clips: " + string.Join(", ", clips.Select(c => $"{c.name}({c.length:0.00}s loop={c.isLooping} human={c.humanMotion})")));
                var inst = (GameObject)Object.Instantiate(model);
                try
                {
                    var anim = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
                    anim.avatar = avatar;
                    anim.applyRootMotion = false;
                    foreach (var clipName in new[] { "Sit_Idle", "Steer_Right", "Brake", "Lift_Relax" })
                    {
                        var clip = clips.FirstOrDefault(c => c.name == clipName);
                        if (clip == null) continue;
                        for (int method = 0; method < 3; method++)
                        {
                            PlayableGraph graph = default;
                            if (method == 0) graph = Sample(anim, clip, 0.5f);
                            else if (method == 1) clip.SampleAnimation(inst, 0.5f);
                            else
                            {
                                AnimationMode.StartAnimationMode();
                                AnimationMode.BeginSampling();
                                AnimationMode.SampleAnimationClip(inst, clip, 0.5f);
                                AnimationMode.EndSampling();
                            }
                            sb.AppendLine($"  m{method} {clipName}@0.5: " + string.Join(" ", new[] { "Hips", "Head", "Hand.L", "Hand.R", "Foot.L", "Foot.R", "LowerArm.L" }
                                .Select(n => { var t = SledVisualsFind(inst.transform, n); return t == null ? n + "=?" : $"{n}={V(inst.transform.InverseTransformPoint(t.position))}"; })));
                            if (method == 0) graph.Destroy();
                            if (method == 2) AnimationMode.StopAnimationMode();
                        }
                    }
                }
                finally { Object.DestroyImmediate(inst); }
            }
            ArtImportPostprocessor.RiderRig = keep;
            AssetDatabase.ImportAsset(ArtImportPostprocessor.RiderPath, ImportAssetOptions.ForceUpdate);
            File.WriteAllText(Path.Combine(LogDir, "art_rig_compare.txt"), sb.ToString());
            Debug.Log("[ArtDiagnostics] wrote art_rig_compare.txt\n" + sb);
        }

        /// <summary>Evaluates one clip on an Animator in edit mode (works for Generic and Humanoid).</summary>
        /// <remarks>The caller destroys the returned graph after reading the pose.</remarks>
        public static PlayableGraph Sample(Animator animator, AnimationClip clip, float time)
        {
            var graph = PlayableGraph.Create("ArtSample");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "out", animator);
            var cp = AnimationClipPlayable.Create(graph, clip);
            cp.SetApplyFootIK(false);
            cp.SetTime(time);
            output.SetSourcePlayable(cp);
            graph.Evaluate(0f);
            return graph;
        }

        private static Transform SledVisualsFind(Transform root, string name) => LugeRush.Vehicle.SledVisuals.FindDeep(root, name);
    }
}
