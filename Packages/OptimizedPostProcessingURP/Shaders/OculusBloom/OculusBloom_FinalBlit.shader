Shader "OculusBloom/FinalBlit"
{

    HLSLINCLUDE


    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ScreenCoordOverride.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "OculusBloom_FXAA.hlsl"
    #include "OculusBloom_ColorAdjustments.hlsl"

    float4 _BlitTexture_TexelSize;

    TEXTURE2D_X(_Bloom_Texture);
    float4 _Bloom_Texture_TexelSize;
    
    half3 _Bloom_Tint;
    half _Bloom_Intensity;
    
    half4 Frag(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        half3 color = FXAA_HDRFilter(uv, _BlitTexture, _BlitTexture_TexelSize);
        half3 bloom = FXAA_HDRFilter(SCREEN_COORD_REMOVE_SCALEBIAS(uv), _Bloom_Texture, _Bloom_Texture_TexelSize);
        
        bloom.rgb *= _Bloom_Intensity.xxx * _Bloom_Tint;
        color += bloom.rgb;

        color = ApplyColorAdjustments(color);

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
            
            #pragma multi_compile_local_fragment _ _COLORADJUSTMENTS
            #pragma multi_compile_local_fragment _ _TONEMAP_ACES _TONEMAP_NEUTRAL
            
            ENDHLSL

        }
    }
}
