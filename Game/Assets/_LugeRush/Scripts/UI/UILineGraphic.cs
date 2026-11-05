using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LugeRush.UI
{
    /// <summary>Thick polyline in local rect coordinates (minimap track outline).</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILineGraphic : MaskableGraphic
    {
        public float Thickness = 6f;
        public bool Closed = true;
        private readonly List<Vector2> points = new List<Vector2>();

        public void SetPoints(IList<Vector2> pts)
        {
            points.Clear();
            points.AddRange(pts);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = points.Count;
            if (n < 2) return;
            int segments = Closed ? n : n - 1;
            float half = Thickness * 0.5f;
            var c = (Color32)color;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % n];
                Vector2 dir = b - a;
                if (dir.sqrMagnitude < 1e-6f) continue;
                Vector2 nrm = new Vector2(-dir.y, dir.x).normalized * half;
                // Extend slightly so joints overlap without gaps.
                Vector2 ext = dir.normalized * half * 0.6f;
                int start = vh.currentVertCount;
                vh.AddVert(a - ext - nrm, c, Vector2.zero);
                vh.AddVert(a - ext + nrm, c, Vector2.zero);
                vh.AddVert(b + ext + nrm, c, Vector2.zero);
                vh.AddVert(b + ext - nrm, c, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
