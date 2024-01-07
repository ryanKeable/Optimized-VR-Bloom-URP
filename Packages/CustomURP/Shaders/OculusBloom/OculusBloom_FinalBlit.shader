Shader "OculusBloom/FinalBlit"
{

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "OculusBloom_FXAA.hlsl"

    TEXTURE2D_X(_BlitTex);
    float4 _BlitTex_TexelSize;

    TEXTURE2D_X(_Bloom_Texture);
    
    half _Bloom_Intensity;
    
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        half4 bloom = SAMPLE_TEXTURE2D_X(_Bloom_Texture, sampler_LinearClamp, uv);
        half3 color = FXAA_HDRFilter(uv, _BlitTex, _BlitTex_TexelSize, bloom.a);

        bloom.rgb *= _Bloom_Intensity.xxx;
        color += bloom.rgb;

        return half4(color, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always
        ZWrite Off
        Cull Off
        
        Pass
        {
            Name "OculusBloom Final Blit"

            HLSLPROGRAM

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma fragmentoption ARB_precision_hint_fastest
            
            #pragma multi_compile _ _FXAA_OFF
            #pragma multi_compile _ _BLOOM_OFF

            ENDHLSL

        }
    }
}
