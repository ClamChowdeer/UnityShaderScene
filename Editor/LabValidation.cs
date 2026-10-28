using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    // Executed only in the disposable validation project using -executeMethod.
    public static class LabValidation
    {
        static Camera camera;
        static MeshRenderer subject;
        static Material original;
        static string destination;
        static int waitFrames;
        static bool? previousCompatibility;

        public static void RunCompatibility()
        {
            if (!Application.isBatchMode || !File.Exists("Assets/.gitkeep"))
                throw new InvalidOperationException("Use only the disposable validation project.");
            var settings = GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>();
            previousCompatibility = settings.enableRenderCompatibilityMode;
            settings.enableRenderCompatibilityMode = true;
            Run();
        }

        static void RestoreCompatibility()
        {
            if (!previousCompatibility.HasValue) return;
            GraphicsSettings.GetRenderPipelineSettings<RenderGraphSettings>().enableRenderCompatibilityMode = previousCompatibility.Value;
            previousCompatibility = null;
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists("Assets/.gitkeep"))
                throw new InvalidOperationException("Validation is restricted to the isolated batch test project.");
            try
            {
                destination = Path.GetFullPath("../Validation-0.8");
                Directory.CreateDirectory(destination);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!AssetDatabase.IsValidFolder("Assets/LabDemo")) AssetDatabase.CreateFolder("Assets", "LabDemo");
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, "Assets/LabDemo/Renderer.asset");
                var pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, "Assets/LabDemo/URP.asset");
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = pipeline;
                original = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                original.SetColor("_BaseColor", new Color(0.74f, 0.25f, 0.12f));
                original.SetFloat("_Smoothness", 0.55f);
                var checker = new Texture2D(32, 32);
                checker.filterMode = FilterMode.Point;
                for (int y = 0; y < 32; y++)
                    for (int x = 0; x < 32; x++)
                        checker.SetPixel(x, y, ((x / 8 + y / 8) % 2) == 0 ? Color.white : new Color(0.2f, 0.38f, 0.75f));
                checker.Apply();
                AssetDatabase.CreateAsset(checker, "Assets/LabDemo/Checker.asset");
                original.SetTexture("_BaseMap", checker);
                original.SetTextureScale("_BaseMap", new Vector2(2, 1));
                original.SetTextureOffset("_BaseMap", new Vector2(0.125f, 0));
                AssetDatabase.CreateAsset(original, "Assets/LabDemo/Subject.mat");
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = "Textured Sphere";
                sphere.transform.position = new Vector3(0, 1.2f, 0);
                sphere.transform.localScale = Vector3.one * 2;
                subject = sphere.GetComponent<MeshRenderer>();
                subject.sharedMaterial = original;
                for (int i = 0; i < 5; i++)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "Architecture " + i;
                    cube.transform.position = new Vector3((i - 2) * 1.8f, 0.5f + i * 0.22f, 3);
                    cube.transform.localScale = new Vector3(1.2f, 1 + i * 0.44f, 1.2f);
                    var material = new Material(original);
                    material.SetColor("_BaseColor", Color.HSVToRGB(i / 6f, 0.55f, 0.85f));
                    AssetDatabase.CreateAsset(material, "Assets/LabDemo/Block" + i + ".mat");
                    cube.GetComponent<Renderer>().sharedMaterial = material;
                }
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                var groundMaterial = new Material(original);
                groundMaterial.SetColor("_BaseColor", new Color(0.45f, 0.5f, 0.4f));
                groundMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                AssetDatabase.CreateAsset(groundMaterial, "Assets/LabDemo/Ground.mat");
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.6f;
                sun.shadows = LightShadows.Soft;
                sun.transform.rotation = Quaternion.Euler(45, -35, 0);
                RenderSettings.sun = sun;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.3f, 0.34f, 0.4f);
                camera = new GameObject("Main Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(8, 6, -11);
                camera.transform.LookAt(new Vector3(0, 1, 1));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.055f, 0.075f, 0.12f);
                camera.GetUniversalAdditionalCameraData();
                EditorSceneManager.SaveScene(scene, "Assets/LabDemo/Demo.unity");
                AssetDatabase.SaveAssets();
                EditorApplication.update += WaitForPipeline;
            }
            catch (Exception exception) { Fail(exception); }
        }

        static void WaitForPipeline()
        {
            if (++waitFrames < 30) return;
            EditorApplication.update -= WaitForPipeline;
            try { Validate(); }
            catch (Exception exception) { Fail(exception); }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("ASSERTION FAILED: " + message);
            Debug.Log("PASS: " + message);
        }

        static void Validate()
        {
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            var originalJson = EditorJsonUtility.ToJson(original);
            bool dirty = subject.gameObject.scene.isDirty;
            bool rendererDirty = EditorUtility.IsDirty(subject);
            // Deliberately fail inside an override scope to verify exception restoration.
            try
            {
                using var transaction = new MaterialTransaction();
                transaction.Apply(new[] { subject }, profile);
                Require(subject.sharedMaterial != original, "Temporary material is assigned inside transaction");
                Require(subject.sharedMaterial.GetTexture("_BaseMap") == original.GetTexture("_BaseMap"), "Texture preserved");
                Require(subject.sharedMaterial.GetTextureScale("_BaseMap") == original.GetTextureScale("_BaseMap"), "Texture tiling preserved");
                Require(subject.sharedMaterial.GetTextureOffset("_BaseMap") == original.GetTextureOffset("_BaseMap"), "Texture offset preserved");
                throw new OperationCanceledException("Deliberate test failure");
            }
            catch (OperationCanceledException) { }
            Require(subject.sharedMaterial == original, "Exception restores original material");
            Require(subject.gameObject.scene.isDirty == dirty, "Transaction preserves scene dirty state");
            Require(EditorUtility.IsDirty(subject) == rendererDirty, "Transaction preserves renderer dirty state");
            Require(EditorJsonUtility.ToJson(original) == originalJson, "Source material unchanged");

            profile.includedLayers = 0;
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Require(transaction.ChangedSlots == 0 && subject.sharedMaterial == original, "Excluded layers remain untouched");
            }
            profile.includedLayers = ~0;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", Color.green);
            subject.SetPropertyBlock(block);
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Require(transaction.ChangedSlots == 0 && transaction.PropertyBlockRenderers == 1, "Property block renderers skipped");
            }
            subject.SetPropertyBlock(null);
            var transparent = new Material(original) { renderQueue = 3000 };
            subject.sharedMaterial = transparent;
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Require(transaction.ChangedSlots == 0, "Transparent materials skipped");
            }
            subject.sharedMaterial = original;
            Object.DestroyImmediate(transparent);

            var cutout = new Material(original);
            cutout.SetFloat("_AlphaClip", 1);
            subject.sharedMaterial = cutout;
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Require(transaction.ChangedSlots == 0, "Cutout meshes excluded by default");
            }
            profile.includeAlphaClipped = true;
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Require(transaction.ChangedSlots == 1 && subject.sharedMaterial.GetFloat("_AlphaClip") == 1, "Cutout preview is opt-in and preserves alpha clipping");
            }
            profile.includeAlphaClipped = false;
            subject.sharedMaterial = original;
            Object.DestroyImmediate(cutout);
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile, camera.transform);
                Require(transaction.ChangedSlots == 0, "Hierarchy scope excludes unrelated objects");
            }
            EditorUtility.SetDirty(subject);
            using (var transaction = new MaterialTransaction()) transaction.Apply(new[] { subject }, profile);
            Require(EditorUtility.IsDirty(subject), "Pre-existing renderer edits stay dirty");
            EditorUtility.ClearDirty(subject);

            profile.style = LookStyle.CustomShader;
            profile.customShader = Shader.Find("Universal Render Pipeline/Unlit");
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Require(subject.sharedMaterial.shader == profile.customShader, "Custom shader applied to temporary material");
                Require(subject.sharedMaterial.GetTexture("_BaseMap") == original.GetTexture("_BaseMap"), "Custom shader texture mapping preserved");
            }
            profile.style = LookStyle.Cel;
            var savedProfile = Object.Instantiate(profile);
            savedProfile.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(savedProfile, "Assets/LabDemo/Cel Look.asset");
            AssetDatabase.SaveAssets();
            var workingCopy = savedProfile.Copy();
            workingCopy.bands = 6;
            Require(savedProfile.bands == 3, "Editing a profile copy leaves the saved profile unchanged");
            Object.DestroyImmediate(workingCopy);

            var shader = Shader.Find("Hidden/SceneLookLab/Surface");
            Require(shader != null && !ShaderUtil.ShaderHasError(shader), "Preview shader imports without errors");
            // Earlier tests deliberately edit renderer state. Establish a clean baseline
            // after those changes so the render-only assertions measure the captures.
            EditorSceneManager.SaveScene(subject.gameObject.scene);
            dirty = subject.gameObject.scene.isDirty;
            int hiddenMaterials = Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.EndsWith(" (Look Preview)"));
            using (var preview = new PreviewSession())
            {
                foreach (LookStyle style in LookPresets.Styles.Concat(new[] { LookStyle.CustomShader }))
                {
                    LookPresets.Apply(profile, style);
                    preview.Capture(camera, profile, null, 960, 540);
                    Require(subject.sharedMaterial == original, style + " capture restores assignments");
                    Require(subject.gameObject.scene.isDirty == dirty, style + " capture preserves scene dirty state");
                    SaveImage(preview.Styled, style + ".png");
                }
                SaveImage(preview.Original, "Original.png");
                Require(!ShaderUtil.ShaderHasError(shader), "Preview shader compiles for actual rendering");
            }
            Require(Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.EndsWith(" (Look Preview)")) == hiddenMaterials, "No temporary material leak after captures");
            Require(!Resources.FindObjectsOfTypeAll<Camera>().Any(c => c.name == "Scene Look Lab Preview Camera"), "Preview camera cleaned up");
            Require(subject.gameObject.scene.isDirty == dirty, "Captures preserve scene dirty state");
            Require(EditorJsonUtility.ToJson(original) == originalJson, "Source material unchanged after renders");
            Require(!Resources.FindObjectsOfTypeAll<GameObject>().Any(go => go.name == "Scene Look Lab Outline"), "Outline shell objects cleaned up");
            Require(!Resources.FindObjectsOfTypeAll<Material>().Any(m => m.name == "Scene Look Lab Outline Material"), "Outline materials cleaned up");
            var outlineShader = Shader.Find("Hidden/SceneLookLab/Outline");
            Require(outlineShader != null && !ShaderUtil.ShaderHasError(outlineShader), "Outline shader compiles for actual rendering");
            byte[] originalImage = File.ReadAllBytes(Path.Combine(destination, "Original.png"));
            foreach (var name in LookPresets.Styles.Select(style => style + ".png"))
                Require(!originalImage.SequenceEqual(File.ReadAllBytes(Path.Combine(destination, name))), name + " differs from original render");
            profile.includedLayers = 1 << 7;
            profile.includeAlphaClipped = true;
            profile.sourceTexture = "_ArtistTexture";
            foreach (var style in LookPresets.Styles)
            {
                LookPresets.Apply(profile, style);
                Require(profile.includedLayers == (1 << 7) && profile.includeAlphaClipped && profile.sourceTexture == "_ArtistTexture",
                    style + " default preserves scope and source mapping");
            }
            LookPresets.Apply(profile, LookStyle.RimLight);
            var loaded = ScriptableObject.CreateInstance<LookProfile>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), loaded);
            Require(loaded.style == LookStyle.RimLight && loaded.accentColor == profile.accentColor && loaded.rimStrength == profile.rimStrength,
                "New preset settings survive serialization");
            JsonUtility.FromJsonOverwrite("{\"style\":3,\"bands\":4}", loaded);
            Require(loaded.style == LookStyle.CustomShader && loaded.bands == 4, "Version 0.1 custom-shader profiles retain their meaning");
            Object.DestroyImmediate(loaded);
            profile.includedLayers = ~0;
            profile.sourceTexture = "_BaseMap";
            profile.includeAlphaClipped = false;
            LookPresets.Apply(profile, LookStyle.OutlineToon);
            string transformJson = EditorJsonUtility.ToJson(subject.transform);
            try
            {
                using var transaction = new MaterialTransaction();
                transaction.Apply(new[] { subject }, profile);
                Require(subject.transform.childCount > 0, "Outline shell exists during transaction");
                throw new OperationCanceledException("Deliberate outline test failure");
            }
            catch (OperationCanceledException) { }
            Require(EditorJsonUtility.ToJson(subject.transform) == transformJson, "Outline exception restores original hierarchy");
            Require(!Resources.FindObjectsOfTypeAll<GameObject>().Any(go => go.name == "Scene Look Lab Outline"), "Outline exception cleans shell objects");
            using (var preview = new PreviewSession())
            {
                profile.outlineWidth = 0;
                preview.Capture(camera, profile, null, 960, 540);
                SaveImage(preview.Styled, "OutlineOff.png");
                Require(!File.ReadAllBytes(Path.Combine(destination, "OutlineOff.png")).SequenceEqual(
                    File.ReadAllBytes(Path.Combine(destination, "OutlineToon.png"))), "Outline width changes rendered pixels");
            }
            // Close-up plain shaderball lets reviewers compare brush boundaries to the references.
            var referenceMaterial = new Material(original);
            var cameraPosition = camera.transform.position;
            var cameraRotation = camera.transform.rotation;
            try
            {
                subject.sharedMaterial = referenceMaterial;
                referenceMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                referenceMaterial.SetColor("_BaseColor", new Color(0.02f, 0.7f, 0.8f));
                camera.transform.position = new Vector3(0, 2.1f, -4.5f);
                camera.transform.LookAt(subject.transform.position);
                using var preview = new PreviewSession();
                foreach (var style in new[] { LookStyle.SoftPattern, LookStyle.HardBrush, LookStyle.OutlineToon })
                {
                    LookPresets.Apply(profile, style);
                    preview.Capture(camera, profile, subject.transform, 640, 640);
                    SaveImage(preview.Styled, "Reference" + style + ".png");
                }
                LookPresets.Apply(profile, LookStyle.Clay);
                preview.Capture(camera, profile, subject.transform, 640, 640);
                SaveImage(preview.Styled, "ClayNoTexture.png");
                referenceMaterial.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                preview.Capture(camera, profile, subject.transform, 640, 640);
                SaveImage(preview.Styled, "ClayWithTexture.png");
                Require(!File.ReadAllBytes(Path.Combine(destination, "ClayNoTexture.png")).SequenceEqual(
                    File.ReadAllBytes(Path.Combine(destination, "ClayWithTexture.png"))), "Clay texture changes rendered pixels");
            }
            finally
            {
                subject.sharedMaterial = original;
                camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                Object.DestroyImmediate(referenceMaterial);
            }
            Object.DestroyImmediate(profile);
            ClayValidation.Run(camera, subject, original);
            Require(LookPresets.MenuStyles.Length == 11 && LookPresets.MenuLabels.Length == 11 && LookPresets.MenuStyles.Contains(LookStyle.StandardLit),
                "Look menu contains Standard Lit and nine stylized/diagnostic looks plus Custom Shader");
            Require(!LookPresets.MenuStyles.Contains(LookStyle.SoftPattern) && !LookPresets.MenuStyles.Contains(LookStyle.HardBrush),
                "Retired brush looks are absent from Look menu");
            Require(LookPresets.MenuStyles.Contains(LookStyle.Clay) && (int)LookStyle.SoftPattern == 10 && (int)LookStyle.HardBrush == 11,
                "Clay remains available and legacy shader IDs remain stable");
            HatchValidation.Run(camera, null);
            StandardLitValidation.Run(original);
            OcclusionValidation.Run();
            ApplyValidation.Run(subject, original);
            File.WriteAllText(Path.Combine(destination, "result.txt"), "PASS: preview regressions and apply destination, asset preservation, runtime shader references, Undo/Redo, outline cleanup and filename collision checks.\n");
            Debug.Log("SCENE_LOOK_LAB_VALIDATION_PASSED");
            RestoreCompatibility();
            EditorApplication.Exit(0);
        }

        static void SaveImage(RenderTexture target, string filename)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
                var pixels = texture.GetPixels32();
                var first = pixels[0];
                Require(pixels.Any(p => Math.Abs(p.r - first.r) + Math.Abs(p.g - first.g) + Math.Abs(p.b - first.b) > 30), filename + " contains scene content");
                File.WriteAllBytes(Path.Combine(destination, filename), texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
        }

        static void Fail(Exception exception)
        {
            RestoreCompatibility();
            Debug.LogException(exception);
            if (!string.IsNullOrEmpty(destination)) File.WriteAllText(Path.Combine(destination, "result.txt"), exception.ToString());
            EditorApplication.Exit(1);
        }
    }
}
