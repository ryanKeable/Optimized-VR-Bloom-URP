﻿Shader "DelMarRenderer/PostProcessing Bloom Optimized"
{

    Properties
    {
        _MainTex ("Source", 2D) = "white" { }
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
    #include "../MightyColourGrading.hlsl"
    #include "../MightyFXAA.hlsl"
    
    TEXTURE2D_X(_MainTex);
    TEXTURE2D_X(_MainTexLowMip);

    float4 _MainTex_TexelSize;
    float4 _MainTexLowMip_TexelSize;

    half4 _Bloom_Params; // x: scatter, y: threshold, z: threshold knee, w: threshold numerator

    #define Scatter                 _Bloom_Params.x
    #define Threshold               _Bloom_Params.y
    #define ThresholdKnee           _Bloom_Params.z
    #define ThresholdNumerator      _Bloom_Params.z

    #define LinearBlurTaps          3
    #define KernelSize          3

    static const half vOffset[3] = {
        0.0h, 1.3846153846h * _MainTex_TexelSize.x, 3.2307692308h * _MainTex_TexelSize.x
    };
    static const half hOffset[3] = {
        0.0h, 1.3846153846h * _MainTex_TexelSize.y * 0.5h, 3.2307692308h * _MainTex_TexelSize.y * 0.5h
    };

    static const half weight[3] = {
        0.22702703h, 0.31621622h, 0.07027027h
    };
    


    half3 BoxBlur(half2 uv, half3 color)
    {
        half3 sum = color; // takes the original sample instead of sampling in the loop again

        int upper = ((KernelSize - 1) / 2);
        int lower = -upper;
        
        UNITY_UNROLL
        for (int x = lower; x <= upper; ++x)
        {
            UNITY_UNROLL
            for (int y = lower; y <= upper; ++y)
            {
                if (x != 0 || y != 0) // ignores sampling the original sample again

                {
                    half2 offset = half2(_MainTex_TexelSize.x * x * 2.0h, _MainTex_TexelSize.y * y);
                    sum += SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + offset).xyz;
                }
            }
        }

        sum /= (KernelSize * KernelSize);
        return half3(sum);
    }

    half3 FilteredColour(half3 color)
    {
        // Thresholding
        half brightness = Max3(color.r, color.g, color.b);
        half softness = clamp((brightness - Threshold) + ThresholdKnee, 0.0, ThresholdKnee);
        softness = (softness * softness) / ThresholdNumerator;
        half multiplier = max(brightness - Threshold, softness) / max(brightness, 1e-4);
        
        color *= multiplier;

        return color;
    }

    half4 FragPrefilter(Varyings input): SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.uv);
        
        // mask first then blur then filter
        half3 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv).xyz;
        
         #ifdef _BLOOM_OFF
        half hdrMask = FastTonemap(HDRFilter(color).xxx);
        if (hdrMask <= 0)
            return 0;
         #endif
        // half3 color = BoxBlur(uv, mask);

        // bloom filter
        color = FilteredColour(color);
        color = FastTonemap(color);

        return half4(color, 1.0h);
    }

    half4 FragLinearBlurH(Varyings input): SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

        #ifdef _BLOOM_OFF

        half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv);

        color.rgb *= weight[0];

        UNITY_UNROLL
        for (int i = 1; i < LinearBlurTaps; i++)
        {
            color.rgb += SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + half2(vOffset[i], 0.0)).xyz * weight[i];
            color.rgb += SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - half2(vOffset[i], 0.0)).xyz * weight[i];
        }

        #else 

        float texelSize = _MainTex_TexelSize.x * 2.0;

        // 9-tap gaussian blur on the downsampled source
        half3 c0 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - float2(texelSize * 4.0, 0.0)));
        half3 c1 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - float2(texelSize * 3.0, 0.0)));
        half3 c2 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - float2(texelSize * 2.0, 0.0)));
        half3 c3 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - float2(texelSize * 1.0, 0.0)));
        half3 c4 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv                               ));
        half3 c5 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + float2(texelSize * 1.0, 0.0)));
        half3 c6 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + float2(texelSize * 2.0, 0.0)));
        half3 c7 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + float2(texelSize * 3.0, 0.0)));
        half3 c8 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + float2(texelSize * 4.0, 0.0)));

        half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                    + c4 * 0.22702703
                    + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

        #endif

        return half4(color.rgb, 1.0h);
    }

    half4 FragLinearBlurV(Varyings input): SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

        #ifdef _BLOOM_OFF

        half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv);
        
        color.rgb *= weight[0];

        UNITY_UNROLL
        for (int i = 1; i < LinearBlurTaps; i++)
        {
            color.rgb += SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + half2(0.0, hOffset[i])).xyz * weight[i];
            color.rgb += SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - half2(0.0, hOffset[i])).xyz * weight[i];
        }

        #else
            float texelSize = _MainTex_TexelSize.y;

            // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
            half3 c0 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 3.23076923)));
            half3 c1 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv - float2(0.0, texelSize * 1.38461538)));
            half3 c2 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv                                      ));
            half3 c3 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 1.38461538)));
            half3 c4 = (SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv + float2(0.0, texelSize * 3.23076923)));

            half3 color = c0 * 0.07027027 + c1 * 0.31621622
                        + c2 * 0.22702703
                        + c3 * 0.31621622 + c4 * 0.07027027;

        #endif
        
        return half4(color.rgb, 1.0h);
    }

    half4 FragUpsample(Varyings input): SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        half2 uv = UnityStereoTransformScreenSpaceTex(input.uv);

        half4 highMip = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, uv);
        half3 lowMip = SAMPLE_TEXTURE2D_X(_MainTexLowMip, sampler_LinearClamp, uv).xyz;
        
        half3 color = highMip.xyz + lowMip * Scatter;
        
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
