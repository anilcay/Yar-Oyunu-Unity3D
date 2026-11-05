using System;
using System.Collections.Generic;
using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>Named camera spots of a map, used by automated captures and for level reviews.</summary>
    public class LevelViewpoints : MonoBehaviour
    {
        [Serializable]
        public struct Viewpoint
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Target;
            public float Fov;
        }

        public List<Viewpoint> Points = new List<Viewpoint>();
    }
}
