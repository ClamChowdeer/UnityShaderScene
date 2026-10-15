using UnityEngine;

namespace SceneLookLab
{
    // Saved with an applied look; no dependency on UnityEditor in player builds.
    [ExecuteAlways, AddComponentMenu("")]
    public sealed class AppliedLookOutline : MonoBehaviour
    {
        public Renderer source;
        public Renderer shell;

        void LateUpdate()
        {
            if (shell == null) return;
            shell.enabled = source != null && source.enabled;
            if (source == null) return;
            shell.forceRenderingOff = source.forceRenderingOff;
            if (source is SkinnedMeshRenderer skin && shell is SkinnedMeshRenderer outline && skin.sharedMesh != null)
            {
                for (int i = 0; outline.sharedMesh != null && i < Mathf.Min(skin.sharedMesh.blendShapeCount, outline.sharedMesh.blendShapeCount); i++)
                    outline.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                outline.localBounds = skin.localBounds;
            }
        }
    }
}
