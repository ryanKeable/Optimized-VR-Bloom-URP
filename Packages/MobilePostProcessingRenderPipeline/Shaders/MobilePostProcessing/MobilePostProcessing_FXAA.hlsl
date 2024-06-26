#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

#define HDRColorThreshold (0.6h)
#define FilteredHDRMaskThreshold (1.175h)
#define FilteredHDRMaskThresholdKnee (0.5h)

#define ContrastThreshold (0.0625h)
#define RelativeThreshold (0.125h)

#define RDK_FXAA_SPAN_MAX (8.0h)
#define RDK_FXAA_REDUCE_MUL (0.25h * (1.0h / 12.0h))
#define RDK_FXAA_REDUCE_MIN (1.0h / 128.0h)

struct HDRLuminanceData
{
    half2 ne, nw, se, sw;
    half highest, lowest, contrast;
};

half LinearRgbToLuminance(half3 linearRgb)
{
    return dot(linearRgb, half3(0.2126729h, 0.7151522h, 0.0721750h));
}

//cheap filter
half HDRFilter(half3 color)
{
    half brightness = Min3(color.r, color.g, color.b);
    return brightness - HDRColorThreshold;
}

half FilteredHDRMask(half mask)
{
    // Thresholding
    half softness = clamp(mask - FilteredHDRMaskThreshold + 0.5h, 0.0, 2.0h * FilteredHDRMaskThresholdKnee);
    softness = (softness * softness) / (4.0h * FilteredHDRMaskThresholdKnee + 1e-4);
    half multiplier = max(mask - FilteredHDRMaskThreshold, softness) / max(mask, 1e-4);
    
    mask *= multiplier;

    return mask;
}


half3 Fetch(float2 coords, float2 offset, TEXTURE2D_X(tex))
{
    float2 uv = coords + offset;
    return(SAMPLE_TEXTURE2D_X(tex, sampler_PointClamp, uv).xyz);
}

// store the luma in the x channel and the hdr value in the y
// we need the luma for blending calculations and we need to the hdr for masking
half2 SampleHDRFilterLuminance(half2 uv, half4 texelSize, TEXTURE2D_X(tex), int uOffset = 0, int vOffset = 0)
{
    half luma = 0.0;

    uv += texelSize.xy * float2(uOffset, vOffset);
    half3 color = SAMPLE_TEXTURE2D_X(tex, sampler_PointClamp, uv.xy);

    // filtering again helps remove text and other issues
    half hdrMask = HDRFilter(color);
    half filteredMask = hdrMask;

    luma = Luminance(color.rgb);

    return half2(luma, filteredMask);
}
// ok we need to know when sampling luminance if a pixel in our neighbou0rhood has a hdr value
HDRLuminanceData SampleHDRFilterLuminanceNeighborhood(half2 uv, half4 texelSize, TEXTURE2D_X(tex))
{
    HDRLuminanceData l;
    l.ne = saturate(SampleHDRFilterLuminance(uv, texelSize, tex, 1, 1));
    l.nw = saturate(SampleHDRFilterLuminance(uv, texelSize, tex, -1, 1));
    l.se = saturate(SampleHDRFilterLuminance(uv, texelSize, tex, 1, -1));
    l.sw = saturate(SampleHDRFilterLuminance(uv, texelSize, tex, -1, -1));

    return l;
}

// if my neighbourhood lacks a HDR pixel then we should skip me
bool ShouldSkipPixel_HDRFilter(HDRLuminanceData l)
{
    half isHDR = l.ne.y + l.nw.y + l.se.y + l.sw.y;
    return isHDR > 0;
}

// if my contrast is too low then skip me
bool ShouldSkipPixel_Contrast(HDRLuminanceData l)
{
    float threshold = max(ContrastThreshold, RelativeThreshold * l.highest);
    return l.contrast < threshold;
}

// // // this needs an effective way of reducing the size of the blur
half3 FXAA_HDRFilter(half3 input, half2 uv, TEXTURE2D_X(tex), half4 texelSize)
{

    half hdrMask = HDRFilter(input).xxx;
    hdrMask = fwidth(hdrMask);

    if (hdrMask <= 0.05)
    {
        return input;
    }

    HDRLuminanceData l = SampleHDRFilterLuminanceNeighborhood(uv, texelSize, tex);
    if (!ShouldSkipPixel_HDRFilter(l))
    {
        return input;
    }

    l.highest = max(l.nw.x, Max3(l.ne.x, l.sw.x, l.se.x));
    l.lowest = min(l.nw.x, Min3(l.ne.x, l.sw.x, l.se.x));

    l.contrast = l.highest - l.lowest;

    // should ignore pixel?
    if (ShouldSkipPixel_Contrast(l))
    {
        return input;
    }

    half2 dir;
    dir.x = - ((l.nw.x + l.ne.x) - (l.sw.x + l.se.x));
    dir.y = ((l.nw.x + l.sw.x) - (l.ne.x + l.se.x));

    half lumaSum = l.nw.x + l.sw.x + l.se.x + l.ne.x;

    half dirReduce = max(lumaSum * RDK_FXAA_REDUCE_MUL, RDK_FXAA_REDUCE_MIN);
    half rcpDirMin = rcp(min(abs(dir.x), abs(dir.y)) + dirReduce);

    dir = min((RDK_FXAA_SPAN_MAX).xx, max((-RDK_FXAA_SPAN_MAX).xx, dir * rcpDirMin)) * texelSize.xy;
    
    // cheaper and nicer blending
    half3 rgb[4];

    [unroll(4)] for (int i = 0; i < 4; i++)
    {
        rgb[i] = saturate(Fetch(uv, dir * (i / 3.0 - 0.5), tex));
    }

    half3 rgbA = 0.5 * (rgb[1] + rgb[2]);
    half3 rgbB = rgbA * 0.5 + 0.25 * (rgb[0] + rgb[3]);

    half3 color = (rgbA + rgbB) * 0.5h;
    

    return(color);
}