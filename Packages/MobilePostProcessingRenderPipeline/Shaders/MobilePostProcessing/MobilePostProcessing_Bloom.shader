﻿Shader "MobileURP/Bloom"
{

    Properties
    {
        _MainTex ("Source", 2D) = "white" { }
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/MobilePostProcessingRenderPipeline/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/MobilePostProcessingRenderPipeline/Shaders/PostProcessing/Common.hlsl"
    #include "MobilePostProcessing_FXAA.hlsl"
    
    TEXTURE2D_X(_MainTexLowMip);
    float4 _MainTexLowMip_TexelSize;
    float4 _BlitTexture_TexelSize;

    half4 _Bloom_Params; // x: scatter, y: threshold, z: threshold knee, w: threshold numerator

    #define Scatter                 _Bloom_Params.x
    #define ClampMax               _Bloom_Params.y
    #define Threshold           _Bloom_Params.z
    #define ThresholdKnee      _Bloom_Params.w

    #define LinearBlurTaps          3
    #define KernelSize              3

    static const half vOffset[3] = {
        0.0h, 1.3846153846h * _BlitTexture_TexelSize.x, 3.2307692308h * _BlitTexture_TexelSize.x
    };
    static const half hOffset[3] = {
        0.0h, 1.3846153846h * _BlitTexture_TexelSize.y * 0.5h, 3.2307692308h * _BlitTexture_TexelSize.y * 0.5h
    };

    static const half weight[3] = {
        0.22702703h, 0.31621622h, 0.07027027h
    };

    half3 FilteredColour(half3 color)
    {
        // User controlled clamp to limit crazy high broken spec
        color = min(ClampMax, color);

        // Thresholding
        half brightness = Max3(color.r, color.g, color.b);
        half softness = clamp(brightness - Threshold + ThresholdKnee, 0.0, 2.0 * ThresholdKnee);
        softness = (softness * softness) / (4.0 * ThresholdKnee + 1e-4);
        half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
        color *= multiplier;

        // Clamp colors to positive once in prefilter. Encode can have a sqrt, and sqrt(-x) == NaN. Up/Downsample passes would then spread the NaN.
        color = max(color, 0);

        return color;
    }

    half4 FragPrefilter(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        
        // mask first then blur then filter
        half3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).xyz;

        // bloom filter
        color = FilteredColour(color);

        return half4(color, 1.0h);
    }

    half4 FragLinearBlurH(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);


        half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

        color.rgb *= weight[0];

        UNITY_UNROLL
        for (int i = 1; i < LinearBlurTaps; i++)
        {
            color.rgb += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + half2(vOffset[i], 0.0)).xyz * weight[i];
            color.rgb += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - half2(vOffset[i], 0.0)).xyz * weight[i];
        }


        return half4(color.rgb, 1.0h);
    }

    half4 FragLinearBlurV(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);


        half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        
        color.rgb *= weight[0];

        UNITY_UNROLL
        for (int i = 1; i < LinearBlurTaps; i++)
        {
            color.rgb += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + half2(0.0, hOffset[i])).xyz * weight[i];
            color.rgb += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - half2(0.0, hOffset[i])).xyz * weight[i];
        }
        
        return half4(color.rgb, 1.0h);
    }

    half4 FragUpsample(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        half4 highMip = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
        half3 lowMip = SAMPLE_TEXTURE2D_X(_MainTexLowMip, sampler_LinearClamp, uv).xyz;
        
        half3 color = lerp(highMip, lowMip, Scatter);
        
        return half4(color, highMip.a);
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
            Name "Bloom Prefilter"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragPrefilter

            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile _ _BLOOM_OFF
            ENDHLSL

        }

        Pass
        {
            Name "Bloom Blur Horizontal"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragLinearBlurH

            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile _ _BLOOM_OFF
            ENDHLSL

        }

        Pass
        {
            Name "Bloom Blur Vertical"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragLinearBlurV

            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile _ _BLOOM_OFF
            ENDHLSL

        }

        Pass
        {
            Name "Bloom Upsample"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment FragUpsample

            #pragma fragmentoption ARB_precision_hint_fastest
            #pragma multi_compile _ _BLOOM_OFF
            ENDHLSL

        }
    }
}
