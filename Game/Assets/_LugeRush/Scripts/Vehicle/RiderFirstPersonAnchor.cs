using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Drives the sled's "Camera_FP" anchor (used by the 1st person camera) from the driver's Head bone:
    /// position = head + eye offset, rotation = sled heading plus only a smoothed fraction (~25 %) of the
    /// head's animated rotation (ARCHITECTURE decision, avoids motion sickness).
    /// </summary>
    [DefaultExecutionOrder(-100)] // LateUpdate before the Cinemachine brain
    public class RiderFirstPersonAnchor : MonoBehaviour
    {
        public const string AnchorName = "Camera_FP";
        public const string HeadBoneName = "Head";
        public const string SeatName = "Seat";

        [Tooltip("Eye point relative to the Head joint in sled space (ANIMATION.md: 0.115 up, 0.085 forward).")]
        public Vector3 EyeOffset = new Vector3(0f, 0.115f, 0.085f);
        [Range(0f, 1f)] public float HeadRotationInfluence = 0.25f;
        public float RotationSmoothing = 8f;
        [Tooltip("Extra downward pitch (degrees) so the bar, hands and sled nose are in view.")]
        public float Pitch = 14f;
        [Tooltip("Head rotation relative to the sled in the Sit_Idle reference pose (set by the prefab factory).")]
        public Quaternion HeadRestLocal = Quaternion.identity;

        private Transform anchor;
        private Transform head;
        private Quaternion smoothedDelta = Quaternion.identity;

        public Transform Head => head;

        private void Awake() { Rebind(); }

        public void Rebind()
        {
            anchor = transform.Find(AnchorName);
            if (anchor == null)
            {
                anchor = new GameObject(AnchorName).transform;
                anchor.SetParent(transform, false);
                anchor.SetSiblingIndex(0);
            }
            head = null;
            var seat = SledVisuals.FindDeep(transform, SeatName);
            if (seat != null) head = SledVisuals.FindDeep(seat, HeadBoneName);
            smoothedDelta = Quaternion.identity;
        }

        private void LateUpdate()
        {
            if (anchor == null || head == null) return;
            Quaternion sled = transform.rotation;
            Quaternion headLocal = Quaternion.Inverse(sled) * head.rotation;
            Quaternion delta = headLocal * Quaternion.Inverse(HeadRestLocal);
            Quaternion wanted = Quaternion.Slerp(Quaternion.identity, delta, HeadRotationInfluence);
            smoothedDelta = Quaternion.Slerp(smoothedDelta, wanted, 1f - Mathf.Exp(-RotationSmoothing * Time.deltaTime));

            anchor.position = head.position + sled * EyeOffset;
            anchor.rotation = sled * smoothedDelta * Quaternion.Euler(Pitch, 0f, 0f);
        }

        /// <summary>Immediate update (edit mode renders, tests).</summary>
        public void Snap()
        {
            smoothedDelta = Quaternion.identity;
            if (anchor == null || head == null) Rebind();
            if (anchor == null || head == null) return;
            anchor.position = head.position + transform.rotation * EyeOffset;
            anchor.rotation = transform.rotation * Quaternion.Euler(Pitch, 0f, 0f);
        }
    }
}
