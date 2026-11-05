using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LugeRush.Track
{
    /// <summary>
    /// Draws a <see cref="LevelPropData"/> with GPU instancing, one draw per mesh and spatial cell. Cells are
    /// culled by distance and camera frustum on the CPU, so thousands of plants cost a few hundred draw calls.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class LevelPropRenderer : MonoBehaviour
    {
        private const float CellSize = 60f;
        private const int MaxPerDraw = 500;
        private const float ShadowMargin = 25f;

        public LevelPropData Data;

        /// <summary>Camera used for culling when set (off-screen captures); otherwise Camera.main.</summary>
        public static Camera CullCameraOverride;
        /// <summary>Multiplies every draw distance (overview captures).</summary>
        public static float DistanceScale = 1f;

        private class Cell
        {
            public Bounds Bounds;
            public Matrix4x4[] Matrices;
            public int Count;
            public RenderParams Params;
            public Mesh Mesh;
            public float DrawDistance;
        }

        private readonly List<Cell> cells = new List<Cell>();
        private readonly Plane[] planes = new Plane[6];

        public int VisibleDraws { get; private set; }
        public long VisibleTriangles { get; private set; }
        public int VisibleInstances { get; private set; }

        private void Awake()
        {
            if (Data == null) return;
            foreach (var g in Data.Groups)
            {
                if (g.Mesh == null || g.Material == null) continue;
                var rp = new RenderParams(g.Material)
                {
                    shadowCastingMode = g.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer
                };
                var byCell = new Dictionary<Vector2Int, List<Matrix4x4>>();
                foreach (var i in g.Instances)
                {
                    var key = new Vector2Int(Mathf.FloorToInt(i.Position.x / CellSize), Mathf.FloorToInt(i.Position.z / CellSize));
                    if (!byCell.TryGetValue(key, out var list)) byCell[key] = list = new List<Matrix4x4>();
                    list.Add(Matrix4x4.TRS(i.Position, i.Rotation, Vector3.one * i.Scale));
                }
                Bounds meshBounds = g.Mesh.bounds;
                float radius = meshBounds.extents.magnitude * 1.5f;
                foreach (var list in byCell.Values)
                {
                    for (int start = 0; start < list.Count; start += MaxPerDraw)
                    {
                        int count = Mathf.Min(MaxPerDraw, list.Count - start);
                        var matrices = list.GetRange(start, count).ToArray();
                        var b = new Bounds(matrices[0].MultiplyPoint3x4(meshBounds.center), Vector3.zero);
                        foreach (var m in matrices)
                            b.Encapsulate(new Bounds(m.MultiplyPoint3x4(meshBounds.center), Vector3.one * (radius * 2f)));
                        var cellParams = rp;
                        cellParams.worldBounds = b;
                        cells.Add(new Cell { Bounds = b, Matrices = matrices, Count = count, Params = cellParams, Mesh = g.Mesh, DrawDistance = g.DrawDistance });
                    }
                }
            }
        }

        private void Update()
        {
            var cam = CullCameraOverride != null ? CullCameraOverride : Camera.main;
            Vector3 eye = Vector3.zero;
            if (cam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, planes);
                eye = cam.transform.position;
            }
            int draws = 0, instances = 0;
            long triangles = 0;
            foreach (var c in cells)
            {
                if (cam != null)
                {
                    float max = c.DrawDistance * DistanceScale;
                    if (c.Bounds.SqrDistance(eye) > max * max) continue;
                    var grown = c.Bounds;
                    grown.Expand(ShadowMargin * 2f);
                    if (!GeometryUtility.TestPlanesAABB(planes, grown)) continue;
                }
                Graphics.RenderMeshInstanced(in c.Params, c.Mesh, 0, c.Matrices, c.Count);
                draws++;
                instances += c.Count;
                triangles += (long)c.Mesh.GetIndexCount(0) / 3 * c.Count;
            }
            VisibleDraws = draws;
            VisibleInstances = instances;
            VisibleTriangles = triangles;
        }
    }
}
