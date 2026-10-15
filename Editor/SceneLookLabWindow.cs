using System;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace SceneLookLab
{
    public sealed class SceneLookLabWindow : EditorWindow
    {
        [SerializeField] Camera sceneCamera;
        [SerializeField] bool useSceneView = true;
        [SerializeField] Transform scope;
        [SerializeField] LookProfile profileAsset;
        [SerializeField] int viewMode = 2;
        [SerializeField] float wipe = 0.5f;
        [SerializeField] bool autoRefresh;
        LookProfile working;
        PreviewSession session;
        Vector2 scroll;
        string error;
        bool stale = true;
        double nextRefresh;
        static readonly string[] ViewNames = { "Original", "Look", "Side by side", "Wipe" };

        [MenuItem("Tools/Scene Look Lab")]
        public static void Open() => GetWindow<SceneLookLabWindow>("Scene Look Lab");

        public static void OpenForCamera(Camera camera)
        {
            var window = GetWindow<SceneLookLabWindow>("Scene Look Lab");
            window.sceneCamera = camera;
            window.useSceneView = false;
            window.Capture();
        }

        void OnEnable()
        {
            minSize = new Vector2(680, 520);
            working = profileAsset != null ? profileAsset.Copy() : CreateInstance<LookProfile>();
            working.hideFlags = HideFlags.HideAndDontSave;
            session = new PreviewSession();
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += CleanUp;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= CleanUp;
            CleanUp();
        }

        void CleanUp()
        {
            session?.Dispose();
            session = null;
            if (working != null) DestroyImmediate(working);
        }

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            autoRefresh = false;
            session?.Dispose();
            session = new PreviewSession();
            stale = true;
            Repaint();
        }

        void Tick()
        {
            if (!autoRefresh || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + 0.5;
            Capture();
        }

        void Capture()
        {
            try
            {
                var source = useSceneView ? SceneView.lastActiveSceneView?.camera : sceneCamera;
                session ??= new PreviewSession();
                session.Capture(source, working, scope);
                error = null;
                stale = false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                autoRefresh = false;
                stale = true;
            }
            Repaint();
        }

        void OnGUI()
        {
            if (working == null) return;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Scene Look Lab", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Preview a whole scene. Original assignments are restored after every capture.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            useSceneView = EditorGUILayout.Toggle("Follow Scene view", useSceneView);
            if (!useSceneView) sceneCamera = (Camera)EditorGUILayout.ObjectField("Camera", sceneCamera, typeof(Camera), true);
            scope = (Transform)EditorGUILayout.ObjectField(new GUIContent("Optional hierarchy root", "Empty means all loaded scenes. Only descendants of this root are overridden."), scope, typeof(Transform), true);
            var layers = InternalEditorUtility.layers;
            int displayMask = 0;
            for (int i = 0; i < layers.Length; i++)
                if ((working.includedLayers.value & (1 << LayerMask.NameToLayer(layers[i]))) != 0) displayMask |= 1 << i;
            int selectedMask = EditorGUILayout.MaskField("Include layers", displayMask, layers);
            if (selectedMask != displayMask)
            {
                int realMask = selectedMask == -1 ? ~0 : 0;
                if (selectedMask != -1)
                    for (int i = 0; i < layers.Length; i++)
                        if ((selectedMask & (1 << i)) != 0) realMask |= 1 << LayerMask.NameToLayer(layers[i]);
                working.includedLayers = realMask;
            }
            int currentLook = Array.IndexOf(LookPresets.MenuStyles, working.style);
            int selectedIndex = EditorGUILayout.Popup("Look", currentLook, LookPresets.MenuLabels);
            var selectedLook = selectedIndex >= 0 ? LookPresets.MenuStyles[selectedIndex] : working.style;
            if (currentLook < 0)
                EditorGUILayout.HelpBox("This saved profile uses a retired look. Its settings are preserved; select another Look to replace it.", MessageType.Info);
            if (selectedLook != working.style)
            {
                LookPresets.Apply(working, selectedLook);
                profileAsset = null;
            }
            EditorGUILayout.LabelField(LookPresets.Describe(working.style), EditorStyles.wordWrappedMiniLabel);
            if (working.style != LookStyle.WorldNormals)
                working.tint = EditorGUILayout.ColorField(working.style == LookStyle.Clay ? "Clay colour" :
                    working.style == LookStyle.InkHatching ? "Paper colour" : "Colour tint", working.tint);
            if (working.style == LookStyle.Cel || working.style == LookStyle.SoftToon ||
                working.style == LookStyle.OutlineToon || working.style == LookStyle.SoftPattern || working.style == LookStyle.HardBrush)
            {
                working.bands = EditorGUILayout.IntSlider("Light bands", working.bands, 2, 6);
                working.shadowColor = EditorGUILayout.ColorField("Shadow tint", working.shadowColor);
            }
            if (working.style == LookStyle.SoftToon)
                working.softness = EditorGUILayout.Slider("Band softness", working.softness, 0.01f, 0.5f);
            if (working.style == LookStyle.Clay)
            {
                working.clayDetail = EditorGUILayout.Slider("Surface detail", working.clayDetail, 0, 1);
                working.clayDetailSize = EditorGUILayout.Slider("Detail size", working.clayDetailSize, 0.02f, 0.5f);
                working.clayUnevenness = EditorGUILayout.Slider("Handcrafted unevenness", working.clayUnevenness, 0, 1);
                working.clayShine = EditorGUILayout.Slider("Soft shine", working.clayShine, 0, 1);
                working.clayTextureStrength = EditorGUILayout.Slider("Original texture strength", working.clayTextureStrength, 0, 1);
            }
            if (working.style == LookStyle.OutlineToon)
            {
                working.outlineWidth = EditorGUILayout.Slider("Outline width (pixels)", working.outlineWidth, 0, 8);
                working.outlineColor = EditorGUILayout.ColorField("Outline colour", working.outlineColor);
            }
            if (working.style == LookStyle.SoftPattern || working.style == LookStyle.HardBrush)
            {
                working.patternScale = EditorGUILayout.Slider("Brush frequency", working.patternScale, 2, 40);
                working.brushStrength = EditorGUILayout.Slider("Brush strength", working.brushStrength, 0, 1);
                if (working.style == LookStyle.SoftPattern)
                    working.softness = EditorGUILayout.Slider("Brush softness", working.softness, 0.01f, 0.5f);
            }
            if (working.style == LookStyle.InkHatching || working.style == LookStyle.ProceduralChecker)
            {
                bool ink = working.style == LookStyle.InkHatching;
                if (ink)
                    working.hatchRotation = EditorGUILayout.Slider(new GUIContent("Hatching rotation (°)", "Rotate both hatch directions in screen space. 0 keeps the original diagonals; 45 aligns them horizontally/vertically."), working.hatchRotation, 0, 180);
                working.patternScale = EditorGUILayout.Slider(ink ? "Hatch spacing (pixels)" : "UV checker frequency", working.patternScale, 2, 40);
                working.accentColor = EditorGUILayout.ColorField(ink ? "Ink colour" : "Alternate colour", working.accentColor);
            }
            if (working.style == LookStyle.RimLight)
            {
                working.accentColor = EditorGUILayout.ColorField("Rim colour", working.accentColor);
                working.rimPower = EditorGUILayout.Slider("Rim falloff", working.rimPower, 0.5f, 8);
                working.rimStrength = EditorGUILayout.Slider("Rim strength", working.rimStrength, 0, 3);
            }
            if (working.style == LookStyle.CustomShader)
            {
                working.customShader = (Shader)EditorGUILayout.ObjectField("Replacement shader", working.customShader, typeof(Shader), false);
                working.sourceTexture = EditorGUILayout.TextField("Source texture property", working.sourceTexture);
                working.sourceColor = EditorGUILayout.TextField("Source colour property", working.sourceColor);
                working.targetTexture = EditorGUILayout.TextField("Target texture property", working.targetTexture);
                working.targetColor = EditorGUILayout.TextField("Target colour property", working.targetColor);
                EditorGUILayout.HelpBox("Custom mode maps base texture, tiling and colour. Other matching properties are copied, but shader keywords, multipass effects and custom lighting may need shader-specific setup.", MessageType.Info);
            }
            working.includeAlphaClipped = EditorGUILayout.Toggle("Include cutout meshes", working.includeAlphaClipped);
            if (EditorGUI.EndChangeCheck()) stale = true;

            EditorGUILayout.BeginHorizontal();
            var picked = (LookProfile)EditorGUILayout.ObjectField("Saved profile", profileAsset, typeof(LookProfile), false);
            if (picked != profileAsset)
            {
                profileAsset = picked;
                if (profileAsset != null)
                {
                    DestroyImmediate(working);
                    working = profileAsset.Copy();
                    stale = true;
                }
            }
            if (GUILayout.Button("Save as new…", GUILayout.Width(110))) SaveProfile();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Capture comparison", GUILayout.Height(28))) Capture();
                autoRefresh = GUILayout.Toggle(autoRefresh, "Auto refresh (2 fps)", "Button", GUILayout.Height(28));
            }
            using (new EditorGUI.DisabledScope(session?.Styled == null || stale))
                if (GUILayout.Button("Export look PNG…", GUILayout.Height(28))) Export();
            EditorGUILayout.EndHorizontal();
            viewMode = GUILayout.Toolbar(viewMode, ViewNames);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
                if (GUILayout.Button("Apply to Scene…", GUILayout.Height(28))) ApplyToScene();
            if (viewMode == 3) wipe = EditorGUILayout.Slider("Original / Look", wipe, 0, 1);
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            else if (stale) EditorGUILayout.HelpBox("Capture to update the preview after changes to the look, camera or scene.", MessageType.Info);
            if (session?.Styled != null)
            {
                if (viewMode == 2)
                {
                    EditorGUILayout.BeginHorizontal();
                    DrawImage(session.Original, "Original");
                    DrawImage(session.Styled, "Look preview");
                    EditorGUILayout.EndHorizontal();
                }
                else if (viewMode == 3)
                {
                    var rect = GUILayoutUtility.GetAspectRect(16f / 9f, GUILayout.MinHeight(180));
                    GUI.DrawTexture(rect, session.Styled, ScaleMode.StretchToFill, false);
                    var clip = new Rect(rect.x, rect.y, rect.width * wipe, rect.height);
                    GUI.BeginGroup(clip);
                    GUI.DrawTexture(new Rect(0, 0, rect.width, rect.height), session.Original, ScaleMode.StretchToFill, false);
                    GUI.EndGroup();
                    EditorGUI.DrawRect(new Rect(rect.x + rect.width * wipe, rect.y, 2, rect.height), Color.white);
                }
                else DrawImage(viewMode == 0 ? session.Original : session.Styled, ViewNames[viewMode]);
                EditorGUILayout.LabelField(session.Report, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Prototype: URP · Edit Mode · Mesh/Skinned Mesh. Terrain, transparent surfaces, particles, sprites and renderers with property blocks keep their original materials. Preview excludes overlay cameras and screen-space UI. Built-in looks use base texture/colour and main-light shading; normal maps, additional lights, baked GI and original vertex effects are not reproduced.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        static void DrawImage(Texture texture, string label)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetAspectRect(16f / 9f, GUILayout.MinHeight(130));
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
            EditorGUILayout.EndVertical();
        }

        void ApplyToScene()
        {
            autoRefresh = false;
            try
            {
                string selected = EditorUtility.OpenFolderPanel("Save new look materials in…", Application.dataPath, "");
                if (string.IsNullOrEmpty(selected)) return;
                string folder = SceneLookApply.ProjectFolder(selected);
                string target = scope != null ? "the hierarchy under '" + scope.name + "'" : "ALL loaded scenes";
                if (!EditorUtility.DisplayDialog("Apply " + working.style + " to scene?",
                    "Apply the CURRENT settings to eligible objects in " + target + ". Layer and cutout filters are respected.\n\n" +
                    "New materials: " + folder + "\n\nOriginal material and prefab assets will not be changed. " +
                    "Existing destination files will not be overwritten. Scenes are not saved automatically. " +
                    "Undo restores scene assignments; generated material files remain. Applying again converts the currently assigned materials.",
                    "Create materials and apply", "Cancel")) return;
                string report = SceneLookApply.Apply(working, scope, folder);
                stale = true;
                error = null;
                EditorUtility.DisplayDialog("Scene Look Lab", report, "OK");
            }
            catch (Exception exception) { error = exception.Message; }
        }

        void SaveProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save Look Profile", working.style + " Look", "asset", "Save this look as a new profile.");
            if (string.IsNullOrEmpty(path)) return;
            var asset = Instantiate(working);
            asset.hideFlags = HideFlags.None;
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(path));
            AssetDatabase.SaveAssets();
            profileAsset = asset;
        }

        void Export()
        {
            string path = EditorUtility.SaveFilePanel("Export Look Preview", "", "scene-look.png", "png");
            if (string.IsNullOrEmpty(path)) return;
            var previous = RenderTexture.active;
            Texture2D image = null;
            try
            {
                RenderTexture.active = session.Styled;
                image = new Texture2D(session.Styled.width, session.Styled.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            catch (Exception exception) { error = exception.Message; }
            finally { RenderTexture.active = previous; if (image != null) DestroyImmediate(image); }
        }
    }
}
