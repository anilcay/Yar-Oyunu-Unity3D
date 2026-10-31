using UnityEngine;

namespace LugeRush.AI
{
    /// <summary>Bot skill profile (Easy / Normal / Hard).</summary>
    [CreateAssetMenu(menuName = "Luge Rush/AI Difficulty", fileName = "AIDifficulty")]
    public class AIDifficulty : ScriptableObject
    {
        public string DisplayName = "Normal";

        [Header("Racing line")]
        [Tooltip("Fraction of the ideal inside-line offset actually used.")]
        [Range(0f, 1f)] public float LineAccuracy = 0.75f;
        [Tooltip("Random lateral wander amplitude (metres).")]
        public float LineWander = 0.3f;

        [Header("Speed planning")]
        [Tooltip("Fraction of the theoretical corner speed the bot dares to carry.")]
        public float CornerSpeedFactor = 0.9f;
        [Tooltip("Fraction of the real braking deceleration assumed when planning brake points (lower = brakes earlier).")]
        public float BrakeEfficiency = 0.75f;
        [Tooltip("Air drag multiplier baseline (>1 = slower on straights).")]
        public float DragFactor = 1f;

        [Header("Look ahead")]
        public float LookAheadMin = 5f;
        public float LookAheadTime = 0.5f;
        public float SteerGain = 2.2f;

        [Header("Behaviour")]
        [Tooltip("Probability of drifting through a tight corner.")]
        [Range(0f, 1f)] public float DriftUsage = 0.3f;
        [Tooltip("Start push reaction delay after GO (seconds) and its random spread.")]
        public float PushReaction = 0.15f;
        public float PushReactionSpread = 0.1f;
        [Tooltip("Rubber band strength: max drag change when far behind / ahead of the reference sled.")]
        [Range(0f, 0.5f)] public float RubberBand = 0.12f;
    }
}
