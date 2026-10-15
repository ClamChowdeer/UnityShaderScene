using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    internal static class StandardLitValidation
    {
        internal static void Run(Material original)
        {
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            Material clay = null, lit = null;
            try
            {
                LookPresets.Apply(profile, LookStyle.Clay);
                clay = MaterialTransaction.BuildMaterial(original, profile, Shader.Find("Hidden/SceneLookLab/Surface"), true);
                LookPresets.Apply(profile, LookStyle.StandardLit);
                lit = MaterialTransaction.BuildMaterial(clay, profile, Shader.Find("Universal Render Pipeline/Lit"), true);
                Check(lit.shader.name == "Universal Render Pipeline/Lit", "Clay converts to actual Unity Lit shader");
                Check(lit.GetTexture("_BaseMap") == clay.GetTexture("_BaseMap") && lit.GetColor("_BaseColor") == clay.GetColor("_BaseColor"),
                    "Clay-to-Lit preserves current base texture and colour");
                Check(lit.IsKeywordEnabled("_ALPHATEST_ON") && lit.GetFloat("_AlphaClip") == 1 && lit.renderQueue == 2450 &&
                    lit.GetTag("RenderType", false) == "TransparentCutout", "Standard Lit cutout keyword and render state configured");
                Object.DestroyImmediate(lit);
                lit = MaterialTransaction.BuildMaterial(clay, profile, Shader.Find("Universal Render Pipeline/Lit"), false);
                Check(!lit.IsKeywordEnabled("_ALPHATEST_ON") && lit.GetFloat("_Surface") == 0 && lit.GetFloat("_ZWrite") == 1,
                    "Standard Lit opaque materials use clean render state");
            }
            finally
            {
                if (lit != null) Object.DestroyImmediate(lit);
                if (clay != null) Object.DestroyImmediate(clay);
                Object.DestroyImmediate(profile);
            }
        }

        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            Debug.Log("PASS: " + message);
        }
    }
}
