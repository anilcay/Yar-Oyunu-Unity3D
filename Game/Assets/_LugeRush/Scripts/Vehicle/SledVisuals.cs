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
        [SerializeField] private float handlebarMaxAngle = 25f;
        [SerializeField] private float frontWheelSteerAngle = 12f;
        [SerializeField] private float wheelRadius = 0.07f;
        [SerializeField] private float steerSmoothing = 12f;
        [Tooltip("Offset the visual root down by the suspension hover height so the model sits on the road.")]
        [SerializeField] private bool snapVisualToGround = true;

        private Transform handlebar;
        private Transform[] wheels = new Transform[4]; // FL, FR, RL, RR
        private Quaternion handlebarRest;
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

        private void Start()
        {
            if (snapVisualToGround && visualRoot != null && controller != null)
                visualRoot.localPosition = new Vector3(visualRoot.localPosition.x, -controller.RestHoverHeight, visualRoot.localPosition.z);
        }

        /// <summary>Re-scans the visual hierarchy (call after swapping the art model).</summary>
        public void Rebind()
        {
            if (visualRoot == null) return;
            handlebar = FindDeep(visualRoot, "Handlebar");
            if (handlebar != null) handlebarRest = handlebar.localRotation;
            for (int i = 0; i < 4; i++)
            {
                wheels[i] = FindDeep(visualRoot, WheelNames[i]);
                if (wheels[i] != null) wheelRest[i] = wheels[i].localRotation;
            }
        }

        private void LateUpdate()
        {
            if (controller == null) return;
            float targetSteer = controller.ControlsLocked || controller.IsOnLift ? 0f : controller.LastInput.Steer;
            steerVisual = Mathf.Lerp(steerVisual, targetSteer, 1f - Mathf.Exp(-steerSmoothing * Time.deltaTime));

            if (handlebar != null)
                handlebar.localRotation = handlebarRest * Quaternion.Euler(0f, steerVisual * handlebarMaxAngle, 0f);

            wheelAngle += controller.Speed * Time.deltaTime / Mathf.Max(0.01f, wheelRadius) * Mathf.Rad2Deg;
            wheelAngle %= 360f;
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null) continue;
                float steer = i < 2 ? steerVisual * frontWheelSteerAngle : 0f;
                wheels[i].localRotation = Quaternion.Euler(0f, steer, 0f) * wheelRest[i] * Quaternion.Euler(wheelAngle, 0f, 0f);
            }
        }

        /// <summary>Tints renderers whose object names start with "Body" (sled colour) and "Helmet" (rider).</summary>
        public void ApplyColors(Color body, Color helmet)
        {
            if (visualRoot == null) visualRoot = transform.Find(VisualRootName);
            if (visualRoot == null) return;
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
