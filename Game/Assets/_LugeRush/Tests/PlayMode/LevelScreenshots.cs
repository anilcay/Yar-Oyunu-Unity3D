using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    internal static class LevelTestUtil
    {
        public static void BeginStripHud()
        {
            // Map checks do not depend on HUD work in progress.
            LogAssert.ignoreFailingMessages = true;
            SceneManager.sceneLoaded += StripHud;
        }

        public static void EndStripHud() => SceneManager.sceneLoaded -= StripHud;

        private static void StripHud(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && mb.GetType().Namespace == "LugeRush.UI") Object.DestroyImmediate(mb);
        }
    }

    /// <summary>
    /// Map renders from the running game into Art/Renders/map_*.png plus render statistics.
    /// Explicit: run with -testFilter LugeRush.Tests.LevelScreenshots (needs a graphics device).
    /// </summary>
    [Explicit, Category("Screenshots")]
    public class LevelScreenshots
    {
        private const int Width = 1920, Height = 1080;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Renders"));

        private Camera cam;

        [UnityTest, Timeout(900000)]
        public IEnumerator Capture([Values("Forest", "Alpine", "Canyon")] string biome)
        {
            Directory.CreateDirectory(OutDir);
            string prefix = "map_" + biome.ToLowerInvariant() + "_";
            RaceSession.Reset();
            RaceSession.Config.Laps = 1;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = BotDifficulty.Normal;
            RaceSession.PlayerAutopilot = true;
            RaceSession.PlayerSled = SledType.Classic;
            SceneManager.LoadScene("Track_" + biome, LoadSceneMode.Single);
            for (int i = 0; i < 10; i++) yield return null;

            var rm = RaceManager.Instance;
            var player = rm.Participants.First(p => p.IsLocalPlayer);
            var track = Object.FindFirstObjectByType<TrackPath>();
            cam = Camera.main;
            var brain = cam.GetComponent("CinemachineBrain") as Behaviour;
            var props = Object.FindFirstObjectByType<LevelPropRenderer>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2500f;

            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            using var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var stats = new StringBuilder($"[LevelStats] {biome}\n");

            var t = player.transform;
            brain.enabled = false;

            // Grid, countdown running.
            yield return Shot(t.TransformPoint(new Vector3(0.9f, 2.1f, -5.2f)), t.TransformPoint(new Vector3(0.9f, 0.7f, 4f)), 60f, prefix + "grid.png");
            yield return null;
            stats.AppendLine($"  grid: batches={batches.LastValue} draws={draws.LastValue} tris={tris.LastValue} setpass={setpass.LastValue} propDraws={props.VisibleDraws} propInst={props.VisibleInstances} propTris={props.VisibleTriangles}");

            // Free viewpoints.
            var vp = Object.FindFirstObjectByType<LevelViewpoints>();
            foreach (var v in vp.Points)
            {
                yield return Shot(v.Position, v.Target, v.Fov, prefix + v.Name + ".png");
                yield return null;
                stats.AppendLine($"  {v.Name}: batches={batches.LastValue} draws={draws.LastValue} tris={tris.LastValue} setpass={setpass.LastValue} propDraws={props.VisibleDraws} propInst={props.VisibleInstances} propTris={props.VisibleTriangles}");
            }

            // Overview without fog.
            RenderSettings.fog = false;
            LevelPropRenderer.DistanceScale = 4f;
            Bounds b = new Bounds(track.GetPosition(0f), Vector3.zero);
            for (float d = 0f; d < track.Length; d += 10f) b.Encapsulate(track.GetPosition(d));
            float size = Mathf.Max(b.size.x, b.size.z);
            yield return Shot(b.center + new Vector3(-0.42f, 0.75f, -0.8f) * size, b.center, 42f, prefix + "overview.png");
            LevelPropRenderer.DistanceScale = 1f;
            RenderSettings.fog = true;
            brain.enabled = true;

            // Race.
            while (rm.State != RaceState.Racing) yield return null;
            float go = Time.time;
            Time.timeScale = 4f;
            while (Time.time - go < 22f) yield return null;
            Time.timeScale = 1f;
            brain.enabled = false;
            yield return Shot(t.TransformPoint(new Vector3(0f, 2.0f, -4.8f)), t.TransformPoint(new Vector3(0f, 0.6f, 3f)), 62f, prefix + "third_person.png");
            yield return null;
            stats.AppendLine($"  race: batches={batches.LastValue} draws={draws.LastValue} tris={tris.LastValue} setpass={setpass.LastValue} propDraws={props.VisibleDraws} propInst={props.VisibleInstances} propTris={props.VisibleTriangles}");

            // GPU cost of one 1080p frame (forced to finish by a 1 pixel read back).
            var timingRt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var timingTex = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var watch = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                cam.targetTexture = timingRt;
                if (i >= 10) watch.Start();
                cam.Render();
                RenderTexture.active = timingRt;
                timingTex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                RenderTexture.active = null;
                if (i >= 10) watch.Stop();
            }
            cam.targetTexture = null;
            stats.AppendLine($"  frame: {watch.Elapsed.TotalMilliseconds / 30.0:0.0} ms on {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})");
            timingRt.Release();
            Object.Destroy(timingRt);
            Object.Destroy(timingTex);

            var fp = SledVisuals.FindDeep(t, "Camera_FP");
            yield return null;
            SetFirstPerson(player, true);
            yield return Shot(fp.position, fp.position + fp.forward, 78f, prefix + "first_person.png", fp.up, () => SetFirstPerson(player, true));
            SetFirstPerson(player, false);
            brain.enabled = true;
            Debug.Log(stats.ToString());
        }

        private static void SetFirstPerson(RaceParticipant p, bool on)
        {
            foreach (var a in p.GetComponentsInChildren<RiderAppearance>())
                if (a.transform.parent != null && a.transform.parent.name == "Seat") a.SetFirstPerson(on);
        }

        private IEnumerator Shot(Vector3 pos, Vector3 look, float fov, string file, Vector3? up = null, System.Action before = null)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, up ?? Vector3.up));
            cam.fieldOfView = fov;
            yield return null; // prop culling sees the new pose
            before?.Invoke();
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, up ?? Vector3.up));
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
            File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }

        [SetUp] public void SetUp() => LevelTestUtil.BeginStripHud();

        [TearDown]
        public void TearDown()
        {
            LevelTestUtil.EndStripHud();
            Time.timeScale = 1f;
            RaceSession.Reset();
        }
    }
}
