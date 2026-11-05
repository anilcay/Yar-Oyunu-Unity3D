using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Drives the purely visual parts of a sled: handlebar turn, wheel spin, ground offset and paint.
    /// Parts are found by name under <see cref="visualRoot"/> so any art model with the agreed names works.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SledVisuals : MonoBehaviour
    {
        public const string VisualRootName = "SledVisual";

        [SerializeField] private SledController controller;
        [SerializeField] private Transform visualRoot;
        [Tooltip("Handlebar yaw at full steer (matches the Steer_Left/Right clips, ANIMATION.md).")]
        [SerializeField] private float handlebarMaxAngle = 22f;
        [Tooltip("Handlebar yaw at full steer while drifting (Drift_Left/Right clips).")]
        [SerializeField] private float handlebarDriftAngle = 30f;
        [Tooltip("Handlebar pulled towards the chest at full brake (sled space, matches the Brake clip).")]
        [SerializeField] private Vector3 handlebarBrakeOffset = new Vector3(0f, 0.07f, -0.13f);
        [SerializeField] private float frontWheelSteerAngle = 12f;
        [SerializeField] private float wheelRadius = 0.05f;
        [SerializeField] private float steerSmoothing = 12f;
        [Tooltip("Offset the visual root down by the suspension hover height so the model sits on the road.")]
        [SerializeField] private bool snapVisualToGround = true;

        private Transform handlebar;
        private Transform[] wheels = new Transform[4]; // FL, FR, RL, RR
        private Quaternion handlebarRest;
        private Vector3 handlebarRestPos;
        private float brakeVisual;
        private float driftBlend;
        private Quaternion[] wheelRest = new Quaternion[4];
        private float steerVisual;
        private float wheelAngle;
        private MaterialPropertyBlock block;

        private static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public Transform VisualRoot => visualRoot;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<SledController>();
            if (visualRoot == null) visualRoot = transform.Find(VisualRootName);
            Rebind();
        }

        private void Start() { SnapToGround(); }

        private void SnapToGround()
        {
            if (snapVisualToGround && visualRoot != null && controller != null)
                visualRoot.localPosition = new Vector3(visualRoot.localPosition.x, -controller.RestHoverHeight, visualRoot.localPosition.z);
        }

        /// <summary>Use a new visual hierarchy (SledVariant swapped the model).</summary>
        public void SetVisualRoot(Transform root)
        {
            visualRoot = root;
            if (controller == null) controller = GetComponent<SledController>();
            Rebind();
            SnapToGround();
        }

        /// <summary>Re-scans the visual hierarchy (call after swapping the art model).</summary>
        public void Rebind()
        {
            if (visualRoot == null) return;
            handlebar = FindDeep(visualRoot, "Handlebar");
            if (handlebar != null)
            {
                handlebarRest = handlebar.localRotation;
                handlebarRestPos = handlebar.localPosition;
            }
            for (int i = 0; i < 4; i++)
            {
                wheels[i] = FindDeep(visualRoot, WheelNames[i]);
                if (wheels[i] != null) wheelRest[i] = wheels[i].localRotation;
            }
        }

        private void LateUpdate()
        {
            if (controller == null) return;
            bool idle = controller.ControlsLocked || controller.IsOnLift;
            float targetSteer = idle ? 0f : controller.LastInput.Steer;
            float k = 1f - Mathf.Exp(-steerSmoothing * Time.deltaTime);
            steerVisual = Mathf.Lerp(steerVisual, targetSteer, k);
            brakeVisual = Mathf.Lerp(brakeVisual, idle || controller.IsDrifting ? 0f : controller.LastInput.Brake, k);
            driftBlend = Mathf.Lerp(driftBlend, controller.IsDrifting ? 1f : 0f, k);

            if (handlebar != null)
            {
                float angle = steerVisual * Mathf.Lerp(handlebarMaxAngle, handlebarDriftAngle, driftBlend);
                handlebar.localRotation = handlebarRest * Quaternion.Euler(0f, angle, 0f);
                handlebar.localPosition = handlebarRestPos + handlebarBrakeOffset * brakeVisual;
            }

            wheelAngle += controller.Speed * Time.deltaTime / Mathf.Max(0.01f, wheelRadius) * Mathf.Rad2Deg;
            wheelAngle %= 360f;
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null) continue;
                float steer = i < 2 ? steerVisual * frontWheelSteerAngle : 0f;
                wheels[i].localRotation = Quaternion.Euler(0f, steer, 0f) * wheelRest[i] * Quaternion.Euler(wheelAngle, 0f, 0f);
            }
        }

        /// <summary>
        /// Sled body colour (SledPaint) and rider helmet colour (nearest helmet palette cell, RiderAppearance).
        /// Riders also get a deterministic body/head/skin variant from the grid slot so bots look different.
        /// Falls back to tinting "Body*"/"Helmet*" renderers for primitive placeholder sleds.
        /// </summary>
        public void ApplyColors(Color body, Color helmet)
        {
            if (visualRoot == null) visualRoot = transform.Find(VisualRootName);
            if (visualRoot == null) return;

            var riders = visualRoot.GetComponentsInChildren<RiderAppearance>(true);
            var paint = GetComponent<SledPaint>();
            if (paint != null || riders.Length > 0)
            {
                if (paint != null) paint.SetColor(body);
                var participant = GetComponent<LugeRush.Race.RaceParticipant>();
                int slot = participant != null ? participant.GridSlot : transform.GetSiblingIndex();
                for (int i = 0; i < riders.Length; i++)
                {
                    if (riders[i].VaryByGridSlot) riders[i].ApplySeed(slot + 31 * i);
                    riders[i].SetHelmetColor(helmet, HelmetPalette);
                    if (i > 0) riders[i].SetHelmet(riders[i].Helmet + 3); // passenger: different helmet
                }
                return;
            }

            block ??= new MaterialPropertyBlock();
            foreach (var r in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                bool isBody = n.StartsWith("Body");
                bool isHelmet = n.StartsWith("Helmet");
                if (!isBody && !isHelmet) continue;
                r.GetPropertyBlock(block);
                Color c = isBody ? body : helmet;
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Helmet palette (row 5) in RiderAppearance index order.</summary>
        public static readonly Color[] HelmetPalette =
        {
            new Color(0.949f, 0.761f, 0.188f), new Color(0.176f, 0.435f, 0.820f), new Color(0.369f, 0.761f, 0.290f),
            new Color(0.137f, 0.141f, 0.153f), new Color(0.941f, 0.941f, 0.941f), new Color(0.910f, 0.416f, 0.627f),
            new Color(0.851f, 0.227f, 0.227f), new Color(0.482f, 0.310f, 0.788f),
        };

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }
    }
}
