using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    internal static class ApplyValidation
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Apply test failed: " + message);
            Debug.Log("PASS: " + message);
        }

        internal static void Run(MeshRenderer subject, Material original)
        {
            string folder = "Assets/LabDemo/Applied";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/LabDemo", "Applied");
            string originalJson = EditorJsonUtility.ToJson(original);
            Check(SceneLookApply.ProjectFolder("") == null, "Cancelled folder makes no selection");
            Check(SceneLookApply.ProjectFolder(Path.GetFullPath(folder)) == folder, "Chosen folder resolves inside Assets");
            bool rejected = false;
            try { SceneLookApply.ProjectFolder(Path.GetFullPath("Packages")); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Outside-Assets destination rejected");
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            foreach (var style in LookPresets.Styles)
            {
                LookPresets.Apply(profile, style);
                if (style == LookStyle.InkHatching) profile.hatchRotation = 37;
                int count = AssetDatabase.FindAssets("t:Material", new[] { folder }).Length;
                SceneLookApply.Apply(profile, subject.transform, folder);
                var material = subject.sharedMaterial;
                if (style == LookStyle.InkHatching)
                    Check(material.GetFloat("_HatchRotation") == 37, "Applied ink material retains selected hatching rotation");
                Check(material != original && AssetDatabase.GetAssetPath(material).StartsWith(folder + "/"), style + " material saved to chosen destination");
                Check(material.hideFlags == HideFlags.None && material.shader.isSupported, style + " material is persistent and supported");
                Check(material.GetTexture("_BaseMap") == original.GetTexture("_BaseMap"), style + " preserves base texture");
                if (style == LookStyle.Clay)
                    Check(material.GetFloat("_ClayDetail") == profile.clayDetail && material.GetFloat("_ClayDetailSize") == profile.clayDetailSize &&
                        material.GetFloat("_ClayUnevenness") == profile.clayUnevenness && material.GetFloat("_ClayShine") == profile.clayShine &&
                        material.GetFloat("_ClayTextureStrength") == profile.clayTextureStrength, "Applied Clay saves all handcrafted settings");
                Check(style == LookStyle.StandardLit ? material.shader.name == "Universal Render Pipeline/Lit" :
                    AssetDatabase.GetAssetPath(material.shader).Contains("/Runtime/"), style + " uses runtime shader asset");
                if (style == LookStyle.StandardLit)
                    Check(material.GetFloat("_Metallic") == 0 && material.GetFloat("_Smoothness") == 0.5f &&
                        material.GetTextureScale("_BaseMap") == original.GetTextureScale("_BaseMap") &&
                        material.GetTextureOffset("_BaseMap") == original.GetTextureOffset("_BaseMap"),
                        "Standard Lit uses Unity defaults and preserves base UV mapping");
                if (style == LookStyle.OutlineToon)
                {
                    var outline = subject.GetComponentInChildren<AppliedLookOutline>();
                    Check(outline != null && outline.source == subject && outline.shell.sharedMaterial != null, "Outline has saved runtime follower");
                    Check(EditorUtility.IsPersistent(outline.shell.sharedMaterial), "Outline material is saved");
                }
                Undo.PerformUndo();
                Check(subject.sharedMaterial == original, style + " Undo restores original assignment");
                Check(subject.GetComponentInChildren<AppliedLookOutline>() == null, style + " Undo removes outline objects");
                Check(AssetDatabase.FindAssets("t:Material", new[] { folder }).Length > count, style + " Undo retains reusable material assets");
                Undo.PerformRedo();
                Check(subject.sharedMaterial == material, style + " Redo restores saved material");
                if (style == LookStyle.OutlineToon)
                    Check(subject.GetComponentInChildren<AppliedLookOutline>()?.source == subject, "Redo restores configured outline");
                Undo.PerformUndo();
            }
            LookPresets.Apply(profile, LookStyle.Clay);
            SceneLookApply.Apply(profile, subject.transform, folder);
            string firstPath = AssetDatabase.GetAssetPath(subject.sharedMaterial);
            Undo.PerformUndo();
            SceneLookApply.Apply(profile, subject.transform, folder);
            Check(AssetDatabase.GetAssetPath(subject.sharedMaterial) != firstPath && AssetDatabase.LoadAssetAtPath<Material>(firstPath) != null,
                "Repeated apply uses unique paths without overwriting");
            Undo.PerformUndo();
            profile.includedLayers = 0;
            int before = AssetDatabase.FindAssets("t:Material", new[] { folder }).Length;
            SceneLookApply.Apply(profile, subject.transform, folder);
            Check(subject.sharedMaterial == original && AssetDatabase.FindAssets("t:Material", new[] { folder }).Length == before,
                "Empty eligibility does not create assets or assign materials");
            Check(EditorJsonUtility.ToJson(original) == originalJson, "Original material asset stays untouched");
            profile.includedLayers = ~0;
            LookPresets.Apply(profile, LookStyle.OutlineToon);
            CheckSkinnedAndPrefab(profile, original, subject.GetComponent<MeshFilter>().sharedMesh, folder);
            SceneLookApply.Apply(profile, subject.transform, folder);
            var savedOutline = subject.GetComponentInChildren<AppliedLookOutline>();
            using (var transaction = new MaterialTransaction())
            {
                transaction.Apply(new[] { subject }, profile);
                Check(!savedOutline.shell.enabled, "Preview temporarily suppresses existing applied outline");
            }
            Check(savedOutline.shell.enabled, "Preview restores existing applied outline");
            int assetsBeforeFailure = AssetDatabase.FindAssets("t:Material", new[] { folder }).Length;
            var assignedBeforeFailure = subject.sharedMaterial;
            bool repeatRejected = false;
            try { SceneLookApply.Apply(profile, subject.transform, folder); }
            catch (InvalidOperationException) { repeatRejected = true; }
            Check(repeatRejected && subject.sharedMaterial == assignedBeforeFailure && savedOutline != null &&
                AssetDatabase.FindAssets("t:Material", new[] { folder }).Length == assetsBeforeFailure,
                "Rejected repeated outline application preserves scene and assets");
            string savedPath = AssetDatabase.GetAssetPath(subject.sharedMaterial);
            string scenePath = "Assets/LabDemo/AppliedScene.unity";
            Check(EditorSceneManager.SaveScene(subject.gameObject.scene, scenePath), "Applied scene saves successfully");
            EditorSceneManager.OpenScene(scenePath);
            var restored = Object.FindObjectsByType<AppliedLookOutline>(FindObjectsSortMode.None).Single();
            Check(restored.source != null && restored.shell != null && AssetDatabase.GetAssetPath(restored.source.sharedMaterial) == savedPath,
                "Saved scene reload preserves materials and outline links");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = Path.GetFullPath("../Validation-0.8/Player/SceneLookLab.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.DetailedBuildReport
            });
            Check(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, "Standalone player builds successfully");
            Check(report.packedAssets.SelectMany(p => p.contents).Any(c => c.sourceAssetPath.EndsWith("LookSurface.shader")),
                "Player contains applied surface shader");
            Check(report.packedAssets.SelectMany(p => p.contents).Any(c => c.sourceAssetPath.EndsWith("LookOutline.shader")),
                "Player contains outline shader");
            Object.DestroyImmediate(profile);
        }

        static void CheckSkinnedAndPrefab(LookProfile profile, Material original, Mesh sourceMesh, string folder)
        {
            var go = new GameObject("Skin test");
            var mesh = Object.Instantiate(sourceMesh);
            var deltas = new Vector3[mesh.vertexCount];
            mesh.AddBlendShapeFrame("Test", 100, deltas, deltas, deltas);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.sharedMaterial = original;
            skin.bones = new[] { go.transform };
            skin.rootBone = go.transform;
            var lod = go.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.01f, new Renderer[] { skin }) });
            SceneLookApply.Apply(profile, go.transform, folder);
            var marker = go.GetComponentInChildren<AppliedLookOutline>();
            var shell = (SkinnedMeshRenderer)marker.shell;
            Check(shell.sharedMesh == mesh && shell.bones.SequenceEqual(skin.bones), "Applied skinned outline shares live mesh and bones");
            skin.SetBlendShapeWeight(0, 37);
            marker.SendMessage("LateUpdate");
            Check(shell.GetBlendShapeWeight(0) == 37, "Runtime follower updates outline blend shapes");
            Check(lod.GetLODs()[0].renderers.Contains(shell), "Outline participates in source LOD");
            Undo.PerformUndo();
            Check(skin.sharedMaterial == original && lod.GetLODs()[0].renderers.Length == 1 && go.GetComponentInChildren<AppliedLookOutline>() == null,
                "Undo restores skinned source and LOD membership");
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(mesh);

            var prefabSource = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefabSource.GetComponent<MeshRenderer>().sharedMaterial = original;
            string prefabPath = "Assets/LabDemo/ApplyTest.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
            Object.DestroyImmediate(prefabSource);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var renderer = instance.GetComponent<MeshRenderer>();
            SceneLookApply.Apply(profile, instance.transform, folder);
            Check(renderer.sharedMaterial != original && prefab.GetComponent<MeshRenderer>().sharedMaterial == original,
                "Apply changes prefab scene instance without editing prefab asset");
            Check(PrefabUtility.GetPropertyModifications(instance).Any(m => m.propertyPath.Contains("m_Materials")),
                "Applied prefab material is recorded as an instance override");
            Undo.PerformUndo();
            Check(renderer.sharedMaterial == original, "Undo restores prefab instance materials");
            Object.DestroyImmediate(instance);
        }
    }
}
