using System.Collections;
using System.IO;
using System.Linq;
using LugeRush.Race;
using LugeRush.Track;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    /// <summary>
    /// Renders of the lift hook and the start gate lamps into Art/Renders (polish_*.png).
    /// Explicit: run with -testFilter LugeRush.Tests.RaceDressingShots (needs a graphics device).
    /// </summary>
    [Explicit, Category("Screenshots")]
    public class RaceDressingShots
    {
        private const int Width = 1280, Height = 720;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Renders"));

        [UnityTest, Timeout(180000)]
        public IEnumerator Capture()
        {
            Directory.CreateDirectory(OutDir);
            RaceSession.Reset();
            RaceSession.Config.Laps = 1;
            RaceSession.Config.BotCount = 0;
            RaceSession.PlayerAutopilot = true;
            SceneManager.LoadScene("Track_Alpine", LoadSceneMode.Single);
            for (int i = 0; i < 5; i++) yield return null;

            var rm = RaceManager.Instance;
            var track = Object.FindFirstObjectByType<TrackPath>();
            var lift = Object.FindFirstObjectByType<LiftSection>();
            var cam = Camera.main;
            var brain = cam.GetComponent("CinemachineBrain") as Behaviour;
            if (brain != null) brain.enabled = false;
            cam.nearClipPlane = 0.05f;

            var lamp = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.name == "Lamp_Green");
            Vector3 lampPos = lamp.transform.parent != null ? lamp.bounds.center : lamp.transform.position;
            Vector3 gateFront = lamp.transform.forward;
            int shots = 0;
            rm.CountdownTick += t => shots++;
            while (shots < 2) yield return null; // two reds lit
            yield return Shot(cam, lampPos - gateFront * 4f - Vector3.up * 0.4f, lampPos, 40f, "polish_gate_red.png");
            while (rm.State != RaceState.Racing) yield return null;
            yield return Shot(cam, lampPos - gateFront * 4f - Vector3.up * 0.4f, lampPos, 40f, "polish_gate_green.png");

            var p = rm.Participants[0];
            float d = track.Wrap(track.LiftStart - 2f);
            Vector3 up = track.GetUp(d), tan = track.GetTangent(d);
            p.Controller.ResetTo(track.GetPosition(d) + up * p.Controller.RestHoverHeight, Quaternion.LookRotation(tan, up));
            p.Controller.SetVelocity(tan * 15f);
            p.Follower.SetDistanceHint(d);
            rm.NotifyTeleported(p, d);

            Time.timeScale = 2f;
            float t0 = Time.time;
            while (track.SignedDelta(track.LiftStart, p.Follower.Distance) < 45f && Time.time - t0 < 15f) yield return new WaitForFixedUpdate();
            Time.timeScale = 0f;
            yield return null;

            var hook = lift.GetComponentsInChildren<Transform>().First(t => t.parent == lift.transform && t.name.Contains("Hook"));
            Debug.Log($"[Dressing] hook pos={hook.position} up={hook.up} scale={hook.lossyScale} sled={p.transform.position}");
            Vector3 right = p.transform.right, sledUp = p.transform.up;
            Vector3 focus = (hook.position + p.transform.position) * 0.5f;
            yield return Shot(cam, focus + right * 2.6f + sledUp * 1.1f - p.transform.forward * 1.2f, focus, 42f, "polish_lift_hook.png");
            yield return Shot(cam, hook.position + right * 0.9f + sledUp * 0.5f + p.transform.forward * 0.5f, hook.position - right * 0.2f, 40f, "polish_lift_hook_close.png");
            Time.timeScale = 1f;
            if (brain != null) brain.enabled = true;
        }

        private static IEnumerator Shot(Camera cam, Vector3 pos, Vector3 look, float fov, string file)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
            cam.fieldOfView = fov;
            yield return null;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
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
