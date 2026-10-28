Shader "Hidden/SceneLookLab/Surface"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Colour", Color) = (1,1,1,1)
        _ClayColor("Clay Colour", Color) = (1,1,1,1)
        _ShadowTint("Shadow Tint", Color) = (0.27,0.32,0.43,1)
        _Bands("Light Bands", Float) = 3
        _LookMode("Look Mode", Float) = 0
        _Softness("Band Softness", Float) = 0.15
        _PatternScale("Pattern Scale", Float) = 8
        _HatchRotation("Hatching Rotation (Degrees)", Range(0,180)) = 0
        _AccentColor("Accent Colour", Color) = (0.25,0.8,1,1)
        _RimPower("Rim Power", Float) = 3
        _RimStrength("Rim Strength", Float) = 1
        _BrushStrength("Brush Strength", Float) = 0.35
        _ClayDetail("Clay Surface Detail", Range(0,1)) = 0.55
        _ClayDetailSize("Clay Detail Size", Range(0.02,0.5)) = 0.08
        _ClayUnevenness("Clay Unevenness", Range(0,1)) = 0.35
        _ClayShine("Clay Soft Shine", Range(0,1)) = 0.35
        _ClayTextureStrength("Clay Original Texture Strength", Range(0,1)) = 1
        _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Cutoff", Range(0,1)) = 0.5
        _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        ZWrite On
        ZTest LEqual
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ClayColor, _ShadowTint, _AccentColor;
            float _Bands, _LookMode, _AlphaClip, _Cutoff, _Cull;
            float _Softness, _PatternScale, _RimPower, _RimStrength;
            float _BrushStrength;
            float _HatchRotation;
            float _ClayDetail, _ClayDetailSize, _ClayUnevenness, _ClayShine, _ClayTextureStrength;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3; float3 positionOS : TEXCOORD4; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(Attributes v)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionOS = v.positionOS.xyz;
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            o.fog = ComputeFogFactor(o.positionCS.z);
            return o;
        }
        half4 BaseSample(float2 uv)
        {
            half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            if (_AlphaClip > 0.5) clip(base.a - _Cutoff);
            return base;
        }
        float NoiseHash(float3 p) { return frac(sin(dot(p, float3(127.1,311.7,74.7))) * 43758.5453); }
        float BrushNoise(float3 p)
        {
            float3 cell = floor(p), f = frac(p);
            f = f * f * (3 - 2 * f);
            return lerp(lerp(lerp(NoiseHash(cell), NoiseHash(cell + float3(1,0,0)), f.x),
                             lerp(NoiseHash(cell + float3(0,1,0)), NoiseHash(cell + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(NoiseHash(cell + float3(0,0,1)), NoiseHash(cell + float3(1,0,1)), f.x),
                             lerp(NoiseHash(cell + float3(0,1,1)), NoiseHash(cell + 1), f.x), f.y), f.z);
        }
        half3 SurfaceNormal(Varyings i, out float grain, out float fineFade)
        {
            half3 normal = normalize(i.normalWS);
            grain = 0.5;
            fineFade = 0;
            if ((int)round(_LookMode) != 2) return normal;
            // Model-local 3D texture: no UV seams, and no sliding on rigid objects.
            float size = max(0.02, _ClayDetailSize);
            float3 p = i.positionOS / size;
            float footprint = max(length(ddx(p)), length(ddy(p)));
            fineFade = 1 - smoothstep(0.25, 1.2, footprint);
            grain = BrushNoise(p * 2.7);
            float pores = pow(saturate(BrushNoise(p * 5.3 + 13.7)), 3);
            float warp = BrushNoise(p * 0.35 + 7.1);
            float ridges = sin((p.x * 0.7 + p.y + p.z * 0.4 + warp * 2) * 18);
            float ridgeFade = 1 - smoothstep(0.08, 0.45, footprint);
            float dents = BrushNoise(i.positionOS * 5.5 + 4.3);
            float height = _ClayDetail * size * (0.015 * grain - 0.012 * pores + 0.00035 * ridges * ridgeFade) * fineFade
                + _ClayUnevenness * 0.025 * dents;
            // Surface-gradient bump mapping works without tangents or normal textures.
            float3 dx = ddx(i.positionWS), dy = ddy(i.positionWS);
            float scale = length(dx) / max(length(ddx(i.positionOS)), 0.00001);
            float3 r1 = cross(dy, normal), r2 = cross(normal, dx);
            float determinant = dot(dx, r1);
            float3 gradient = (ddx(height) * r1 + ddy(height) * r2) * scale;
            if (abs(determinant) > 1e-10)
                normal = normalize(abs(determinant) * normal - sign(determinant) * gradient);

            return normal;
        }
        ENDHLSL
        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 base = BaseSample(i.uv);
                half3 colour = base.rgb;
                int mode = (int)round(_LookMode);
                half3 normal = normalize(i.normalWS);
                if (mode == 8) return half4(normal * 0.5 + 0.5, 1);
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                if (mode == 2)
                {
                    float grain, fineFade;
                    normal = SurfaceNormal(i, grain, fineFade);
                    colour = lerp(_BaseColor.rgb, base.rgb, saturate(_ClayTextureStrength));
                    colour *= 1 + _ClayDetail * (grain - 0.5) * 0.045 * fineFade;
                    half wrap = saturate((dot(normal, light.direction) + 0.18) / 1.18) * light.shadowAttenuation;
                    colour *= lerp(_ShadowTint.rgb, max(light.color, half3(0.05,0.05,0.05)), wrap);
                    half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half3 halfway = SafeNormalize(light.direction + view);
                    half highlight = pow(saturate(dot(normal, halfway)), 16);
                    colour += light.color * highlight * _ClayShine * 0.32 * light.shadowAttenuation
                        * saturate(dot(normal, light.direction));
                    return half4(MixFog(colour, i.fog), 1);
                }
                half diffuse = saturate(dot(normal, light.direction)) * light.shadowAttenuation;
                half bands = max(2, _Bands);
                if (mode == 0 || mode == 9)
                    diffuse = floor(diffuse * (bands - 1) + 0.5) / (bands - 1);
                if (mode == 4)
                {
                    half softened = 0;
                    for (int band = 0; band < 5; band++)
                    {
                        if (band >= (int)bands - 1) break;
                        half threshold = (band + 0.5) / (bands - 1);
                        half width = max(0.001, _Softness / (bands - 1));
                        softened += smoothstep(threshold - width, threshold + width, diffuse) / (bands - 1);
                    }
                    diffuse = softened;
                }
                if (mode == 10 || mode == 11)
                {
                    float pattern;
                    if (mode == 10)
                    {
                        float3 p = i.positionWS * max(2, _PatternScale);
                        pattern = BrushNoise(p * float3(1,3,1)) * 0.7 + BrushNoise(p * 2.1) * 0.3;
                    }
                    else
                    {
                        float3 weights = abs(normal);
                        float2 plane = weights.x > weights.y && weights.x > weights.z ? i.positionWS.yz :
                            weights.y > weights.z ? i.positionWS.xz : i.positionWS.xy;
                        float2 p = plane * max(2, _PatternScale);
                        float2 cell = frac(p) - 0.5;
                        float dab = length(cell * float2(1,1.35));
                        float aa = max(fwidth(dab), 0.005);
                        pattern = 1 - smoothstep(0.29 - aa, 0.29 + aa, dab);
                    }
                    float painted = saturate(diffuse + (pattern - 0.5) * _BrushStrength);
                    float aa = max(fwidth(painted), 0.002);
                    float width = mode == 10 ? max(aa, _Softness / (bands - 1)) : aa;
                    float stepped = 0;
                    for (int band = 0; band < 5; band++)
                    {
                        if (band >= (int)bands - 1) break;
                        float threshold = (band + 0.5) / (bands - 1);
                        stepped += smoothstep(threshold - width, threshold + width, painted) / (bands - 1);
                    }
                    diffuse = stepped;
                }
                if (mode == 6)
                {
                    // Filter the checker across a pixel footprint to avoid moire at distance.
                    float2 uv = i.uv * max(2, _PatternScale);
                    float2 width = max(fwidth(uv), 0.0001);
                    float2 edge0 = uv - width * 0.5;
                    float2 edge1 = uv + width * 0.5;
                    float2 tri0 = abs(frac(edge0 * 0.5) - 0.5) * 2;
                    float2 tri1 = abs(frac(edge1 * 0.5) - 0.5) * 2;
                    float2 filtered = (tri1 - tri0) / width;
                    half check = saturate(0.5 + 0.5 * filtered.x * filtered.y);
                    colour = lerp(_ClayColor.rgb, _AccentColor.rgb, check);
                }
                if (mode != 1) colour *= lerp(_ShadowTint.rgb, max(light.color, half3(0.05,0.05,0.05)), diffuse);
                if (mode == 5)
                {
                    float spacing = max(2, _PatternScale);
                    float angle = radians(fmod(_HatchRotation, 180.0));
                    float sine, cosine;
                    sincos(angle, sine, cosine);
                    float2 screen = float2(cosine * i.positionCS.x - sine * i.positionCS.y,
                                           sine * i.positionCS.x + cosine * i.positionCS.y);
                    float2 hatchUV = float2(screen.x + screen.y, screen.x - screen.y) / spacing;
                    float2 aa = max(fwidth(hatchUV), 0.001);
                    float2 lines = 1 - smoothstep(0.10, 0.10 + aa, abs(frac(hatchUV) - 0.5));
                    half hatch = max(lines.x * (1 - smoothstep(0.45, 0.85, diffuse)),
                                     lines.y * (1 - smoothstep(0.1, 0.45, diffuse)));
                    colour = lerp(_ClayColor.rgb * lerp(0.68, 1.0, diffuse), _AccentColor.rgb, hatch);
                }
                if (mode == 7)
                {
                    half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                    half rim = pow(1 - saturate(dot(normal, view)), max(0.5, _RimPower));
                    colour += _AccentColor.rgb * rim * _RimStrength;
                }
                return half4(MixFog(colour, i.fog), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            half4 DepthFrag(Varyings i) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); BaseSample(i.uv); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
            void DepthNormalsFrag(Varyings i, out half4 outNormal : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                    , out float4 outLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                BaseSample(i.uv);
                float grain, fineFade;
                float3 normal = SurfaceNormal(i, grain, fineFade);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normal);
                    outNormal = half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0);
                #else
                    outNormal = half4(normal, 0);
                #endif
                #ifdef _WRITE_RENDERING_LAYERS
                    outLayers = float4(EncodeMeshRenderingLayer(GetMeshRenderingLayer()), 0, 0, 0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o = Vert(v);
                float3 direction = _LightDirection;
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    direction = normalize(_LightPosition - o.positionWS);
                #endif
                o.positionCS = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, direction));
                #if UNITY_REVERSED_Z
                    o.positionCS.z = min(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z = max(o.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            half4 ShadowFrag(Varyings i) : SV_Target { BaseSample(i.uv); return 0; }
            ENDHLSL
        }
    }
}
