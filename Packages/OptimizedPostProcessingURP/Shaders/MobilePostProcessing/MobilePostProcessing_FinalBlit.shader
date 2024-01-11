Shader "MobileURP/FinalBlit"
{

    HLSLINCLUDE

    #pragma target 2.0
    #pragma fragmentoption ARB_precision_hint_fastest
    
    #pragma multi_compile_local_fragment _ _COLORADJUSTMENTS
    #pragma multi_compile_local_fragment _ _FXAA_ON
    #pragma multi_compile_local_fragment _ _TONEMAP_ACES _TONEMAP_NEUTRAL

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "MobilePostProcessing_FXAA.hlsl"
    #include "MobilePostProcessing_ColorAdjustments.hlsl"



    float4 _BlitTexture_TexelSize;

    TEXTURE2D_X(_Bloom_Texture);
    float4 _Bloom_Texture_TexelSize;
    
    half3 _Bloom_Tint;
    half _Bloom_Intensity;
    
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv.xy);

        #if _FXAA_ON
            color = FXAA_HDRFilter(color, uv, _BlitTexture, _BlitTexture_TexelSize);
        #endif
        
        half3 bloom = SAMPLE_TEXTURE2D_X(_Bloom_Texture, sampler_LinearClamp, uv).xyz;
        bloom.rgb *= _Bloom_Intensity.xxx * _Bloom_Tint;
        color += bloom.rgb;

        color = ApplyColorAdjustments(color);

        return half4(color, 1.0);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        
        Pass
        {
            Name "Mobile Final Blit"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            
            ENDHLSL

        }
    }
}
