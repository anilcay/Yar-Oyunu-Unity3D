using System.Collections;
using System.IO;
using System.Linq;
using LugeRush.Race;
using LugeRush.Track;
using LugeRush.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LugeRush.Tests
{
    /// <summary>
    /// Art integration renders from the running game (real Animator + IK + sled swaps) into Art/Renders/int_*.png.
    /// Explicit: run with -testFilter LugeRush.Tests.IntegrationScreenshots (needs a graphics device).
    /// </summary>
    [Explicit, Category("Screenshots")]
    public class IntegrationScreenshots
    {
        private const int Width = 1600, Height = 900;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Renders"));

        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureIntegrationRenders()
        {
            Directory.CreateDirectory(OutDir);
            RaceSession.Reset();
            RaceSession.Config.Laps = 1;
            RaceSession.Config.BotCount = 7;
            RaceSession.Config.Difficulty = BotDifficulty.Normal;
            RaceSession.PlayerAutopilot = true;
            RaceSession.PlayerSled = SledType.Classic;
            SceneManager.LoadScene("Dev_Greybox", LoadSceneMode.Single);
            for (int i = 0; i < 10; i++) yield return null;

            var rm = RaceManager.Instance;
            var track = Object.FindFirstObjectByType<TrackPath>();
            var player = rm.Participants.First(p => p.IsLocalPlayer);
            var cam = new GameObject("IntegrationCamera").AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 1500f;
            var mainCam = Camera.main;
            if (mainCam != null) cam.CopyFrom(mainCam);
            cam.enabled = false;
            cam.nearClipPlane = 0.03f;

            // --- On the grid (countdown): 3rd person over the field towards the start gate.
            var t = player.transform;
            Render(cam, t.TransformPoint(new Vector3(0.9f, 2.1f, -5.2f)), t.TransformPoint(new Vector3(0.9f, 0.7f, 4f)), 60f, "int_third_person_grid.png");

            // 1st person: own head/helmet hidden (shadows only).
            // (SledCameraDirector restores the head renderers every LateUpdate, so hide right before rendering.)
            yield return null;
            var fp = SledVisuals.FindDeep(t, "Camera_FP");
            SetFirstPerson(player, true);
            Render(cam, fp.position, fp.position + fp.forward, 75f, "int_first_person_grid.png", fp.up);
            SetFirstPerson(player, false);

            // Finish arch + canopy seen from the start gate.
            float d0 = track.StartLineDistance;
            Vector3 eye = track.GetPosition(d0 - 3f) + track.GetUp(d0) * 1.7f + track.GetRight(d0) * 1.2f;
            Render(cam, eye, track.GetPosition(d0 + 12f) + Vector3.up * 2.5f, 65f, "int_finish_arch.png");

            // --- Race: wait for GO + a few seconds, then chase / 1st person shots.
            while (rm.State != RaceState.Racing) yield return null;
            float go = Time.time;
            while (Time.time - go < 2.2f) yield return null;
            // Sleds passing under the finish arch (looking back from the canopy).
            Vector3 back = track.GetPosition(d0 + 22f) + Vector3.up * 2.2f + track.GetRight(d0 + 22f) * 1.3f;
            Render(cam, back, track.GetPosition(d0 + 2f) + Vector3.up * 0.8f, 60f, "int_finish_pass.png");

            Time.timeScale = 4f;
            while (Time.time - go < 26f) yield return null;
            Time.timeScale = 1f;
            yield return null;
            Render(cam, t.TransformPoint(new Vector3(0f, 2.0f, -4.8f)), t.TransformPoint(new Vector3(0f, 0.6f, 3f)), 62f, "int_third_person_race.png");
            yield return null;
            SetFirstPerson(player, true);
            Render(cam, fp.position, fp.position + fp.forward, 78f, "int_first_person_race.png", fp.up);
            SetFirstPerson(player, false);

            // --- Lift: side view when the player is half way up.
            Time.timeScale = 6f;
            float tl = Time.time;
            while (!(player.Controller.IsOnLift && track.SignedDelta(track.LiftStart, player.Follower.Distance) > 25f) && Time.time - tl < 200f) yield return null;
            Time.timeScale = 1f;
            yield return null;
            float dl = player.Follower.Distance;
            Vector3 side = track.GetPosition(dl) + track.GetRight(dl) * 6.5f + Vector3.up * 3f - track.GetTangent(dl) * 4f;
            Render(cam, side, t.position + Vector3.up * 0.6f, 55f, "int_lift.png");
            Vector3 stationEye = track.GetPosition(track.LiftStart - 8f) + Vector3.up * 3.5f + track.GetRight(track.LiftStart) * 5f;
            Render(cam, stationEye, track.GetPosition(track.LiftStart + 10f), 60f, "int_lift_station.png");

            // --- Lineup: 4 sled types x 4 colours on the open ground, frozen.
            var catalog = Object.FindFirstObjectByType<RaceBootstrap>().Catalog;
            Vector3 origin = new Vector3(-260f, 0f, -330f);
            var types = new[] { SledType.Classic, SledType.Sport, SledType.Tandem, SledType.Mini };
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    var prefab = catalog.GetPrefab(types[row]);
                    var go2 = Object.Instantiate(prefab, origin + new Vector3(col * 1.6f, 0.16f, row * 3.4f), Quaternion.Euler(0f, 30f, 0f));
                    go2.GetComponent<SledController>().enabled = false;
                    go2.GetComponent<Rigidbody>().isKinematic = true;
                    foreach (var mb in go2.GetComponents<MonoBehaviour>())
                        if (mb is RaceParticipant || mb is SledRecovery || mb is TrackFollower) mb.enabled = false;
                    go2.GetComponent<RaceParticipant>().GridSlot = row * 4 + col;
                    go2.GetComponent<SledVisuals>().ApplyColors(catalog.GetBodyColor((SledColor)col), catalog.GetHelmetColor(col + row * 4));
                }
            }
            for (int i = 0; i < 5; i++) yield return null;
            Vector3 c = origin + new Vector3(2.4f, 0.4f, 5.1f);
            Render(cam, c + new Vector3(7.5f, 6.5f, -9.5f), c, 30f, "int_lineup.png");
            Render(cam, origin + new Vector3(-2.5f, 1.2f, 0.8f), origin + new Vector3(1.6f, 0.45f, 0.2f), 40f, "int_lineup_closeup.png");

#if UNITY_EDITOR
            // Environment prefab spot check: water shader, tree trunk, rock.
            Vector3 w = new Vector3(-120f, 0f, -400f);
            foreach (var (path, offset) in new[]
            {
                ("Prefabs/Environment/Alpine/Env_Alpine_Waterfall_01.prefab", Vector3.zero),
                ("Prefabs/Environment/Alpine/Env_Alpine_Stream_01.prefab", new Vector3(0f, 0.3f, -14f)),
                ("Prefabs/Environment/Alpine/Env_Alpine_Cliff_02.prefab", Vector3.zero),
                ("Prefabs/Environment/Forest/Env_Forest_TallTree_01.prefab", new Vector3(-9f, 0f, -8f)),
                ("Prefabs/Environment/Common/Env_Common_RockLarge_01.prefab", new Vector3(7f, 0f, -9f)),
            })
            {
                var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LugeRush/" + path);
                Assert.IsNotNull(pf, path);
                // Water/cliff fronts face model +Z: turn them towards the camera.
                Object.Instantiate(pf, w + offset, Quaternion.Euler(0f, 180f, 0f));
            }
            yield return null;
            Render(cam, w + new Vector3(2f, 7f, -26f), w + new Vector3(0f, 3f, 0f), 50f, "int_water.png");
#endif
            Object.Destroy(cam.gameObject);
            Debug.Log($"[Integration] renders written to {OutDir}");
        }

        private static void SetFirstPerson(RaceParticipant p, bool on)
        {
            foreach (var a in p.GetComponentsInChildren<RiderAppearance>())
                if (a.transform.parent != null && a.transform.parent.name == "Seat") a.SetFirstPerson(on);
        }

        private static void Render(Camera cam, Vector3 pos, Vector3 look, float fov, string file, Vector3? up = null)
        {
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, up ?? Vector3.up));
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
            File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }

        [SetUp]
        public void SetUp()
        {
            // Measurement test: unrelated runtime errors (e.g. HUD/UI work in progress) must not abort the run.
            LogAssert.ignoreFailingMessages = true;
            SceneManager.sceneLoaded += StripHud;
        }

        /// <summary>The HUD is not needed for these measurements; remove it before its Start runs.</summary>
        private static void StripHud(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null && mb.GetType().Namespace == "LugeRush.UI") Object.DestroyImmediate(mb);
        }

        [TearDown]
        public void TearDown()
        {
            SceneManager.sceneLoaded -= StripHud;
            Time.timeScale = 1f;
            RaceSession.Reset();
        }
    }
}
