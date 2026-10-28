using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    internal static class CompatibilityValidation
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception("Compatibility: " + message);
            Debug.Log("PASS: " + message);
        }

        // Generated only inside the disposable validation project, never shipped as a
        // shader asset with the same name as the game's real Meadow Ground shader.
        const string MeadowFixture = @"Shader ""ScGame Greybox/Meadow Ground"" {
Properties { _ColorA(""Earth"",Color)=(.34,.37,.19,1) _ColorB(""Moss"",Color)=(.52,.52,.29,1) }
SubShader { Tags { ""RenderPipeline""=""UniversalPipeline"" } Pass {
Tags { ""LightMode""=""UniversalForwardOnly"" }
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
half4 _ColorA, _ColorB;
struct A {float4 p:POSITION;}; struct V {float4 p:SV_POSITION; float3 ws:TEXCOORD0;};
V vert(A i){V o;o.ws=TransformObjectToWorld(i.p.xyz);o.p=TransformWorldToHClip(o.ws);return o;}
float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
half4 frag(V i):SV_Target {float field=noise(i.ws.xz*.24)*.7+noise(i.ws.xz*.9)*.3;return half4(lerp(_ColorA.rgb,_ColorB.rgb,field),1);}
ENDHLSL
} } }";

        internal static void Run(Camera camera, MeshRenderer subject, Material original)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/LabDemo/CompatibilityMeadow.shader");
            string unknownPath = AssetDatabase.GenerateUniqueAssetPath("Assets/LabDemo/CompatibilityUnknown.shader");
            File.WriteAllText(path, MeadowFixture);
            File.WriteAllText(unknownPath, MeadowFixture.Replace("ScGame Greybox/Meadow Ground", "SceneLookLab Tests/Unknown Ground"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(unknownPath, ImportAssetOptions.ForceSynchronousImport);
            var meadow = new Material(AssetDatabase.LoadAssetAtPath<Shader>(path)) { name = "Test Earth / Moss" };
            var unknown = new Material(AssetDatabase.LoadAssetAtPath<Shader>(unknownPath)) { name = "Unsupported Ground" };
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            string originalJson = EditorJsonUtility.ToJson(original);
            try
            {
                subject.sharedMaterial = unknown;
                bool dirty = EditorUtility.IsDirty(subject);
                var scan = MaterialCompatibility.Scan(new[] { subject }, profile);
                Check(scan.Converted == 0 && scan.NeedsReview == 1 && scan.Describe().Contains("Unknown Ground"),
                    "Unknown shader is preserved with named review reason");
                Check(subject.sharedMaterial == unknown && EditorUtility.IsDirty(subject) == dirty, "Compatibility scan never changes assignments or dirty state");
                using (var transaction = new MaterialTransaction())
                {
                    transaction.Apply(new[] { subject }, profile);
                    Check(transaction.ChangedSlots == 0 && subject.sharedMaterial == unknown, "Preview skips unknown material instead of making it white");
                }
                int assetCount = AssetDatabase.FindAssets("t:Material", new[] { "Assets/LabDemo" }).Length;
                string report = SceneLookApply.Apply(profile, subject.transform, "Assets/LabDemo");
                Check(subject.sharedMaterial == unknown && report.Contains("REVIEW") &&
                    AssetDatabase.FindAssets("t:Material", new[] { "Assets/LabDemo" }).Length == assetCount,
                    "Apply preserves unknown material and creates no unwanted assets");
                subject.sharedMaterial = original;
                profile.excludedMaterials = new[] { original };
                Check(MaterialCompatibility.Scan(new[] { subject }, profile).Converted == 0, "Material exclusion is respected");
                profile.excludedMaterials = Array.Empty<Material>();
                profile.excludedShaders = new[] { original.shader };
                Check(MaterialCompatibility.Scan(new[] { subject }, profile).Converted == 0, "Shader exclusion is respected");
                using (var transaction = new MaterialTransaction())
                {
                    transaction.Apply(new[] { subject }, profile);
                    Check(subject.sharedMaterial == original, "Exclusions protect actual preview assignments");
                }
                var copied = profile.Copy();
                Check(copied.excludedShaders.Length == 1 && copied.excludedShaders[0] == original.shader, "Profile copies retain exclusions");
                Object.DestroyImmediate(copied);
                LookPresets.Apply(profile, LookStyle.Clay);
                Check(profile.excludedShaders.Length == 1, "Changing looks retains exclusions");
                profile.excludedShaders = Array.Empty<Shader>();

                // Mixed material slots are independently classified.
                subject.sharedMaterials = new[] { original, unknown, meadow };
                var mixed = MaterialCompatibility.Scan(new[] { subject }, profile);
                Check(mixed.Converted == 2 && mixed.Preserved == 1 && mixed.NeedsReview == 1, "Mixed supported, unknown and Meadow slots report accurately");
                using (var transaction = new MaterialTransaction())
                {
                    transaction.Apply(new[] { subject }, profile);
                    var assigned = subject.sharedMaterials;
                    Check(assigned[0] != original && assigned[1] == unknown && assigned[2].GetFloat("_MeadowBlend") == 1,
                        "Mixed conversion preserves unsupported slots while adapting supported ones");
                }
                subject.sharedMaterials = new[] { meadow };
                foreach (var style in LookPresets.Styles)
                {
                    LookPresets.Apply(profile, style);
                    var plan = MaterialCompatibility.Scan(new[] { subject }, profile);
                    Check(style == LookStyle.StandardLit ? plan.Converted == 0 && plan.NeedsReview == 1 : plan.Converted == 1,
                        style + " explicitly handles Meadow compatibility");
                }

                LookPresets.Apply(profile, LookStyle.TexturedUnlit);
                using (var preview = new PreviewSession())
                {
                    preview.Capture(camera, profile, subject.transform, 480, 270);
                    var before = Pixels(preview.Original);
                    var after = Pixels(preview.Styled);
                    float difference = before.Zip(after, (a,b) => Mathf.Abs(a.r-b.r)+Mathf.Abs(a.g-b.g)+Mathf.Abs(a.b-b.b)).Average();
                    Check(difference < 0.005f, "Meadow adapter matches original two-colour world-space pattern pixel-for-pixel within tolerance");
                }
                LookPresets.Apply(profile, LookStyle.Clay);
                string apply = SceneLookApply.Apply(profile, subject.transform, "Assets/LabDemo");
                var saved = subject.sharedMaterial;
                Check(saved != meadow && EditorUtility.IsPersistent(saved) && saved.GetFloat("_MeadowBlend") == 1 &&
                    saved.GetColor("_MeadowColorA") == meadow.GetColor("_ColorA") && saved.GetColor("_MeadowColorB") == meadow.GetColor("_ColorB"),
                    "Applied Meadow material persists both colours and procedural adapter");
                var second = MaterialCompatibility.Scan(new[] { subject }, profile);
                Check(second.Converted == 1, "Already-adapted Meadow retains converter on future look changes");
                LookPresets.Apply(profile, LookStyle.StandardLit);
                Check(MaterialCompatibility.Scan(new[] { subject }, profile).Converted == 0, "Standard Lit cannot silently flatten an already-adapted Meadow material");
                Undo.PerformUndo();
                Check(subject.sharedMaterial == meadow, "Undo restores original Meadow material");
                Check(EditorJsonUtility.ToJson(original) == originalJson, "Compatibility workflow leaves original assets untouched");
            }
            finally
            {
                subject.sharedMaterials = new[] { original };
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(meadow);
                Object.DestroyImmediate(unknown);
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset(unknownPath);
            }
        }

        static Color[] Pixels(RenderTexture target)
        {
            var old = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            try { RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); return image.GetPixels(); }
            finally { RenderTexture.active = old; Object.DestroyImmediate(image); }
        }
    }
}
