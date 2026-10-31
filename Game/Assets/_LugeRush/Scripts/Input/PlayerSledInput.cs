using LugeRush.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LugeRush.Input
{
    /// <summary>
    /// Local player input from LugeRush.inputactions (map "Sled"). Button presses are latched in Update
    /// so FixedUpdate (which reads SledInput) never misses them.
    /// </summary>
    public class PlayerSledInput : MonoBehaviour, ISledInputSource
    {
        public InputActionAsset Actions;
        [SerializeField] private string mapName = "Sled";

        private InputActionMap map;
        private InputAction steer, brake, drift, push, cameraToggle, lookBack, reset, pause;
        private bool pushLatch, resetLatch, cameraLatch, pauseLatch;

        public bool LookBackHeld => lookBack != null && lookBack.IsPressed();

        private void Awake()
        {
            if (Actions == null) return;
            // Clone so several players (split screen later) never share action state.
            var asset = Instantiate(Actions);
            map = asset.FindActionMap(mapName, true);
            steer = map.FindAction("Steer", true);
            brake = map.FindAction("Brake", true);
            drift = map.FindAction("Drift", true);
            push = map.FindAction("Push", true);
            cameraToggle = map.FindAction("Camera", true);
            lookBack = map.FindAction("LookBack", true);
            reset = map.FindAction("Reset", true);
            pause = map.FindAction("Pause", true);
        }

        private void OnEnable() { map?.Enable(); }
        private void OnDisable() { map?.Disable(); }

        private void Update()
        {
            if (map == null) return;
            pushLatch |= push.WasPressedThisFrame();
            resetLatch |= reset.WasPressedThisFrame();
            cameraLatch |= cameraToggle.WasPressedThisFrame();
            pauseLatch |= pause.WasPressedThisFrame();
        }

        public SledInput ReadInput()
        {
            if (map == null) return SledInput.None;
            var input = new SledInput
            {
                Steer = Mathf.Clamp(steer.ReadValue<float>(), -1f, 1f),
                Brake = Mathf.Clamp01(brake.ReadValue<float>()),
                Drift = drift.IsPressed(),
                Push = pushLatch,
                Reset = resetLatch
            };
            pushLatch = false;
            resetLatch = false;
            return input;
        }

        public bool ConsumeCameraToggle()
        {
            bool v = cameraLatch;
            cameraLatch = false;
            return v;
        }

        public bool ConsumePause()
        {
            bool v = pauseLatch;
            pauseLatch = false;
            return v;
        }
    }
}
