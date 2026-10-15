using UnityEngine;
using System.Linq;

namespace SceneLookLab
{
    internal static class LookPresets
    {
        public static readonly LookStyle[] Styles =
        {
            LookStyle.StandardLit, LookStyle.Cel, LookStyle.SoftToon, LookStyle.InkHatching, LookStyle.Clay,
            LookStyle.TexturedUnlit, LookStyle.ProceduralChecker, LookStyle.RimLight, LookStyle.WorldNormals,
            LookStyle.OutlineToon
        };
        // Legacy enum IDs remain valid for already-saved profiles and materials.
        public static readonly LookStyle[] MenuStyles = Styles.Concat(new[] { LookStyle.CustomShader }).ToArray();
        public static readonly string[] MenuLabels = MenuStyles.Select(style => UnityEditor.ObjectNames.NicifyVariableName(style.ToString())).ToArray();

        // Only appearance changes. Preserve the user's camera, layer and hierarchy scope,
        // alpha-clip choice, source mapping and custom shader reference.
        public static void Apply(LookProfile profile, LookStyle style)
        {
            profile.style = style;
            profile.tint = Color.white;
            profile.shadowColor = new Color(0.27f, 0.32f, 0.43f);
            profile.bands = 3;
            profile.softness = 0.15f;
            profile.patternScale = 8;
            profile.hatchRotation = 0;
            profile.accentColor = new Color(0.25f, 0.8f, 1f);
            profile.rimPower = 3;
            profile.rimStrength = 1;
            profile.outlineWidth = 2;
            profile.outlineColor = new Color(0.025f, 0.025f, 0.035f);
            profile.brushStrength = 0.35f;
            profile.clayDetail = 0.55f;
            profile.clayDetailSize = 0.08f;
            profile.clayUnevenness = 0.35f;
            profile.clayShine = 0.35f;
            profile.clayTextureStrength = 1;
            switch (style)
            {
                case LookStyle.SoftToon:
                    profile.shadowColor = new Color(0.42f, 0.36f, 0.52f);
                    profile.softness = 0.22f;
                    break;
                case LookStyle.InkHatching:
                    profile.tint = new Color(0.94f, 0.89f, 0.77f);
                    profile.accentColor = new Color(0.08f, 0.07f, 0.10f);
                    break;
                case LookStyle.Clay:
                    profile.tint = Color.white;
                    profile.shadowColor = new Color(0.34f, 0.30f, 0.27f);
                    break;
                case LookStyle.ProceduralChecker:
                    profile.tint = new Color(0.90f, 0.94f, 0.98f);
                    profile.accentColor = new Color(0.08f, 0.19f, 0.32f);
                    break;
                case LookStyle.RimLight:
                    profile.tint = new Color(0.10f, 0.13f, 0.20f);
                    profile.rimStrength = 1.4f;
                    break;
                case LookStyle.OutlineToon:
                    profile.shadowColor = new Color(0.38f, 0.31f, 0.18f);
                    profile.outlineWidth = 3;
                    break;
                case LookStyle.SoftPattern:
                    profile.bands = 2;
                    profile.patternScale = 12;
                    profile.softness = 0.07f;
                    profile.shadowColor = new Color(0.025f, 0.10f, 0.38f);
                    break;
                case LookStyle.HardBrush:
                    profile.bands = 2;
                    profile.patternScale = 22;
                    profile.brushStrength = 0.22f;
                    profile.shadowColor = new Color(0.025f, 0.10f, 0.38f);
                    break;
            }
        }

        public static string Describe(LookStyle style) => style switch
        {
            LookStyle.StandardLit => "Unity URP Lit with base texture, colour and UVs preserved. Default non-metallic surface, smoothness 0.5. Not a restore-originals action; other texture maps are not converted.",
            LookStyle.Cel => "Hard light bands with each surface's base texture and colour.",
            LookStyle.SoftToon => "Softened light bands and warm shadows; keeps base textures.",
            LookStyle.InkHatching => "Screen-space crosshatching on opaque surfaces. Hatch density follows main-light shading. Not an outline effect.",
            LookStyle.Clay => "Handcrafted clay: fine grain, shallow dents and soft waxy highlights. Detail size uses model-local units; unevenness affects shading, not the silhouette.",
            LookStyle.TexturedUnlit => "Base textures and colours without surface lighting; scene fog still applies.",
            LookStyle.ProceduralChecker => "Procedural UV checker pattern. Requires mesh UVs; replaces base textures.",
            LookStyle.RimLight => "A coloured silhouette rim over a dark textured surface. No bloom or transparent hologram rendering.",
            LookStyle.WorldNormals => "Diagnostic world-space mesh normals as RGB. This is not normal mapping.",
            LookStyle.OutlineToon => "Textured toon bands with an adjustable geometry silhouette outline. Best on closed meshes; cutout holes and internal creases are not outlined.",
            LookStyle.SoftPattern => "Textured toon lighting with irregular, softly brushed light/shadow boundaries. Pattern scale is in world units.",
            LookStyle.HardBrush => "Textured toon lighting with crisp, dotted brush marks along light/shadow boundaries. Pattern scale is in world units.",
            _ => "Use your own URP-compatible surface shader."
        };
    }
}
