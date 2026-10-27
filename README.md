# Scene Look Lab — prototype 0.8

A Unity Editor package for comparing and applying scene surface looks. Targets Unity 6000.0 and URP 17.0.4. Preview remains temporary; Apply creates saved materials, and Outline Toon adds runtime outline objects.

## Install from Git (recommended for shared projects)

Requires Unity 6000.0 and Universal Render Pipeline (tested with Unity 6000.0.68f1 and URP 17.0.4). Built-in Render Pipeline and HDRP are not supported. Git must be installed and available to Unity; restart Unity/Unity Hub after installing Git. Private repositories also require GitHub access and Git authentication on each teammate's machine; do not put credentials in the URL.

1. Open **Window → Package Management → Package Manager**.
2. Select **+ → Install package from Git URL** (or **Add package from git URL**).
3. Paste the version-pinned URL:

   ```text
   https://github.com/ClamChowdeer/UnityShaderScene.git#v0.8.0
   ```

4. Wait for import and compilation, then open **Tools → Scene Look Lab**.

Commit your game project's `Packages/manifest.json` and `Packages/packages-lock.json` to share the installation. The tool's source stays in this repository; each teammate downloads the pinned version. Unity resolves the declared URP dependency, so review package changes and test on a copied scene before adopting it in another project.

### Moving from a local installation

Replace the existing local package dependency with this Git URL; do not install a duplicate under Assets. If the package is embedded in the project's Packages folder, back up and remove that embedded copy before installing from Git, since embedded packages take precedence. Keep generated materials and profiles. All source `.meta` GUIDs are retained from the local v0.7.0 package so references can survive the switch; verify your scenes before saving or committing.

The URL's version tag intentionally pins a release. Future updates should use a new version tag; don't move existing release tags. No `.unitypackage` file is needed.

## Install locally (alternative)

In Unity, open **Window → Package Management → Package Manager**. Use **+ → Install package from disk** and select this folder's `package.json` (some Editor versions label this action “Add package from disk”). Unity records a local package reference in the project's Packages manifest; committing or pushing is a separate action.

Open **Tools → Scene Look Lab**. Arrange the Scene view and leave **Follow Scene view** enabled, then click **Capture comparison**. Alternatively, disable that option and choose a scene Camera. Captures use a 1280 × 720 frame.

Choose a style directly from **Look**, then click **Capture comparison**. This is the only style selector. Switching styles loads sensible settings while preserving your layer filters, hierarchy scope, source property mapping and cutout choice. Adjust the controls below it or select **Custom Shader** to use your own shader.

| Default | Appearance and controls |
| --- | --- |
| Standard Lit | Actual Unity URP Lit; base texture, colour and UV mapping, default metallic 0 and smoothness 0.5 |
| Cel | Hard light bands; texture tint, band count and shadow colour |
| Soft Toon | Softened bands; adds a band-softness control |
| Ink Hatching | Paper and ink colours with lighting-driven crosshatching; pixel spacing and rotation controls |
| Clay | Handcrafted grain, pores, shallow dents and soft waxy highlights; adjustable texture strength and colour tint |
| Textured Unlit | Original base textures and colours without surface lighting |
| Procedural Checker | Generated UV checker; two colours and UV frequency |
| Rim Light | Dark textured surfaces with coloured silhouette rims; falloff and strength |
| World Normals | Diagnostic mesh normals as RGB; not a normal-map effect |
| Outline Toon | Textured toon bands with a geometry silhouette outline; pixel width and colour |

All ten looks are available without third-party shaders. Standard Lit uses the installed URP Lit shader, not a stylized approximation. It preserves current base textures, colours, UVs and eligible cutouts, but starts other properties from Unity defaults; normal, metallic, emission and other auxiliary maps are not converted. It is not Restore Original: previous material assignments and previously multiplied colour tints are not recovered. Preview and Apply share this conversion. Existing applied outlines must still be undone before applying another look to those objects.

Checker shading requires UVs; hatching uses screen coordinates and changes with framing. Soft Pattern and Hard Brush have been removed from the Look menu; their shader IDs remain supported for previously saved materials and profiles. Rim Light does not add bloom. The library does not add bloom, motion blur, depth of field, ambient occlusion, parallax, transparency or particles.

Outline Toon uses expanded back-face geometry and works best on closed meshes. It outlines silhouettes, not internal creases or texture edges. Open meshes, split normals, unusual winding and negative scales can require further tuning. Preview uses temporary baked poses for skinned meshes and removes its shells after capture. Apply instead saves outline children, sharing the source mesh/bones and following blend shapes at runtime. Existing LOD membership is extended to the outline. Dynamic mesh replacement and custom vertex animation need additional integration.

Use Original, Look, Side by side, or Wipe to compare. **Auto refresh** captures at most twice a second; on large scenes use manual captures. Move the camera through Unity's Scene view, not the preview image.

Use **Include layers** or an **Optional hierarchy root** to limit the affected geometry. All loaded scenes remain visible; only eligible geometry is overridden. **Save as new** stores a Look Profile asset in a location you choose. Selecting a saved profile copies its settings into the window; editing those settings never silently overwrites the saved profile. **Export look PNG** saves the styled image.

## Hatching direction

Select **Ink Hatching** and adjust **Hatching rotation (°)** from 0 to 180. Both perpendicular line sets rotate together in screen space: 0 retains the original diagonals, 45 aligns them horizontally/vertically, and 90 swaps which direction is the primary hatch. A full 180 returns to the initial orientation. Spacing and ink/paper colours remain independent. The setting is stored in saved profiles and generated materials and works in gameplay. Existing profiles/materials default to zero, preserving their previous direction. Hatching remains camera/screen-oriented, not attached to the model's surface.

## Handcrafted Clay

Clay layers procedural grain, small pores and subtle ridge marks over a softer, uneven surface with broad highlights. It keeps material colours; the default white tint does not force everything brown.

- **Surface detail:** fine bumps, pores and subtle ridge marks; zero removes this fine layer.
- **Detail size:** model-local size of the grain. Adjust for the mesh's authored scale.
- **Handcrafted unevenness:** broader shading dents, independent of fine detail. This does not displace vertices or change silhouettes.
- **Soft shine:** broad, restrained highlights for a waxy modelling-clay feel; zero removes the highlight.
- **Original texture strength:** one retains the original colour texture; zero uses the material colour multiplied by Clay colour tint. Texture alpha still controls cutouts.

Detail uses model-local 3D coordinates, avoiding UV seams and sliding when rigid objects move. Skinned deformation can stretch or move procedural detail; this is not a rest-pose texture implementation. Fine detail fades with distance to reduce aliasing. Existing normal maps are not used. Round forms, soft scene lighting and contact shadows still matter: this surface shader alone cannot turn angular geometry into a clay sculpture. Preview and Apply use the same parameters; saved profiles include all five controls. Previously applied materials are not rewritten with new profile values: Undo and reapply, or adjust their material properties.

## Apply a look to your scene

1. Choose your Look and settings; optionally limit the hierarchy and layers.
2. Click **Apply to Scene…**. The folder picker opens every time: choose or create a folder inside this project's **Assets** folder. Cancelling makes no scene or material changes. Linked folders and folders outside Assets are rejected.
3. Confirm the scope and destination. With no hierarchy root, eligible objects in **all loaded scenes** are affected, not just objects visible in the preview camera.
4. New materials are created in your chosen folder and assigned to scene instances. Original material and prefab assets stay unchanged; colliding filenames receive unique suffixes. Save your scene when satisfied.

Apply uses the **current settings**, not a cached preview image. Materials shared by multiple eligible slots reuse one generated surface material. Unsupported surfaces retain their originals, following the preview filters. Outline Toon also creates outline material files and child objects for gameplay. The package must remain installed because the materials reference its runtime shaders and outlines reference its runtime component.

**Undo / Redo** restores scene assignments and outline objects, but keeps generated material files for reuse. Files are not automatically deleted on Undo. If Apply fails, its scene changes are rolled back and its newly created material files are removed. Prefab Mode is blocked; apply to scene instances instead. Applying again converts the currently assigned materials, so Undo first to compare alternatives without multiplying tints. An existing applied outline must be undone before another application to that object.

Test in a scene copy before adopting a look in production. Apply does not save scenes automatically, modify prefab assets, or convert dynamically spawned prefabs. Game scripts which later replace materials can override the applied look. These are simplified shaders, not a conversion of every original shading feature.

## Custom shader mapping

Choose a URP-compatible surface shader. The tool starts from a temporary copy of each source material, switches the shader, and maps base texture and colour. It falls back from `_BaseMap` to `_MainTex` and from `_BaseColor` to `_Color`. You can specify the source and destination property names; texture scale and offset are copied too.

Other matching serialized properties may carry across, but this is not a universal shader converter. Shader-specific keyword validation, texture packing, render-state setup, extra passes, additional textures and vertex animation need dedicated adapters. Using the built-in looks is the supported baseline.

## Preview behaviour

### Depth and normals (0.8)

All built-in looks support URP depth and normal generation. The stylized surface shader now includes a DepthNormalsOnly pass (including URP's deferred octahedral normal encoding and rendering-layer output). Standard Lit uses Unity's native passes. Colour, depth and depth-normal passes use the same base texture alpha, tint alpha, cutoff and culling, including opted-in cutout foliage. Clay shares its procedural bump-normal calculation between colour and normal rendering. The expanded Outline Toon shell now writes matching silhouette depth and normals, with the same visibility and cutout rules as its colour pass.

The hidden preview camera explicitly requests its own depth texture even when the source camera or pipeline disables depth textures. Original and styled render requests regenerate depth inside their respective material states; styled depth is not borrowed from the source camera or reused from the original capture. This does not change the source camera, renderer asset or pipeline settings. Normals are generated when URP renderer features request them.

Regression coverage includes a synthetic depth-clipped raymarched cloud slab, depth fog and soft-particle intersection fading, tested in Forward, Deferred (packed normals) and Forward+, in both Render Graph and compatibility mode. The fixture deliberately swaps the foliage alpha mask and uses a tilted background to detect stale depth, missing normals and filled-in cutout holes. Separate tests cover DepthOnly prepasses, post-opaque depth copying, expanded outline silhouettes and Clay bump normals.

These tests exercise the URP depth/normal contract, not a particular third-party cloud package. Effects using proprietary buffers, temporal history, custom camera callbacks or textures sampled before URP produces them still need integration-specific testing. Custom replacement shaders must supply their own compatible passes. Transparent surfaces remain excluded; this release does not add transparent-depth rendering, original foliage wind deformation or motion vectors.

The window creates a hidden camera, renders an original image, temporarily replaces eligible renderer material references, renders the look image, and restores all references in a synchronous `try/finally` scope. Outline Toon also creates temporary hidden mesh shells under the affected objects. Temporary materials, cameras, textures and shells are cleaned up. Source material assets are not edited. This is a transaction over live renderer references, not a separate copy of the scene: callbacks can observe temporary assignments and shell objects during the styled capture.

The source camera's URP settings are copied. Camera stacks are not copied; overlay cameras, screen-space overlay UI and camera-attached custom scripts are not reproduced. Temporal antialiasing is disabled for deterministic still comparisons. Original and styled captures are sequential, so time-dependent effects may differ slightly.

## Current limits

- Preview and Apply controls run in Edit Mode with URP. Applied materials and outlines are usable in gameplay and player builds.
- MeshRenderer and SkinnedMeshRenderer are supported. Transparent materials, particles, sprites and renderers using MaterialPropertyBlock are skipped. Terrain retains its original shader. The status line counts renderer material slots, not terrain.
- Cutout meshes are opt-in. Wind/vertex deformation from their original shader is not reproduced by the built-in looks.
- Stylized looks preserve base texture, UV scale/offset and colour. They use simplified main-light shading, fog and shadows. Original normal maps, metallic response, baked GI and additional lights are not reproduced. Standard Lit uses Unity's URP lighting, with the conversion limitations described above.
- Custom shader support is experimental. Imported shader packages may need custom adapters.
- Scene changes do not automatically mark a cached capture as stale; recapture or enable auto refresh before exporting after editing the scene.
- Motion-vector passes and third-party-specific buffers can need additional support. Built-in looks support URP depth and normals. This prototype is for visual exploration, not performance benchmarking or pixel-identical final output.

## Development validation

The repository includes Editor validation code, but no demo project, generated materials, screenshots or builds. Run `SceneLookLab.LabValidation.Run` only in a disposable Unity 6000.0 URP test project, in batch mode with an `Assets/.gitkeep` marker. Use `SceneLookLab.LabValidation.RunCompatibility` for the compatibility-mode run; it restores the prior compatibility setting afterwards. The runner creates/replaces test assets under `Assets/LabDemo`; never run it in a game project. It checks preview restoration, textures, filtering, dirty state, compilation, rendering, profiles and cleanup. Apply checks cover chosen destinations, preserved originals, filename collisions, Undo/Redo, saved-scene reload, persistent outlines and a Windows player build with shader inclusion. Clay, Standard Lit, hatching direction and the depth-effect regressions above are included. Images, results and the test player are written to the sibling `Validation-0.8` folder. The player build requires Windows build support.

Test your specific game scene and imported shaders before adopting the package. Applying a look does not commit or push any game-project files.
