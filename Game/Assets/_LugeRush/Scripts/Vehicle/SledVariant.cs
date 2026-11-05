using UnityEngine;

namespace LugeRush.Vehicle
{
    /// <summary>
    /// Lets one sled instance change its sled type at runtime (menu selection, randomised bots) without respawning:
    /// the "SledVisual" child (art model + riders) and the physical dimensions (BoxCollider, probe rectangle,
    /// camera anchors) are copied from the type's prefab in the <see cref="SledCatalog"/>.
    /// Called from <see cref="SledController.ConfigureBody"/> whenever Stats change.
    /// </summary>
    [DisallowMultipleComponent]
    public class SledVariant : MonoBehaviour
    {
        public SledCatalog Catalog;
        [Tooltip("Type of the model currently under SledVisual (set by the prefab factory).")]
        public SledType CurrentType = SledType.Classic;

        public event System.Action<SledType> TypeChanged;

        /// <summary>Swaps model + dimensions to <paramref name="type"/>. No-op when already that type.</summary>
        public bool ApplyType(SledType type)
        {
            if (type == CurrentType || Catalog == null) return false;
            var prefab = Catalog.GetPrefab(type);
            if (prefab == null) return false;
            var srcVisual = prefab.transform.Find(SledVisuals.VisualRootName);
            if (srcVisual == null) return false;

            // Remove the old model first so lookups by name (Camera_FP, Handlebar, Head...) only see the new one.
            var old = transform.Find(SledVisuals.VisualRootName);
            if (old != null)
            {
                old.SetParent(null, false);
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject);
                else DestroyImmediate(old.gameObject);
            }

            var visual = Instantiate(srcVisual.gameObject, transform, false);
            visual.name = SledVisuals.VisualRootName;
            visual.transform.SetLocalPositionAndRotation(srcVisual.localPosition, srcVisual.localRotation);
            visual.transform.SetSiblingIndex(srcVisual.GetSiblingIndex());
            SetLayerRecursive(visual, gameObject.layer);

            CopyColliders(prefab);
            CopyAnchor(prefab, "CameraTarget");
            CopyAnchor(prefab, "Camera_FP");
            var srcController = prefab.GetComponent<SledController>();
            if (srcController != null && TryGetComponent(out SledController controller))
            {
                controller.ProbeHalfExtents = srcController.ProbeHalfExtents;
                controller.CenterOfMass = srcController.CenterOfMass;
            }

            CurrentType = type;
            if (TryGetComponent(out SledVisuals visuals)) visuals.SetVisualRoot(visual.transform);
            if (TryGetComponent(out SledPaint paint)) paint.Rebind();
            if (TryGetComponent(out SledAnimatorBridge bridge)) bridge.Rebind();
            if (TryGetComponent(out RiderFirstPersonAnchor fp)) fp.Rebind();
            TypeChanged?.Invoke(type);
            return true;
        }

        private void CopyColliders(GameObject prefab)
        {
            var src = prefab.GetComponent<BoxCollider>();
            var dst = GetComponent<BoxCollider>();
            if (src == null || dst == null) return;
            dst.center = src.center;
            dst.size = src.size;
        }

        private void CopyAnchor(GameObject prefab, string anchorName)
        {
            var src = prefab.transform.Find(anchorName);
            var dst = transform.Find(anchorName);
            if (src == null || dst == null) return;
            dst.SetLocalPositionAndRotation(src.localPosition, src.localRotation);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }
}
