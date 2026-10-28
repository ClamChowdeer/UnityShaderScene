using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SceneLookLab
{
    internal sealed class MaterialPlan
    {
        internal sealed class Entry
        {
            public Renderer renderer;
            public Material material;
            public int slot;
            public bool convert, alphaClip, review;
            public string reason;
        }
        public readonly List<Entry> entries = new();
        public int Converted => entries.Count(e => e.convert);
        public int Preserved => entries.Count(e => !e.convert);
        public int NeedsReview => entries.Count(e => e.review);
        public string Summary => $"{Converted} slots eligible · {Preserved} preserved · {NeedsReview} need review";
        public string Describe()
        {
            var text = new StringBuilder(Summary + "\n");
            foreach (var e in entries)
                text.AppendLine($"{(e.convert ? "CONVERT" : e.review ? "REVIEW — PRESERVED" : "PRESERVE")} | {(e.renderer != null ? e.renderer.name : "(removed renderer)")} / slot {e.slot} | " +
                    $"{(e.material != null ? e.material.name : "(empty)")} | {(e.material?.shader != null ? e.material.shader.name : "(no shader)")} | {e.reason}");
            return text.ToString();
        }
    }

    internal static class MaterialCompatibility
    {
        internal static bool IsMeadow(Material material) => material != null && material.shader != null &&
            ((material.shader.name == "ScGame Greybox/Meadow Ground" && material.HasProperty("_ColorA") && material.HasProperty("_ColorB")) ||
             (material.shader.name == "Hidden/SceneLookLab/Surface" && material.HasProperty("_MeadowBlend") && material.GetFloat("_MeadowBlend") > 0.5f));

        internal static bool Known(Material m) => m.shader.name == "Universal Render Pipeline/Lit" ||
            m.shader.name == "Universal Render Pipeline/Simple Lit" || m.shader.name == "Universal Render Pipeline/Unlit" ||
            m.shader.name == "Hidden/SceneLookLab/Surface";

        internal static MaterialPlan Scan(IEnumerable<Renderer> renderers, LookProfile profile, Transform scope = null)
        {
            var plan = new MaterialPlan();
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.GetComponent<AppliedLookOutline>() != null ||
                    !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded ||
                    EditorSceneManager.IsPreviewScene(renderer.gameObject.scene) || EditorUtility.IsPersistent(renderer) ||
                    (scope != null && !renderer.transform.IsChildOf(scope)) ||
                    (profile.includedLayers.value & (1 << renderer.gameObject.layer)) == 0) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var m = materials[i];
                    var e = new MaterialPlan.Entry { renderer = renderer, material = m, slot = i };
                    plan.entries.Add(e);
                    if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) { e.reason = "Unsupported renderer type."; continue; }
                    if (renderer.HasPropertyBlock()) { e.reason = "Property block may override appearance."; e.review = true; continue; }
                    if (m == null || m.shader == null) { e.reason = "Missing material or shader."; e.review = true; continue; }
                    if ((profile.excludedMaterials ?? Array.Empty<Material>()).Contains(m) ||
                        (profile.excludedShaders ?? Array.Empty<Shader>()).Contains(m.shader))
                    { e.reason = "Explicitly excluded by this profile."; continue; }
                    e.alphaClip = (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > 0.5f) ||
                        m.IsKeywordEnabled("_ALPHATEST_ON") || m.GetTag("RenderType", false) == "TransparentCutout";
                    if (m.renderQueue >= (int)RenderQueue.Transparent || m.GetTag("RenderType", false) == "Transparent" ||
                        (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f))
                    { e.reason = "Transparent surface; keep its original shader."; continue; }
                    if (e.alphaClip && !profile.includeAlphaClipped) { e.reason = "Cutout meshes are excluded."; continue; }
                    bool meadow = IsMeadow(m);
                    if (meadow && (profile.style == LookStyle.StandardLit || profile.style == LookStyle.CustomShader))
                    { e.reason = "Meadow Ground world-space blend cannot be represented by this target. Choose a stylized look or retain the original."; e.review = true; continue; }
                    if (!meadow && !Known(m))
                    { e.reason = "No verified converter for this shader. Kept unchanged instead of guessing its colour."; e.review = true; continue; }
                    if (!meadow && (!m.HasProperty("_BaseMap") || !m.HasProperty("_BaseColor")))
                    { e.reason = "Expected base texture/colour properties are missing."; e.review = true; continue; }
                    e.convert = true;
                    bool replacesColour = profile.style == LookStyle.InkHatching || profile.style == LookStyle.ProceduralChecker ||
                        profile.style == LookStyle.WorldNormals;
                    e.reason = replacesColour ? "This look intentionally replaces source colours." :
                        meadow ? "Meadow Ground adapter preserves Earth/Moss colours and world-space blending; lighting is restyled." :
                        "Base texture/colour conversion; source lighting, normal/metallic/emission maps and vertex effects are not reproduced.";
                }
            }
            return plan;
        }
    }
}
