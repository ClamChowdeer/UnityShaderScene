using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    // Synchronous transaction: no overridden renderer survives past the render request,
    // including when material conversion, rendering or an editor callback throws.
    internal sealed class MaterialTransaction : IDisposable
    {
        readonly List<(Renderer renderer, Material[] materials, bool wasDirty)> originals = new();
        readonly Dictionary<Material, Material> replacements = new();
        readonly List<Object> outlineObjects = new();
        readonly List<(Transform transform, bool wasDirty)> outlineParents = new();
        readonly List<(Renderer renderer, bool enabled, bool dirty)> appliedOutlines = new();
        public int ChangedSlots { get; private set; }
        public int SkippedSlots { get; private set; }
        public int PropertyBlockRenderers { get; private set; }

        public void Apply(IEnumerable<Renderer> renderers, LookProfile profile, Transform scope = null, bool createOutlines = true)
        {
            Shader shader = profile.style == LookStyle.CustomShader ? profile.customShader :
                Shader.Find(profile.style == LookStyle.StandardLit ? "Universal Render Pipeline/Lit" : "Hidden/SceneLookLab/Surface");
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("The selected shader is missing or unsupported.");

            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.GetComponent<AppliedLookOutline>() != null || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded ||
                    EditorSceneManager.IsPreviewScene(renderer.gameObject.scene) ||
                    EditorUtility.IsPersistent(renderer) ||
                    (scope != null && !renderer.transform.IsChildOf(scope)) ||
                    (profile.includedLayers.value & (1 << renderer.gameObject.layer)) == 0)
                    continue;

                var source = renderer.sharedMaterials;
                // Property blocks can override the proposed look's properties. Preserve them
                // by leaving these renderers unchanged, rather than silently mis-previewing.
                if (!(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) || renderer.HasPropertyBlock())
                {
                    SkippedSlots += source.Length;
                    if (renderer.HasPropertyBlock()) PropertyBlockRenderers++;
                    continue;
                }

                var output = (Material[])source.Clone();
                bool changed = false;
                for (int i = 0; i < source.Length; i++)
                {
                    var material = source[i];
                    bool alphaClip = material != null &&
                        ((material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f) ||
                         material.IsKeywordEnabled("_ALPHATEST_ON") || material.GetTag("RenderType", false) == "TransparentCutout");
                    if (material == null || material.shader == null ||
                        material.renderQueue >= (int)RenderQueue.Transparent ||
                        material.GetTag("RenderType", false) == "Transparent" ||
                        (material.HasProperty("_Surface") && material.GetFloat("_Surface") > 0.5f) ||
                        (alphaClip && !profile.includeAlphaClipped))
                    {
                        SkippedSlots++;
                        continue;
                    }
                    if (!replacements.TryGetValue(material, out var replacement))
                    {
                        replacement = BuildMaterial(material, profile, shader, alphaClip);
                        replacements.Add(material, replacement);
                    }
                    output[i] = replacement;
                    ChangedSlots++;
                    changed = true;
                }
                if (!changed) continue;
                originals.Add((renderer, source, EditorUtility.IsDirty(renderer)));
                renderer.sharedMaterials = output;
                foreach (var existing in renderer.GetComponentsInChildren<AppliedLookOutline>(true))
                    if (existing.source == renderer && existing.shell != null)
                    {
                        appliedOutlines.Add((existing.shell, existing.shell.enabled, EditorUtility.IsDirty(existing.shell)));
                        existing.shell.enabled = false;
                    }
                if (createOutlines && profile.style == LookStyle.OutlineToon && profile.outlineWidth > 0)
                    AddOutline(renderer, source, output, profile);
            }
        }

        void AddOutline(Renderer renderer, Material[] source, Material[] output, LookProfile profile)
        {
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                mesh = new Mesh { name = "Scene Look Lab Outline Pose", hideFlags = HideFlags.HideAndDontSave };
                outlineObjects.Add(mesh);
                skinned.BakeMesh(mesh);
            }
            else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
            if (mesh == null) return;
            var shader = Shader.Find("Hidden/SceneLookLab/Outline");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The outline shader is unavailable.");
            var materials = new Material[output.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var material = new Material(shader) { name = "Scene Look Lab Outline Material", hideFlags = HideFlags.HideAndDontSave };
                outlineObjects.Add(material);
                material.SetFloat("_Visible", output[i] != source[i] ? 1 : 0);
                material.SetColor("_OutlineColor", profile.outlineColor);
                material.SetFloat("_OutlineWidth", profile.outlineWidth);
                if (output[i] != source[i])
                {
                    material.SetTexture("_BaseMap", output[i].GetTexture("_BaseMap"));
                    material.SetTextureScale("_BaseMap", output[i].GetTextureScale("_BaseMap"));
                    material.SetTextureOffset("_BaseMap", output[i].GetTextureOffset("_BaseMap"));
                    material.SetFloat("_AlphaClip", output[i].GetFloat("_AlphaClip"));
                    material.SetFloat("_Cutoff", output[i].GetFloat("_Cutoff"));
                    material.SetColor("_BaseColor", output[i].GetColor("_BaseColor"));
                }
                materials[i] = material;
            }
            var parent = renderer.transform;
            outlineParents.Add((parent, EditorUtility.IsDirty(parent)));
            var go = new GameObject("Scene Look Lab Outline") { hideFlags = HideFlags.HideAndDontSave, layer = renderer.gameObject.layer };
            outlineObjects.Add(go);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var shell = go.AddComponent<MeshRenderer>();
            shell.sharedMaterials = materials;
            shell.shadowCastingMode = ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = LightProbeUsage.Off;
            shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static Material BuildMaterial(Material source, LookProfile profile, Shader shader, bool alphaClip)
        {
            Material result = null;
            try
            {
                // Start Lit from clean Unity defaults, never inherited stylized keywords or maps.
                result = profile.style == LookStyle.StandardLit ? new Material(shader) : new Material(source);
                result.name = source.name + " (Look Preview)";
                result.hideFlags = HideFlags.HideAndDontSave;
                result.shader = shader;
                if (profile.style != LookStyle.CustomShader) result.shaderKeywords = Array.Empty<string>();
                result.renderQueue = alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;

                string sourceMap = FindProperty(source, profile.sourceTexture, "_BaseMap", "_MainTex");
                string sourceColor = FindProperty(source, profile.sourceColor, "_BaseColor", "_Color");
                string targetMap = profile.style == LookStyle.CustomShader ? profile.targetTexture : "_BaseMap";
                string targetColor = profile.style == LookStyle.CustomShader ? profile.targetColor : "_BaseColor";
                if (!string.IsNullOrEmpty(targetMap) && result.HasProperty(targetMap))
                {
                    result.SetTexture(targetMap, sourceMap == null ? Texture2D.whiteTexture : source.GetTexture(sourceMap));
                    if (sourceMap != null)
                    {
                        result.SetTextureScale(targetMap, source.GetTextureScale(sourceMap));
                        result.SetTextureOffset(targetMap, source.GetTextureOffset(sourceMap));
                    }
                }
                if (!string.IsNullOrEmpty(targetColor) && result.HasProperty(targetColor))
                    result.SetColor(targetColor, (sourceColor == null ? Color.white : source.GetColor(sourceColor)) * profile.tint);
                if (profile.style == LookStyle.StandardLit)
                {
                    result.SetFloat("_AlphaClip", alphaClip ? 1 : 0);
                    result.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
                    result.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2);
                    result.SetOverrideTag("RenderType", alphaClip ? "TransparentCutout" : "Opaque");
                    if (alphaClip) result.EnableKeyword("_ALPHATEST_ON");
                }
                else if (profile.style != LookStyle.CustomShader)
                {
                    result.SetFloat("_LookMode", (float)profile.style);
                    result.SetFloat("_Bands", profile.bands);
                    result.SetColor("_ShadowTint", profile.shadowColor);
                    result.SetColor("_ClayColor", profile.tint);
                    result.SetFloat("_Softness", profile.softness);
                    result.SetFloat("_PatternScale", profile.patternScale);
                    result.SetFloat("_HatchRotation", profile.hatchRotation);
                    result.SetColor("_AccentColor", profile.accentColor);
                    result.SetFloat("_RimPower", profile.rimPower);
                    result.SetFloat("_RimStrength", profile.rimStrength);
                    result.SetFloat("_BrushStrength", profile.brushStrength);
                    result.SetFloat("_ClayDetail", profile.clayDetail);
                    result.SetFloat("_ClayDetailSize", profile.clayDetailSize);
                    result.SetFloat("_ClayUnevenness", profile.clayUnevenness);
                    result.SetFloat("_ClayShine", profile.clayShine);
                    result.SetFloat("_ClayTextureStrength", profile.clayTextureStrength);
                    result.SetFloat("_AlphaClip", alphaClip ? 1 : 0);
                    result.SetFloat("_Cutoff", source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f);
                    result.SetFloat("_Cull", source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2);
                }
                return result;
            }
            catch { if (result != null) Object.DestroyImmediate(result); throw; }
        }

        static string FindProperty(Material material, params string[] names)
        {
            foreach (string name in names)
                if (!string.IsNullOrEmpty(name) && material.HasProperty(name)) return name;
            return null;
        }

        public void Dispose()
        {
            // Destroy shells first, before their materials and baked meshes.
            for (int i = outlineObjects.Count - 1; i >= 0; i--)
                if (outlineObjects[i] != null) Object.DestroyImmediate(outlineObjects[i]);
            outlineObjects.Clear();
            foreach (var parent in outlineParents)
                if (parent.transform != null && !parent.wasDirty) EditorUtility.ClearDirty(parent.transform);
            outlineParents.Clear();
            foreach (var outline in appliedOutlines)
                if (outline.renderer != null)
                {
                    outline.renderer.enabled = outline.enabled;
                    if (!outline.dirty) EditorUtility.ClearDirty(outline.renderer);
                }
            appliedOutlines.Clear();
            foreach (var entry in originals)
            {
                if (entry.renderer == null) continue;
                entry.renderer.sharedMaterials = entry.materials;
                if (!entry.wasDirty) EditorUtility.ClearDirty(entry.renderer);
            }
            originals.Clear();
            foreach (var material in replacements.Values) if (material != null) Object.DestroyImmediate(material);
            replacements.Clear();
        }
    }

    internal sealed class PreviewSession : IDisposable
    {
        Camera previewCamera;
        public RenderTexture Original { get; private set; }
        public RenderTexture Styled { get; private set; }
        public string Report { get; private set; }

        public void Capture(Camera source, LookProfile profile, Transform scope, int width = 1280, int height = 720)
        {
            if (source == null) throw new InvalidOperationException("Choose a scene camera or open the Scene view.");
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                throw new InvalidOperationException("This prototype requires Universal Render Pipeline (URP).");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Capture is available in Edit Mode only.");
            if (previewCamera == null)
            {
                var go = new GameObject("Scene Look Lab Preview Camera") { hideFlags = HideFlags.HideAndDontSave };
                previewCamera = go.AddComponent<Camera>();
                previewCamera.enabled = false;
            }
            previewCamera.CopyFrom(source);
            previewCamera.enabled = false;
            previewCamera.targetTexture = null;
            previewCamera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            previewCamera.aspect = (float)width / height;
            var data = previewCamera.GetUniversalAdditionalCameraData();
            if (source.TryGetComponent<UniversalAdditionalCameraData>(out var sourceData))
            {
                EditorUtility.CopySerialized(sourceData, data);
                data.renderType = CameraRenderType.Base;
                data.cameraStack.Clear();
                data.antialiasing = AntialiasingMode.None;
            }
            else
            {
                data.renderPostProcessing = true;
                data.volumeLayerMask = ~0;
            }
            if (data.volumeTrigger == null) data.volumeTrigger = source.transform;
            // Each SubmitRenderRequest runs its own URP depth pass/copy while the
            // matching material transaction is active. Never borrow source-camera
            // depth, or inherit an explicit Off override from that camera.
            data.requiresDepthOption = CameraOverrideOption.On;
            previewCamera.depthTextureMode |= DepthTextureMode.Depth;
            EnsureTargets(width, height);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = Original };
            if (!RenderPipeline.SupportsRenderRequest(previewCamera, request))
                throw new InvalidOperationException("The current renderer does not support single-camera render requests.");
            RenderPipeline.SubmitRenderRequest(previewCamera, request);
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None), profile, scope);
                request.destination = Styled;
                RenderPipeline.SubmitRenderRequest(previewCamera, request);
                Report = $"{transaction.ChangedSlots} material slots previewed · {transaction.SkippedSlots} skipped · " +
                    $"{transaction.PropertyBlockRenderers} renderers with property blocks skipped";
            }
        }

        void EnsureTargets(int width, int height)
        {
            if (Original != null && Original.width == width && Original.height == height) return;
            ReleaseTargets();
            Original = NewTarget("Original", width, height);
            Styled = NewTarget("Look", width, height);
        }

        static RenderTexture NewTarget(string label, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                { name = "Scene Look Lab " + label, hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
            texture.Create();
            return texture;
        }

        void ReleaseTargets()
        {
            if (Original != null) { Original.Release(); Object.DestroyImmediate(Original); }
            if (Styled != null) { Styled.Release(); Object.DestroyImmediate(Styled); }
            Original = Styled = null;
        }

        public void Dispose()
        {
            ReleaseTargets();
            if (previewCamera != null) Object.DestroyImmediate(previewCamera.gameObject);
        }
    }
}
