using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    internal static class OcclusionValidation
    {
        const int Layer = 29;
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception("Occlusion: " + message);
            Debug.Log("PASS: " + message);
        }

        internal static void Run()
        {
            Debug.Log("Occlusion suite compatibility mode: " +
                GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode);
            var oldPipeline = GraphicsSettings.defaultRenderPipeline;
            var oldQuality = QualitySettings.renderPipeline;
            var root = new GameObject("Occlusion regression fixture");
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            var probe = new Material(Shader.Find("Hidden/SceneLookLab/Tests/DepthEffectsProbe"));
            feature.passMaterial = probe;
            feature.fetchColorBuffer = false;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            feature.Create();
            renderer.rendererFeatures.Add(feature);
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.supportsCameraDepthTexture = false; // Source and asset both explicitly disable it.
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var foreground = new Material(lit);
            var background = new Material(lit);
            var maskA = new Texture2D(8, 8);
            var maskB = new Texture2D(8, 8);
            maskA.filterMode = maskB.filterMode = FilterMode.Point;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    maskA.SetPixel(x, y, new Color(1, 1, 1, x < 4 ? 0 : 1));
                    maskB.SetPixel(x, y, new Color(1, 1, 1, x < 4 ? 1 : 0));
                }
            maskA.Apply(); maskB.Apply();
            foreground.SetTexture("_BaseMap", maskA);
            foreground.SetTexture("_MainTex", maskB);
            foreground.SetFloat("_AlphaClip", 1);
            foreground.EnableKeyword("_ALPHATEST_ON");
            foreground.renderQueue = 2450;
            var front = Quad(root.transform, foreground, 3, 2.8f);
            Quad(root.transform, background, 10, 16).transform.localRotation = Quaternion.Euler(0, 35, 0);
            var cameraObject = new GameObject("Depth regression camera");
            cameraObject.transform.SetParent(root.transform);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 20;
            camera.fieldOfView = 60;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.GetUniversalAdditionalCameraData().requiresDepthOption = CameraOverrideOption.Off;
            string sourceCamera = EditorJsonUtility.ToJson(camera.GetUniversalAdditionalCameraData());
            try
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                using var preview = new PreviewSession();
                foreach (var mode in new[] { RenderingMode.Forward, RenderingMode.Deferred, RenderingMode.ForwardPlus })
                {
                    renderer.renderingMode = mode;
                    renderer.accurateGbufferNormals = true;
                    renderer.depthPrimingMode = DepthPrimingMode.Forced;
                    renderer.copyDepthMode = CopyDepthMode.ForcePrepass;
                    foreach (var style in LookPresets.Styles)
                    {
                        LookPresets.Apply(profile, style);
                        profile.includedLayers = 1 << Layer;
                        profile.includeAlphaClipped = true;
                        // Deliberately swap the alpha mask: stale original-camera depth
                        // would put the foreground on exactly the WRONG side of the image.
                        profile.sourceTexture = "_MainTex";
                        for (int effect = 0; effect < 4; effect++)
                        {
                            probe.SetFloat("_Effect", effect);
                            preview.Capture(camera, profile, root.transform, 256, 256);
                            var before = Pixels(preview.Original);
                            var after = Pixels(preview.Styled);
                            float originalDifference = At(before, 0.4f).r - At(before, 0.6f).r;
                            float styledDifference = At(after, 0.4f).r - At(after, 0.6f).r;
                            Check(Mathf.Abs(originalDifference) > 0.1f && originalDifference * styledDifference < -0.01f,
                                mode + "/" + style + "/effect" + effect + " follows CURRENT cutout occlusion, not original depth");
                            Check(At(after, 0.4f).g > 0.85f && At(after, 0.6f).g > 0.85f,
                                mode + "/" + style + "/effect" + effect + " supplies valid normals through foliage holes");
                            var left = At(after, 0.4f);
                            var right = At(after, 0.6f);
                            // Foreground faces the camera; background is tilted. This
                            // catches absent foreground normals even when depth is correct.
                            float nearNormal = left.r < right.r ? left.b : right.b;
                            float farNormal = left.r < right.r ? right.b : left.b;
                            Check(nearNormal < 0.04f && farNormal > 0.05f,
                                mode + "/" + style + "/effect" + effect + " normals match foreground versus tilted background");
                            if (style == LookStyle.Clay && mode == RenderingMode.Forward)
                                Save(preview.Styled, "Occlusion-effect" + effect + ".png");
                        }
                        Check(front.sharedMaterial == foreground, style + " restores cutout source after depth effects");
                    }
                }
                // Test depth-copy route without a normals consumer, with source depth OFF.
                renderer.renderingMode = RenderingMode.Forward;
                renderer.depthPrimingMode = DepthPrimingMode.Disabled;
                renderer.copyDepthMode = CopyDepthMode.AfterOpaques;
                feature.requirements = ScriptableRenderPassInput.Depth;
                renderer.SetDirty();
                LookPresets.Apply(profile, LookStyle.Cel);
                profile.includeAlphaClipped = true;
                profile.sourceTexture = "_MainTex";
                probe.SetFloat("_Effect", 1);
                preview.Capture(camera, profile, root.transform, 256, 256);
                var copyPixels = Pixels(preview.Styled);
                Check(Mathf.Abs(At(copyPixels, 0.4f).r - At(copyPixels, 0.6f).r) > 0.3f,
                    "Depth-copy route occludes raymarched clouds with source depth disabled");
                renderer.copyDepthMode = CopyDepthMode.ForcePrepass;
                preview.Capture(camera, profile, root.transform, 256, 256);
                var prepass = Pixels(preview.Styled);
                Check(Mathf.Abs(At(prepass, 0.4f).r - At(prepass, 0.6f).r) > 0.3f,
                    "DepthOnly prepass retains foliage alpha holes");
                // A fresh depth texture is still required when a consumer fails to
                // request depth but samples it after opaques (common third-party pattern).
                feature.requirements = ScriptableRenderPassInput.None;
                renderer.copyDepthMode = CopyDepthMode.AfterOpaques;
                renderer.SetDirty();
                preview.Capture(camera, profile, root.transform, 256, 256);
                var forcedDepth = Pixels(preview.Styled);
                Check(Mathf.Abs(At(forcedDepth, 0.4f).r - At(forcedDepth, 0.6f).r) > 0.3f,
                    "Preview camera independently requests fresh depth with source and effect requests OFF");

                front.enabled = false;
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.layer = Layer;
                sphere.transform.SetParent(root.transform);
                sphere.transform.localPosition = new Vector3(0, 0, 3);
                sphere.transform.localScale = Vector3.one * 1.5f;
                sphere.GetComponent<Renderer>().sharedMaterial = background;
                feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
                renderer.copyDepthMode = CopyDepthMode.ForcePrepass;
                renderer.SetDirty();
                probe.SetFloat("_Effect", 0);
                LookPresets.Apply(profile, LookStyle.OutlineToon);
                profile.outlineWidth = 0;
                preview.Capture(camera, profile, root.transform, 256, 256);
                var noOutline = Pixels(preview.Styled);
                profile.outlineWidth = 8;
                preview.Capture(camera, profile, root.transform, 256, 256);
                var withOutline = Pixels(preview.Styled);
                int shellPixels = 0;
                for (int p = 0; p < noOutline.Length; p++)
                    if (noOutline[p].r > 0.7f && withOutline[p].r < 0.5f && withOutline[p].g > 0.85f) shellPixels++;
                Check(shellPixels > 20, "Expanded toon silhouette contributes matching depth AND normals");
                Save(preview.Styled, "Outline-depth-normals.png");

                LookPresets.Apply(profile, LookStyle.Clay);
                profile.clayDetail = profile.clayUnevenness = 0;
                preview.Capture(camera, profile, root.transform, 256, 256);
                var smoothNormals = Pixels(preview.Styled);
                profile.clayDetail = profile.clayUnevenness = 1;
                preview.Capture(camera, profile, root.transform, 256, 256);
                var clayNormals = Pixels(preview.Styled);
                Check(smoothNormals.Zip(clayNormals, (a,b) => Mathf.Abs(a.b-b.b)).Sum() > 1,
                    "Clay depth normals include the same procedural bump normal as its colour pass");
                Check(EditorJsonUtility.ToJson(camera.GetUniversalAdditionalCameraData()) == sourceCamera &&
                    !pipeline.supportsCameraDepthTexture, "Preview does not change source camera or pipeline depth settings");
                Check(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/SceneLookLab/Surface")) &&
                    !ShaderUtil.ShaderHasError(Shader.Find("Hidden/SceneLookLab/Outline")), "Surface and outline depth-normal shader variants compile");
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = oldPipeline;
                QualitySettings.renderPipeline = oldQuality;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(foreground); Object.DestroyImmediate(background);
                Object.DestroyImmediate(maskA); Object.DestroyImmediate(maskB);
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(renderer);
                Object.DestroyImmediate(feature);
                Object.DestroyImmediate(probe);
            }
        }

        static MeshRenderer Quad(Transform parent, Material material, float z, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.transform.SetParent(parent);
            go.layer = Layer;
            go.transform.localPosition = new Vector3(0, 0, z);
            go.transform.localScale = Vector3.one * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go.GetComponent<MeshRenderer>();
        }
        static Color At(Color[] pixels, float x) => pixels[128 * 256 + (int)(x * 256)];
        static Color[] Pixels(RenderTexture target)
        {
            var old = RenderTexture.active;
            var tex = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try
            {
                RenderTexture.active = target;
                tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                tex.Apply();
                return tex.GetPixels();
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(tex); }
        }
        static void Save(RenderTexture target, string name)
        {
            var old = RenderTexture.active;
            var tex = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                tex.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.GetFullPath("../Validation-0.9/" + name), tex.EncodeToPNG());
            }
            finally { RenderTexture.active = old; Object.DestroyImmediate(tex); }
        }
    }
}
