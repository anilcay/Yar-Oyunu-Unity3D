using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LugeRush.Track
{
    /// <summary>
    /// Per-map render settings that do not live in the scene: main light shadow distance (URP asset) and the
    /// game camera's post-processing flag. The asset value is restored when the map unloads.
    /// </summary>
    public class LevelAtmosphere : MonoBehaviour
    {
        public float ShadowDistance = 120f;

        private UniversalRenderPipelineAsset asset;
        private float previousDistance;

        private void Awake()
        {
            asset = UniversalRenderPipeline.asset;
            if (asset != null)
            {
                previousDistance = asset.shadowDistance;
                asset.shadowDistance = ShadowDistance;
            }
            var cam = Camera.main;
            if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        }

        private void OnDestroy()
        {
            if (asset != null) asset.shadowDistance = previousDistance;
        }
    }
}
