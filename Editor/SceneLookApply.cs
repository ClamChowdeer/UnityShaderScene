using System;
using System.Collections.Generic;
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
    internal static class SceneLookApply
    {
        sealed class Change
        {
            public Renderer renderer;
            public Material[] before, after;
        }

        internal static string ProjectFolder(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath)) return null;
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string selected = Path.GetFullPath(absolutePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!selected.Equals(assets, StringComparison.OrdinalIgnoreCase) &&
                !selected.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a folder inside this project's Assets folder.");
            // Reject directory links so a folder inside Assets cannot redirect asset writes elsewhere.
            for (var dir = new DirectoryInfo(selected); dir != null; dir = dir.Parent)
            {
                if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Please choose a regular folder, not a linked folder.");
                if (dir.FullName.Equals(assets, StringComparison.OrdinalIgnoreCase)) break;
            }
            string relative = "Assets" + selected.Substring(assets.Length).Replace('\\', '/');
            AssetDatabase.Refresh();
            if (!AssetDatabase.IsValidFolder(relative)) throw new InvalidOperationException("The selected folder could not be imported.");
            return relative;
        }

        internal static string Apply(LookProfile profile, Transform scope, string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                throw new InvalidOperationException("Apply requires Edit Mode and URP.");
            if (string.IsNullOrEmpty(folder) || !(folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Choose a destination inside Assets.");
            folder = ProjectFolder(Path.GetFullPath(folder));
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Exit Prefab Mode first. Apply changes scene instances, not prefab assets.");

            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var originals = renderers.ToDictionary(r => r, r => r.sharedMaterials);
            var changes = new List<Change>();
            var copies = new Dictionary<Material, Material>();
            var assets = new List<string>();
            var temporary = new List<Material>();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply Scene Look");
            int slots = 0, skipped = 0;
            try
            {
                // Use exactly the preview's conversion and eligibility rules, then restore it
                // before recording Undo or making any persistent assignments.
                using (var transaction = new MaterialTransaction())
                {
                    transaction.Apply(renderers, profile, scope, false);
                    skipped = transaction.SkippedSlots;
                    foreach (var renderer in renderers)
                    {
                        var before = originals[renderer];
                        var after = renderer.sharedMaterials;
                        if (before.SequenceEqual(after)) continue;
                        if (renderer.GetComponentInChildren<AppliedLookOutline>(true) != null)
                            throw new InvalidOperationException("This selection already has an applied outline. Undo that application before applying another look.");
                        for (int i = 0; i < after.Length; i++)
                        {
                            if (after[i] == before[i]) continue;
                            if (!copies.TryGetValue(after[i], out var copy))
                            {
                                copy = new Material(after[i]) { hideFlags = HideFlags.None, name = before[i].name + " " + profile.style };
                                temporary.Add(copy);
                                copies.Add(after[i], copy);
                            }
                            after[i] = copy;
                            slots++;
                        }
                        changes.Add(new Change { renderer = renderer, before = before, after = after });
                    }
                }
                if (changes.Count == 0) return "No eligible material slots were found. Nothing was changed.";
                foreach (var material in temporary.ToArray()) SaveMaterial(material, folder, assets);
                foreach (var change in changes)
                {
                    Undo.RecordObject(change.renderer, "Apply Scene Look");
                    change.renderer.sharedMaterials = change.after;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(change.renderer);
                    if (profile.style == LookStyle.OutlineToon && profile.outlineWidth > 0)
                        AddOutline(change, profile, folder, assets, temporary);
                    EditorSceneManager.MarkSceneDirty(change.renderer.gameObject.scene);
                }
                AssetDatabase.SaveAssets();
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                return $"Applied {profile.style} to {slots} slots on {changes.Count} objects; {skipped} slots skipped.\n" +
                    $"Created {assets.Count} materials in {folder}. Save your scene to keep the assignments.\n" +
                    "Undo restores scene assignments and removes outline objects. Generated material files remain for reuse.";
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                foreach (string path in assets) AssetDatabase.DeleteAsset(path);
                throw;
            }
            finally
            {
                foreach (var material in temporary)
                    if (material != null && !EditorUtility.IsPersistent(material)) Object.DestroyImmediate(material);
            }
        }

        static void SaveMaterial(Material material, string folder, List<string> paths)
        {
            string name = material.name;
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length > 70) name = name.Substring(0, 70);
            if (string.IsNullOrEmpty(name)) name = "Look Material";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".mat");
            // Track only a path reserved by this operation, never an existing asset.
            paths.Add(path);
            AssetDatabase.CreateAsset(material, path);
            if (!EditorUtility.IsPersistent(material)) throw new IOException("Could not save material: " + path);
        }

        static void AddOutline(Change change, LookProfile profile, string folder, List<string> assets, List<Material> temporary)
        {
            var source = change.renderer;
            Mesh mesh = source is SkinnedMeshRenderer skin ? skin.sharedMesh : source.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return;
            var shader = Shader.Find("Hidden/SceneLookLab/Outline");
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("The outline shader is unavailable.");
            var materials = new Material[change.after.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                var material = new Material(shader) { name = source.name + " Outline " + i };
                temporary.Add(material);
                bool visible = change.before[i] != change.after[i];
                material.SetFloat("_Visible", visible ? 1 : 0);
                material.SetFloat("_OutlineWidth", profile.outlineWidth);
                material.SetColor("_OutlineColor", profile.outlineColor);
                if (visible)
                {
                    var surface = change.after[i];
                    material.SetTexture("_BaseMap", surface.GetTexture("_BaseMap"));
                    material.SetTextureScale("_BaseMap", surface.GetTextureScale("_BaseMap"));
                    material.SetTextureOffset("_BaseMap", surface.GetTextureOffset("_BaseMap"));
                    material.SetColor("_BaseColor", surface.GetColor("_BaseColor"));
                    material.SetFloat("_AlphaClip", surface.GetFloat("_AlphaClip"));
                    material.SetFloat("_Cutoff", surface.GetFloat("_Cutoff"));
                }
                SaveMaterial(material, folder, assets);
                materials[i] = material;
            }
            var go = new GameObject("Scene Look Lab Applied Outline") { layer = source.gameObject.layer };
            Undo.RegisterCreatedObjectUndo(go, "Create look outline");
            Undo.SetTransformParent(go.transform, source.transform, "Parent look outline");
            Undo.RecordObject(go.transform, "Position look outline");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            Renderer shell;
            if (source is SkinnedMeshRenderer original)
            {
                var outline = Undo.AddComponent<SkinnedMeshRenderer>(go);
                Undo.RecordObject(outline, "Configure look outline");
                outline.sharedMesh = mesh;
                outline.bones = original.bones;
                outline.rootBone = original.rootBone;
                outline.localBounds = original.localBounds;
                outline.quality = original.quality;
                outline.updateWhenOffscreen = original.updateWhenOffscreen;
                for (int i = 0; i < mesh.blendShapeCount; i++) outline.SetBlendShapeWeight(i, original.GetBlendShapeWeight(i));
                shell = outline;
            }
            else
            {
                var filter = Undo.AddComponent<MeshFilter>(go);
                Undo.RecordObject(filter, "Configure look mesh");
                filter.sharedMesh = mesh;
                shell = Undo.AddComponent<MeshRenderer>(go);
                Undo.RecordObject(shell, "Configure look outline");
            }
            shell.sharedMaterials = materials;
            shell.shadowCastingMode = ShadowCastingMode.Off;
            shell.receiveShadows = false;
            shell.lightProbeUsage = LightProbeUsage.Off;
            shell.reflectionProbeUsage = ReflectionProbeUsage.Off;
            shell.renderingLayerMask = source.renderingLayerMask;
            var marker = Undo.AddComponent<AppliedLookOutline>(go);
            Undo.RecordObject(marker, "Configure outline follower");
            marker.source = source;
            marker.shell = shell;
            // Keep outline visibility in step with its source's LOD selection.
            var lodGroup = source.GetComponentInParent<LODGroup>();
            if (lodGroup != null)
            {
                var lods = lodGroup.GetLODs();
                bool changed = false;
                for (int i = 0; i < lods.Length; i++)
                    if (lods[i].renderers.Contains(source))
                    {
                        lods[i].renderers = lods[i].renderers.Concat(new[] { shell }).ToArray();
                        changed = true;
                    }
                if (changed)
                {
                    Undo.RecordObject(lodGroup, "Add outline to LOD");
                    lodGroup.SetLODs(lods);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(lodGroup);
                }
            }
        }
    }
}
