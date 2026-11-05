using LugeRush.Vehicle;
using UnityEngine;

namespace LugeRush.UI
{
    /// <summary>
    /// Menu backdrop: low-poly hillside with a turntable showing the selected sled (the "SledVisual"
    /// hierarchy of the type prefab in SledCatalog, or the default prefab when it is missing), slowly rotating.
    /// </summary>
    public class SledPreviewStage : MonoBehaviour
    {
        public Camera StageCamera;
        public float RotateSpeed = 22f;
        [Tooltip("Where the sled sits on screen: the camera aims this far left of the turntable.")]
        public float AimOffset = 1.25f;

        private Transform turntable;
        private GameObject current;
        private SledType shownType = (SledType)(-1);
        private SledColor shownColor = (SledColor)(-1);
        private MaterialPropertyBlock block;
        private Shader lit;
        private bool built;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public bool SledVisible
        {
            get => turntable != null && turntable.gameObject.activeSelf;
            set { if (turntable != null) turntable.gameObject.SetActive(value); }
        }

        private void Awake() { Build(); }

        public void Build()
        {
            if (built) return;
            built = true;
            lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (StageCamera == null) StageCamera = Camera.main;
            if (StageCamera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                StageCamera = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            StageCamera.fieldOfView = 38f;
            StageCamera.nearClipPlane = 0.1f;
            StageCamera.farClipPlane = 600f;
            var camPos = new Vector3(0.2f, 1.25f, -3.9f);
            StageCamera.transform.position = transform.position + camPos;
            StageCamera.transform.rotation = Quaternion.LookRotation(transform.position + new Vector3(-AimOffset, 0.45f, 0f) - StageCamera.transform.position);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 260f;
            RenderSettings.fogColor = new Color(0.70f, 0.80f, 0.86f);

            var decor = new GameObject("Decor").transform;
            decor.SetParent(transform, false);
            var grass = Mat(new Color(0.30f, 0.52f, 0.24f), 0.05f);
            var grassDark = Mat(new Color(0.20f, 0.40f, 0.20f), 0.05f);
            var pine = Mat(new Color(0.13f, 0.36f, 0.22f), 0.1f);
            var trunk = Mat(new Color(0.36f, 0.24f, 0.15f), 0.1f);
            var rock = Mat(new Color(0.48f, 0.47f, 0.45f), 0.15f);
            var asphalt = Mat(new Color(0.15f, 0.15f, 0.16f), 0.5f);
            var curbY = Mat(new Color(1f, 0.78f, 0.05f), 0.4f);
            var curbB = Mat(new Color(0.05f, 0.05f, 0.05f), 0.4f);
            var mountain = Mat(new Color(0.42f, 0.52f, 0.60f), 0.05f);
            var snow = Mat(new Color(0.95f, 0.97f, 1f), 0.2f);

            Prim(PrimitiveType.Cylinder, decor, new Vector3(0, -0.06f, 6f), new Vector3(140f, 0.05f, 140f), grass);
            // Short piece of track under the sled.
            Prim(PrimitiveType.Cube, decor, new Vector3(0, -0.03f, 0f), new Vector3(4f, 0.06f, 40f), asphalt).transform.localRotation = Quaternion.Euler(0, 12f, 0);
            for (int i = -10; i < 10; i++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var c = Prim(PrimitiveType.Cube, decor, Vector3.zero, new Vector3(0.35f, 0.12f, 1f), (i & 1) == 0 ? curbY : curbB);
                    var rot = Quaternion.Euler(0, 12f, 0);
                    c.transform.localRotation = rot;
                    c.transform.localPosition = rot * new Vector3(side * 2.17f, 0.02f, i * 1f + 0.5f);
                }
            }
            // Turntable.
            Prim(PrimitiveType.Cylinder, decor, new Vector3(0, 0.0f, 0), new Vector3(3.1f, 0.04f, 3.1f), Mat(new Color(0.95f, 0.45f, 0.1f), 0.5f));
            Prim(PrimitiveType.Cylinder, decor, new Vector3(0, 0.03f, 0), new Vector3(2.9f, 0.04f, 2.9f), Mat(new Color(0.22f, 0.24f, 0.28f), 0.6f));

            var rng = new System.Random(7);
            for (int i = 0; i < 70; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = 7f + (float)rng.NextDouble() * 45f;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r + 10f);
                if (p.z < -2f) continue; // keep the view clear behind the camera
                if (Mathf.Abs(p.x) < 3.5f && p.z < 25f) continue; // keep the track clear
                float h = 3f + (float)rng.NextDouble() * 4f;
                Tree(decor, p, h, pine, trunk);
            }
            for (int i = 0; i < 14; i++)
            {
                var p = new Vector3(((float)rng.NextDouble() - 0.5f) * 30f, 0f, 3f + (float)rng.NextDouble() * 25f);
                if (Mathf.Abs(p.x) < 3f) continue;
                var rk = Prim(PrimitiveType.Cube, decor, p, Vector3.one * (0.4f + (float)rng.NextDouble() * 1.2f), rock);
                rk.transform.localRotation = Quaternion.Euler(rng.Next(0, 40), rng.Next(0, 360), rng.Next(0, 40));
            }
            for (int i = 0; i < 6; i++)
            {
                var p = new Vector3(-90f + i * 38f + (float)rng.NextDouble() * 10f, -2f, 150f + (float)rng.NextDouble() * 40f);
                float h = 45f + (float)rng.NextDouble() * 35f;
                Cone(decor, p, 40f + (float)rng.NextDouble() * 20f, h, mountain);
                Cone(decor, p + new Vector3(0, h * 0.62f, 0), 40f * 0.42f, h * 0.38f, snow);
            }
            Prim(PrimitiveType.Cylinder, decor, new Vector3(-18f, -0.04f, 30f), new Vector3(30f, 0.05f, 22f), grassDark);

            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(transform, false);
            turntable.localPosition = new Vector3(0, 0.05f, 0);
            turntable.localRotation = Quaternion.Euler(0, 200f, 0);
        }

        private void Update()
        {
            if (turntable != null) turntable.Rotate(0f, RotateSpeed * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        public void Show(SledType type, SledColor color)
        {
            Build();
            if (type == shownType && color == shownColor && current != null) return;
            var content = GameContent.Instance;
            var catalog = content != null ? content.Sleds : null;
            if (type != shownType || current == null)
            {
                if (current != null) Destroy(current);
                current = CreateVisual(content, catalog, type);
                shownType = type;
            }
            shownColor = color;
            if (current != null && catalog != null) Tint(current, catalog.GetBodyColor(color), catalog.GetHelmetColor((int)color), catalog);
        }

        private GameObject CreateVisual(GameContent content, SledCatalog catalog, SledType type)
        {
            var prefab = catalog != null ? catalog.GetPrefab(type) : null;
            bool placeholder = prefab == null;
            if (placeholder && content != null) prefab = content.SledPrefab;
            var visual = prefab != null ? prefab.transform.Find(SledVisuals.VisualRootName) : null;
            if (visual == null) return null;
            var go = Instantiate(visual.gameObject, turntable, false);
            go.name = "Preview_" + type;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            if (placeholder)
            {
                // Placeholder art is identical for all types: hint the silhouette by proportions.
                Vector3 s = type switch
                {
                    SledType.Sport => new Vector3(0.95f, 0.9f, 1.15f),
                    SledType.Tandem => new Vector3(1.0f, 1.0f, 1.42f),
                    SledType.Mini => new Vector3(1.08f, 1.05f, 0.82f),
                    _ => Vector3.one
                };
                go.transform.localScale = s;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        private void Tint(GameObject go, Color body, Color helmet, SledCatalog catalog)
        {
            foreach (var rider in go.GetComponentsInChildren<RiderAppearance>(true)) rider.SetHelmetColor(helmet, catalog.HelmetColors);
            block ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.gameObject.name;
                bool isBody = n.StartsWith("Body");
                bool isHelmet = n.StartsWith("Helmet") && go.GetComponentInChildren<RiderAppearance>(true) == null;
                if (!isBody && !isHelmet) continue;
                r.GetPropertyBlock(block);
                var c = isBody ? body : helmet;
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block);
            }
        }

        // ------------------------------------------------------------------ Decor helpers

        private Material Mat(Color c, float smooth)
        {
            var m = new Material(lit) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            return m;
        }

        private static GameObject Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private void Tree(Transform parent, Vector3 pos, float height, Material leaves, Material trunk)
        {
            Prim(PrimitiveType.Cylinder, parent, pos + new Vector3(0, height * 0.12f, 0), new Vector3(0.25f, height * 0.12f, 0.25f), trunk);
            Cone(parent, pos + new Vector3(0, height * 0.2f, 0), height * 0.3f, height * 0.55f, leaves);
            Cone(parent, pos + new Vector3(0, height * 0.5f, 0), height * 0.22f, height * 0.5f, leaves);
        }

        private static Mesh coneMesh;

        private static void Cone(Transform parent, Vector3 basePos, float radius, float height, Material mat)
        {
            if (coneMesh == null) coneMesh = BuildCone(7);
            var go = new GameObject("Cone");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = basePos;
            go.transform.localScale = new Vector3(radius, height, radius);
            go.transform.localRotation = Quaternion.Euler(0, basePos.x * 13f, 0);
            go.AddComponent<MeshFilter>().sharedMesh = coneMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Flat shaded cone, unit radius and height, base at y = 0.</summary>
        private static Mesh BuildCone(int sides)
        {
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int s = verts.Count;
                verts.Add(p0); verts.Add(Vector3.up); verts.Add(p1);
                tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
            }
            var m = new Mesh { name = "LowPolyCone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
