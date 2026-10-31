using System.Collections.Generic;
using LugeRush.Input;
using LugeRush.Race;
using LugeRush.Vehicle;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.CameraSystem
{
    /// <summary>
    /// Chooses between the Cinemachine cameras following the local sled: 3rd person (default), 1st person (C),
    /// look back (Q, held), lift side shot and finish orbit. Also drives speed based FOV/offset/shake.
    /// </summary>
    public class SledCameraDirector : MonoBehaviour
    {
        public const string FirstPersonAnchorName = "Camera_FP";
        public const string FollowTargetName = "CameraTarget";
        public const string HeadBoneName = "Head";

        [Header("Cameras")]
        public CinemachineCamera ThirdPerson;
        public CinemachineCamera FirstPerson;
        public CinemachineCamera LookBack;
        public CinemachineCamera LiftCam;
        public CinemachineCamera FinishOrbit;

        [Header("Target")]
        public SledController Target;
        public PlayerSledInput PlayerInput;
        [Tooltip("Fallback 1st person eye position (sled local) when no Camera_FP / Head exists.")]
        public Vector3 FirstPersonFallbackOffset = new Vector3(0f, 0.95f, -0.2f);

        [Header("3rd person dynamics")]
        public float ThirdFovSlow = 58f;
        public float ThirdFovFast = 74f;
        public Vector3 ThirdOffsetSlow = new Vector3(0f, 2.0f, -4.8f);
        public Vector3 ThirdOffsetFast = new Vector3(0f, 2.4f, -6.2f);
        public float ThirdShakeMax = 0.35f;

        [Header("1st person dynamics")]
        public float FirstFovSlow = 68f;
        public float FirstFovFast = 86f;
        public float FirstShakeMax = 0.5f;

        [Header("Finish orbit")]
        public float OrbitDegreesPerSecond = 18f;

        public bool IsFirstPerson { get; private set; }

        private RaceParticipant participant;
        private CinemachineFollow thirdFollow;
        private CinemachineBasicMultiChannelPerlin thirdNoise, firstNoise;
        private CinemachineOrbitalFollow orbit;
        private CinemachineCamera active;
        private readonly List<Renderer> headRenderers = new List<Renderer>();
        private const int ActivePriority = 20;

        private void Start()
        {
            if (Target == null)
            {
                foreach (var p in FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None))
                    if (p.IsLocalPlayer) { Target = p.GetComponent<SledController>(); break; }
            }
            if (Target != null) Bind(Target);
        }

        /// <summary>Points every camera at <paramref name="sled"/>.</summary>
        public void Bind(SledController sled)
        {
            Target = sled;
            participant = sled.GetComponent<RaceParticipant>();
            if (PlayerInput == null) PlayerInput = sled.GetComponent<PlayerSledInput>();

            Transform follow = SledVisuals.FindDeep(sled.transform, FollowTargetName) ?? sled.transform;
            Transform fp = SledVisuals.FindDeep(sled.transform, FirstPersonAnchorName) ?? SledVisuals.FindDeep(sled.transform, HeadBoneName);
            if (fp == null)
            {
                fp = new GameObject(FirstPersonAnchorName).transform;
                fp.SetParent(sled.transform, false);
                fp.localPosition = FirstPersonFallbackOffset;
            }

            SetTargets(ThirdPerson, follow, follow);
            SetTargets(FirstPerson, fp, null);
            SetTargets(LookBack, follow, follow);
            SetTargets(LiftCam, follow, follow);
            SetTargets(FinishOrbit, follow, follow);

            thirdFollow = ThirdPerson != null ? ThirdPerson.GetComponent<CinemachineFollow>() : null;
            thirdNoise = ThirdPerson != null ? ThirdPerson.GetComponent<CinemachineBasicMultiChannelPerlin>() : null;
            firstNoise = FirstPerson != null ? FirstPerson.GetComponent<CinemachineBasicMultiChannelPerlin>() : null;
            orbit = FinishOrbit != null ? FinishOrbit.GetComponent<CinemachineOrbitalFollow>() : null;

            headRenderers.Clear();
            foreach (var r in sled.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.name.StartsWith("Head") || r.gameObject.name.StartsWith("Helmet")) headRenderers.Add(r);

            Activate(ThirdPerson);
        }

        private static void SetTargets(CinemachineCamera cam, Transform follow, Transform lookAt)
        {
            if (cam == null) return;
            cam.Follow = follow;
            cam.LookAt = lookAt;
        }

        private void LateUpdate()
        {
            if (Target == null) return;
            if (PlayerInput != null && PlayerInput.ConsumeCameraToggle()) IsFirstPerson = !IsFirstPerson;
            bool lookBack = PlayerInput != null && PlayerInput.LookBackHeld;

            bool finished = participant != null && participant.Finished;
            CinemachineCamera want;
            if (finished && FinishOrbit != null) want = FinishOrbit;
            else if (Target.IsOnLift && LiftCam != null) want = LiftCam;
            else if (lookBack && LookBack != null) want = LookBack;
            else want = IsFirstPerson && FirstPerson != null ? FirstPerson : ThirdPerson;
            if (want != active) Activate(want);

            float s = Target.Speed01;
            float s2 = s * s;
            if (ThirdPerson != null)
            {
                ThirdPerson.Lens.FieldOfView = Mathf.Lerp(ThirdFovSlow, ThirdFovFast, s2);
                if (thirdFollow != null) thirdFollow.FollowOffset = Vector3.Lerp(ThirdOffsetSlow, ThirdOffsetFast, s);
                if (thirdNoise != null) thirdNoise.AmplitudeGain = ThirdShakeMax * s2 * (Target.IsGrounded ? 1f : 0.3f);
            }
            if (FirstPerson != null)
            {
                FirstPerson.Lens.FieldOfView = Mathf.Lerp(FirstFovSlow, FirstFovFast, s2);
                if (firstNoise != null) firstNoise.AmplitudeGain = FirstShakeMax * s2 * (Target.IsGrounded ? 1f : 0.3f);
            }
            if (orbit != null && active == FinishOrbit)
                orbit.HorizontalAxis.Value = Mathf.Repeat(orbit.HorizontalAxis.Value + OrbitDegreesPerSecond * Time.unscaledDeltaTime, 360f);

            // Hide own head/helmet in 1st person (shadows stay).
            var mode = active == FirstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in headRenderers) if (r != null) r.shadowCastingMode = mode;
        }

        private void Activate(CinemachineCamera cam)
        {
            active = cam;
            foreach (var c in new[] { ThirdPerson, FirstPerson, LookBack, LiftCam, FinishOrbit })
                if (c != null) c.Priority = c == cam ? ActivePriority : 0;
        }
    }
}
