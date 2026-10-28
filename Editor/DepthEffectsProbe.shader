Shader "Hidden/SceneLookLab/Tests/DepthEffectsProbe"
{
    Properties { _Effect("Effect", Float) = 0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            float _Effect;
            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float depth = LinearEyeDepth(SampleSceneDepth(i.texcoord), _ZBufferParams);
                float3 normal = SampleSceneNormals(i.texcoord);
                float result = depth / 12;
                if (_Effect == 1)
                {
                    // Synthetic volumetric cloud slab, raymarched only up to opaque scene depth.
                    float opticalDepth = 0;
                    for (int step = 0; step < 32; step++)
                    {
                        float z = 4 + (step + 0.5) * (3.0 / 32);
                        if (z >= depth) break;
                        float density = 0.7 + 0.2 * sin(z * 7 + i.texcoord.x * 11);
                        opticalDepth += density * (3.0 / 32);
                    }
                    result = 1 - exp(-opticalDepth);
                }
                if (_Effect == 2) result = 1 - exp(-depth * 0.15); // depth fog
                if (_Effect == 3) result = saturate(depth - 5); // soft-particle intersection
                return half4(saturate(result), saturate(length(normal)), normal.z * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }
    }
}
