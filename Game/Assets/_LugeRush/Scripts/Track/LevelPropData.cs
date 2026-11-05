using System;
using System.Collections.Generic;
using UnityEngine;

namespace LugeRush.Track
{
    /// <summary>Scattered vegetation and rocks of one map: compact placements, grouped by mesh (no GameObjects).</summary>
    public class LevelPropData : ScriptableObject
    {
        [Serializable]
        public struct Instance
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public float Scale;
        }

        [Serializable]
        public class Group
        {
            public string Name;
            public Mesh Mesh;
            public Material Material;
            public bool CastShadows = true;
            [Tooltip("Instances further from the camera than this are not drawn.")]
            public float DrawDistance = 300f;
            public List<Instance> Instances = new List<Instance>();
        }

        public List<Group> Groups = new List<Group>();

        public int InstanceCount
        {
            get
            {
                int n = 0;
                foreach (var g in Groups) n += g.Instances.Count;
                return n;
            }
        }
    }
}
