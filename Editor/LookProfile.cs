using UnityEngine;

namespace SceneLookLab
{
    // Keep the original numeric values stable for profiles saved with 0.1.0.
    public enum LookStyle
    {
        Cel = 0, TexturedUnlit = 1, Clay = 2, CustomShader = 3,
        SoftToon = 4, InkHatching = 5, ProceduralChecker = 6, RimLight = 7, WorldNormals = 8,
        OutlineToon = 9, SoftPattern = 10, HardBrush = 11, StandardLit = 12
    }

    [CreateAssetMenu(menuName = "Scene Look Lab/Look Profile")]
    public sealed class LookProfile : ScriptableObject
    {
        public LookStyle style = LookStyle.Cel;
        public Color tint = Color.white;
        public Color shadowColor = new Color(0.27f, 0.32f, 0.43f);
        [Range(2, 6)] public int bands = 3;
        [Range(0.01f, 0.5f)] public float softness = 0.15f;
        [Range(2, 40)] public float patternScale = 8;
        [Range(0, 180)] public float hatchRotation;
        public Color accentColor = new Color(0.25f, 0.8f, 1f);
        [Range(0.5f, 8)] public float rimPower = 3;
        [Range(0, 3)] public float rimStrength = 1;
        [Range(0, 8)] public float outlineWidth = 2;
        public Color outlineColor = new Color(0.025f, 0.025f, 0.035f);
        [Range(0, 1)] public float brushStrength = 0.35f;
        [Range(0, 1)] public float clayDetail = 0.55f;
        [Range(0.02f, 0.5f)] public float clayDetailSize = 0.08f;
        [Range(0, 1)] public float clayUnevenness = 0.35f;
        [Range(0, 1)] public float clayShine = 0.35f;
        [Range(0, 1)] public float clayTextureStrength = 1;
        public LayerMask includedLayers = ~0;
        public bool includeAlphaClipped;
        public Shader customShader;
        public string sourceTexture = "_BaseMap";
        public string sourceColor = "_BaseColor";
        public string targetTexture = "_BaseMap";
        public string targetColor = "_BaseColor";

        internal LookProfile Copy()
        {
            var copy = Instantiate(this);
            copy.hideFlags = HideFlags.HideAndDontSave;
            return copy;
        }
    }
}
