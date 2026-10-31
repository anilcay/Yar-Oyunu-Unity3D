using System;
using System.Collections.Generic;
using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>
    /// Data-only description of a track (closed loop). Level designers author one per map;
    /// the editor TrackSceneBuilder turns it into spline + meshes + lift + grid.
    /// </summary>
    [CreateAssetMenu(menuName = "Luge Rush/Track Definition", fileName = "TrackDefinition")]
    public class TrackDefinition : ScriptableObject
    {
        [Serializable]
        public struct Knot
        {
            [Tooltip("World position (metres). Y is height.")]
            public Vector3 Position;
            [Tooltip("Banking in degrees. Positive = right edge lower (banked for a right turn).")]
            public float BankDeg;
            [Tooltip("Road width in metres (curb to curb). <= 0 uses DefaultWidth.")]
            public float Width;

            public Knot(Vector3 position, float bankDeg = 0f, float width = 0f)
            {
                Position = position;
                BankDeg = bankDeg;
                Width = width;
            }
        }

        [Header("Identity")]
        public string TrackId = "dev_greybox";
        public string DisplayName = "Geliştirme Pisti";

        [Header("Shape (closed loop, driving direction = knot order)")]
        public List<Knot> Knots = new List<Knot>();
        public float DefaultWidth = 4f;
        [Tooltip("Bezier handle length as a fraction of the shorter neighbouring knot distance.")]
        [Range(0.1f, 0.5f)] public float HandleScale = 0.35f;

        [Header("Lift")]
        [Tooltip("Knot index where the cable lift grabs the sleds (bottom of the descent).")]
        public int LiftStartKnot;
        [Tooltip("Knot index where the lift releases the sleds (top).")]
        public int LiftEndKnot;
        public float LiftSpeed = 18f;
        public float LiftExitSpeed = 9f;

        [Header("Race")]
        [Tooltip("Distance from the lift end to the start/finish line (start grid sits in between).")]
        public float StartLineOffset = 40f;
        public float CheckpointSpacing = 50f;

        [Header("Mesh")]
        public float CurbWidth = 0.35f;
        public float CurbHeight = 0.12f;
        public float BarrierHeight = 1.2f;
        public bool BuildSkirt = true;
        public float SkirtGroundY = 0f;
    }
}
