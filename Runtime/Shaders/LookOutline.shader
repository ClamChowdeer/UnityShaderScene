Shader "Hidden/SceneLookLab/Outline"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _OutlineColor("Outline Color", Color) = (0.025,0.025,0.035,1)
        _OutlineWidth("Outline Width", Float) = 2
        _Visible("Visible", Float) = 1
        _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Cutoff", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-1" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor, _OutlineColor;
                float _OutlineWidth, _Visible, _AlphaClip, _Cutoff;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float2 direction = TransformWorldToViewDir(normalWS).xy;
                direction /= max(length(direction), 0.0001);
                o.positionCS.xy += direction * _OutlineWidth * 2 / _ScreenParams.xy * o.positionCS.w;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                clip(_Visible - 0.5);
                if (_AlphaClip > 0.5)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
