using System.Linq;
using LugeRush.Vehicle;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Builds Art/Animations/AC_Rider.controller (ARCHITECTURE animation contract) and the upper-body mask.
    /// Base: Ride (1D Steer: -1 Steer_Left, 0 Sit_Idle, +1 Steer_Right), Drift (1D Steer), Lift, Push, Win, Lose
    /// (hand IK on, states tagged NoIK release the hands). Brake: override layer (upper body) whose weight
    /// SledAnimatorBridge drives from the brake input. Hit: additive Bump.
    /// </summary>
    public static class ArtAnimatorFactory
    {
        public const string Folder = EditorAssetUtil.Root + "/Art/Animations";
        public const string ControllerPath = Folder + "/AC_Rider.controller";
        public const string UpperBodyMaskPath = Folder + "/AM_Rider_UpperBody.mask";

        public static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(ArtImportPostprocessor.RiderPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == name);

        [MenuItem("Luge Rush/Art/Build Rider Animator")]
        public static AnimatorController Build()
        {
            EditorAssetUtil.EnsureFolder(Folder);
            var mask = BuildMask();

            // Recreate from scratch each time (deterministic). Keep the asset GUID by clearing in place.
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub != ctrl && !(sub is AnimatorController)) Object.DestroyImmediate(sub, true);
            ctrl.layers = new AnimatorControllerLayer[0];
            ctrl.parameters = new AnimatorControllerParameter[0];

            ctrl.AddParameter("Steer", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Brake", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Speed01", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Drift", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("OnLift", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Push", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Bump", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Win", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Lose", AnimatorControllerParameterType.Trigger);

            BuildBase(ctrl);
            BuildBrake(ctrl, mask);
            BuildHit(ctrl);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ArtAnimatorFactory] Built {ControllerPath} ({ctrl.layers.Length} layers).");
            return ctrl;
        }

        private static AvatarMask BuildMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                          || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                          || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
                mask.SetHumanoidBodyPartActive(part, on);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static BlendTree SteerTree(AnimatorController ctrl, string name, string left, string center, string right)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.Simple1D, blendParameter = "Steer", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, ctrl);
            tree.hideFlags = HideFlags.HideInHierarchy;
            tree.AddChild(Clip(left), -1f);
            if (center != null) tree.AddChild(Clip(center), 0f);
            tree.AddChild(Clip(right), 1f);
            return tree;
        }

        private static void BuildBase(AnimatorController ctrl)
        {
            ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = "Base Layer",
                defaultWeight = 1f,
                iKPass = true,
                stateMachine = NewMachine(ctrl, "Base Layer")
            });
            var sm = ctrl.layers[0].stateMachine;

            // Steer > 0 = right (ARCHITECTURE decision): -1 Steer_Left, 0 Sit_Idle, +1 Steer_Right.
            var ride = sm.AddState("Ride", new Vector3(300, 0));
            ride.motion = SteerTree(ctrl, "RideSteer", "Steer_Left", "Sit_Idle", "Steer_Right");
            sm.defaultState = ride;

            var drift = sm.AddState("Drift", new Vector3(300, 120));
            drift.motion = SteerTree(ctrl, "DriftSteer", "Drift_Left", null, "Drift_Right");

            var lift = sm.AddState("Lift", new Vector3(600, 0));
            lift.motion = Clip("Lift_Relax");
            lift.tag = RiderIK.NoIKTag;

            var push = sm.AddState("Push", new Vector3(0, 220));
            push.motion = Clip("Push_Start");
            push.tag = RiderIK.NoIKTag;

            var win = sm.AddState("Win", new Vector3(300, 300));
            win.motion = Clip("Win_Cheer");
            win.tag = RiderIK.NoIKTag;

            var lose = sm.AddState("Lose", new Vector3(600, 300));
            lose.motion = Clip("Lose_Shrug");
            lose.tag = RiderIK.NoIKTag;

            Transition(ride, drift, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Drift");
            Transition(drift, ride, 0.25f).AddCondition(AnimatorConditionMode.IfNot, 0, "Drift");
            Transition(ride, lift, 0.5f).AddCondition(AnimatorConditionMode.If, 0, "OnLift");
            Transition(drift, lift, 0.5f).AddCondition(AnimatorConditionMode.If, 0, "OnLift");
            Transition(lift, ride, 0.4f).AddCondition(AnimatorConditionMode.IfNot, 0, "OnLift");

            var toPush = sm.AddAnyStateTransition(push);
            toPush.duration = 0.1f;
            toPush.canTransitionToSelf = false;
            toPush.AddCondition(AnimatorConditionMode.If, 0, "Push");
            var back = push.AddTransition(ride);
            back.hasExitTime = true;
            back.exitTime = 0.85f;
            back.duration = 0.15f;

            foreach (var (state, trigger) in new[] { (win, "Win"), (lose, "Lose") })
            {
                var t = sm.AddAnyStateTransition(state);
                t.duration = 0.3f;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            }
        }

        private static void BuildBrake(AnimatorController ctrl, AvatarMask mask)
        {
            var sm = NewMachine(ctrl, "Brake");
            var brake = sm.AddState("Brake", new Vector3(300, 0));
            brake.motion = Clip("Brake");
            sm.defaultState = brake;
            ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = SledAnimatorBridgeBrakeLayer,
                defaultWeight = 0f, // driven by SledAnimatorBridge
                blendingMode = AnimatorLayerBlendingMode.Override,
                avatarMask = mask,
                stateMachine = sm
            });
        }

        private const string SledAnimatorBridgeBrakeLayer = SledAnimatorBridge.BrakeLayerName;

        private static void BuildHit(AnimatorController ctrl)
        {
            var sm = NewMachine(ctrl, "Hit");
            var empty = sm.AddState("Empty", new Vector3(300, 0));
            sm.defaultState = empty;
            var bump = sm.AddState("Bump", new Vector3(300, 120));
            bump.motion = Clip("Bump");
            var toBump = sm.AddAnyStateTransition(bump);
            toBump.duration = 0.05f;
            toBump.canTransitionToSelf = true;
            toBump.AddCondition(AnimatorConditionMode.If, 0, "Bump");
            var back = bump.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.95f;
            back.duration = 0.05f;
            ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = "Hit",
                defaultWeight = 1f,
                blendingMode = AnimatorLayerBlendingMode.Additive,
                stateMachine = sm
            });
        }

        private static AnimatorStateMachine NewMachine(AnimatorController ctrl, string name)
        {
            var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, ctrl);
            return sm;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }
    }
}
