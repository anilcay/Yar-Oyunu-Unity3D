using System.IO;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Renders preview images of Dev_Greybox (3rd person, 1st person, overview) into Art/Renders.
    /// Batch (needs a graphics device, i.e. no -nographics):
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod LugeRush.Editor.GreyboxScreenshot.Capture
    /// </summary>
    public static class GreyboxScreenshot
    {
        private const int Width = 1600;
        private const int Height = 900;

        [MenuItem("Luge Rush/Capture Greybox Screenshots")]
        public static void Capture()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Renders"));
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(GreyboxBuilder.ScenePath);

            RaceParticipant player = null;
            var catalog = AssetDatabase.LoadAssetAtPath<SledCatalog>(SledAssetFactory.CatalogPath);
            int i = 0;
            foreach (var p in Object.FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None))
            {
                if (p.IsLocalPlayer) player = p;
                // Runtime-only bits: colours (property blocks) and visual ground offset.
                var visuals = p.GetComponent<SledVisuals>();
                var sled = p.GetComponent<SledController>();
                if (catalog != null && visuals != null)
                    visuals.ApplyColors(catalog.GetBodyColor((SledColor)(p.GridSlot % 4)), catalog.GetHelmetColor(p.GridSlot));
                var root = p.transform.Find(SledVisuals.VisualRootName);
                if (root != null && sled != null) root.localPosition = new Vector3(0f, -sled.RestHoverHeight, 0f);
                i++;
            }
            if (player == null)
            {
                Debug.LogError("[GreyboxScreenshot] No local player found.");
                return;
            }

            var camGo = new GameObject("ScreenshotCamera");
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 2000f;

            var t = player.transform;
            Vector3 tpPos = t.TransformPoint(new Vector3(0f, 2.0f, -4.8f));
            Vector3 tpLook = t.TransformPoint(new Vector3(0f, 0.6f, 3f));
            Render(cam, tpPos, Quaternion.LookRotation(tpLook - tpPos, Vector3.up), 60f, Path.Combine(outDir, "dev_greybox.png"));

            var fp = SledVisuals.FindDeep(t, "Camera_FP");
            foreach (var r in t.GetComponentsInChildren<Renderer>())
                if (r.name.StartsWith("Helmet")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            Render(cam, fp.position, fp.rotation, 75f, Path.Combine(outDir, "dev_greybox_fp.png"));

            var track = Object.FindFirstObjectByType<TrackPath>();
            track.EnsureBuilt();
            Vector3 center = new Vector3(55f, 30f, -80f);
            Vector3 overPos = center + new Vector3(-170f, 330f, -260f);
            Render(cam, overPos, Quaternion.LookRotation(center - overPos, Vector3.up), 50f, Path.Combine(outDir, "dev_greybox_overview.png"));

            // Mid-track views: banked curve and hairpin.
            RenderTrackView(cam, track, 300f, Path.Combine(outDir, "dev_greybox_banked.png"));
            RenderTrackView(cam, track, track.LiftStart - 300f, Path.Combine(outDir, "dev_greybox_hairpin.png"));

            Object.DestroyImmediate(camGo);
            Debug.Log($"[GreyboxScreenshot] Saved renders to {outDir}");
        }

        private static void RenderTrackView(UnityEngine.Camera cam, TrackPath track, float distance, string file)
        {
            Vector3 p = track.GetPosition(distance) + track.GetUp(distance) * 1.6f - track.GetTangent(distance) * 3f;
            Vector3 look = track.GetPosition(distance + 25f) + Vector3.up * 0.5f;
            Render(cam, p, Quaternion.LookRotation(look - p, Vector3.up), 65f, file);
        }

        private static void Render(UnityEngine.Camera cam, Vector3 pos, Quaternion rot, float fov, string file)
        {
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
