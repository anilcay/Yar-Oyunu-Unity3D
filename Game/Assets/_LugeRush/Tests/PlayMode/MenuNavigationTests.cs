using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LugeRush.Network;
using LugeRush.Race;
using LugeRush.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LugeRush.Tests
{
    /// <summary>
    /// Menu navigation through the EventSystem. Keyboard and gamepad reach the UI through the same module actions,
    /// so the test checks the bindings of those actions, then walks every panel with move/submit/cancel events.
    /// </summary>
    public class MenuNavigationTests
    {
        private static readonly string[] Panels = { "main", "sled", "raceoptions", "multiplayer", "settings", "lobby" };

        private Gamepad pad;
        private Keyboard keys;
        private bool settingsChanged;
        private InputSettings.BackgroundBehavior behavior;
        private InputSettings.EditorInputBehaviorInPlayMode editorBehavior;

        [UnityTest, Timeout(120000)]
        public IEnumerator EveryPanel_FullyReachableWithoutPointer()
        {
            LogAssert.ignoreFailingMessages = true;
            SceneManager.LoadScene("Boot", LoadSceneMode.Single);
            float t0 = Time.realtimeSinceStartup;
            while (MainMenuController.Instance == null && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            var menu = MainMenuController.Instance;
            Assert.IsNotNull(menu, "Main menu did not load");
            yield return null;

            var es = EventSystem.current;
            Assert.IsNotNull(es);
            Assert.IsTrue(es.sendNavigationEvents, "EventSystem navigation events are off");
            var module = es.GetComponent<InputSystemUIInputModule>();
            Assert.IsNotNull(module);
            // Batch runs have no window focus: keyboards are disabled in the background by default.
            behavior = InputSystem.settings.backgroundBehavior;
            editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settingsChanged = true;
            pad = InputSystem.AddDevice<Gamepad>();
            keys = InputSystem.AddDevice<Keyboard>();
            yield return null;
            foreach (var (action, name) in new[] { (module.move.action, "move"), (module.submit.action, "submit"), (module.cancel.action, "cancel") })
            {
                Assert.IsNotNull(action, $"UI action {name} missing");
                Assert.IsTrue(action.controls.Any(c => c.device == pad), $"UI action {name} does not reach the gamepad");
                Assert.IsTrue(action.controls.Any(c => c.device == keys), $"UI action {name} does not reach the keyboard");
            }

            // Real device input through the module: gamepad d-pad, keyboard arrow, each moves the selection.
            menu.ShowRoot();
            yield return null;
            var firstItem = es.currentSelectedGameObject;
            yield return Press(() => InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadDown)),
                               () => InputSystem.QueueStateEvent(pad, new GamepadState()));
            var afterPad = es.currentSelectedGameObject;
            Assert.AreNotEqual(firstItem, afterPad, "Gamepad d-pad down did not move the selection");
            yield return new WaitForSecondsRealtime(0.6f); // the module's move repeat delay
            yield return Press(() => InputSystem.QueueStateEvent(keys, new KeyboardState(Key.UpArrow)),
                               () => InputSystem.QueueStateEvent(keys, new KeyboardState()));
            Assert.AreEqual(firstItem, es.currentSelectedGameObject, "Keyboard arrow up did not move the selection back");

            var problems = new List<string>();
            var svc = NetworkSessionService.Instance;
            foreach (string id in Panels)
            {
                if (id == "lobby")
                {
                    Assert.IsNotNull(svc);
                    Assert.IsTrue(svc.HostDirect(7797), "Could not host for the lobby panel");
                    float t1 = Time.realtimeSinceStartup;
                    while (NetworkLobby.Instance == null && Time.realtimeSinceStartup - t1 < 5f) yield return null;
                    yield return null;
                }
                menu.Show(id, false);
                yield return null;
                yield return null;
                CheckPanel(id, menu, es, problems);
            }
            svc.Leave();
            yield return new WaitForSecondsRealtime(0.5f);

            // Stepper: left/right through the same move event; Esc/B: back out of a sub panel.
            menu.ShowRoot();
            yield return null;
            menu.Show("raceoptions");
            yield return null;
            var stepper = es.currentSelectedGameObject != null ? es.currentSelectedGameObject.GetComponent<UIStepper>() : null;
            Assert.IsNotNull(stepper, "Race options must start on a stepper");
            Move(es, MoveDirection.Down);
            yield return null;
            stepper = es.currentSelectedGameObject.GetComponent<UIStepper>();
            Assert.IsNotNull(stepper, "Down from the map stepper must land on the laps stepper");
            int before = stepper.Index;
            Move(es, MoveDirection.Right);
            yield return null;
            Assert.AreNotEqual(before, stepper.Index, "Right did not change the stepper");
            Move(es, MoveDirection.Left);
            yield return null;
            Assert.AreEqual(before, stepper.Index, "Left did not restore the stepper");
            menu.Current.OnBack();
            yield return null;
            Assert.AreEqual("main", menu.Current.Id, "Back from race options");
            Assert.IsNotNull(es.currentSelectedGameObject, "Nothing selected after back");

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        private static void CheckPanel(string id, MainMenuController menu, EventSystem es, List<string> problems)
        {
            var first = es.currentSelectedGameObject;
            if (first == null || !first.activeInHierarchy) { problems.Add($"{id}: nothing selected on open"); return; }
            if (!first.transform.IsChildOf(menu.Current.Root)) problems.Add($"{id}: selection {first.name} is outside the panel");

            var all = menu.Current.Root.GetComponentsInChildren<Selectable>().Where(s => s.IsActive() && s.IsInteractable() && !(s is Scrollbar)).ToList();
            var reached = new HashSet<Selectable>();
            var queue = new Queue<Selectable>();
            var start = first.GetComponent<Selectable>();
            reached.Add(start);
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var s = queue.Dequeue();
                foreach (var next in new[] { s.FindSelectableOnUp(), s.FindSelectableOnDown(), s.FindSelectableOnLeft(), s.FindSelectableOnRight() })
                    if (next != null && next.IsActive() && next.IsInteractable() && reached.Add(next)) queue.Enqueue(next);
            }
            foreach (var s in all)
                if (!reached.Contains(s)) problems.Add($"{id}: {Describe(s)} cannot be reached from {first.name}");
            foreach (var s in reached)
                if (!s.transform.IsChildOf(menu.Current.Root)) problems.Add($"{id}: navigation leaks to {Describe(s)}");

            // Real move events must follow the same graph: Down from the first item changes the selection (if there is one).
            if (all.Count > 1)
            {
                Move(es, MoveDirection.Down);
                if (es.currentSelectedGameObject == null || (es.currentSelectedGameObject == first && first.GetComponent<Selectable>().FindSelectableOnDown() != null))
                    problems.Add($"{id}: Down event did not move the selection");
            }
            Debug.Log($"[MenuNav] {id}: {reached.Count}/{all.Count} reachable from {first.name}");
        }

        private static string Describe(Selectable s)
        {
            var label = s.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            return $"{s.GetType().Name} '{s.name}'{(label != null ? $" ({label.text})" : "")}";
        }

        private static void Move(EventSystem es, MoveDirection dir)
        {
            var data = new AxisEventData(es) { moveDir = dir, moveVector = dir == MoveDirection.Left ? Vector2.left : dir == MoveDirection.Right ? Vector2.right : dir == MoveDirection.Up ? Vector2.up : Vector2.down };
            ExecuteEvents.Execute(es.currentSelectedGameObject, data, ExecuteEvents.moveHandler);
        }

        private static IEnumerator Press(System.Action down, System.Action up)
        {
            down();
            InputSystem.Update();
            yield return null;
            yield return null;
            up();
            InputSystem.Update();
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (settingsChanged)
            {
                InputSystem.settings.backgroundBehavior = behavior;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorBehavior;
            }
            if (pad != null) InputSystem.RemoveDevice(pad);
            if (keys != null) InputSystem.RemoveDevice(keys);
            // Boot's persistent objects (NetworkManager, session service) must not leak into the race tests.
            var probe = new GameObject("Probe");
            Object.DontDestroyOnLoad(probe);
            foreach (var root in probe.scene.GetRootGameObjects())
                if (!root.name.Contains("tests runner") && !root.name.Contains("TestRunner") && !root.name.Contains("PlaymodeTestsController"))
                    Object.Destroy(root);
            RaceSession.Reset();
            RaceFlow.Handler = null;
        }
    }
}
