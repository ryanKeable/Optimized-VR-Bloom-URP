#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"

float4 _ColorAdjustmentParams;
half3 _ColorBalance;
half3 _ColorFilter;

#define GAMMA                   _ColorAdjustmentParams.x
#define SATURATION              _ColorAdjustmentParams.y
#define CONTRAST                _ColorAdjustmentParams.z
#define VIBRANCE                _ColorAdjustmentParams.w
#define COLORBALANCE            _ColorBalance.xyz
#define COLORFILTER             _ColorFilter.xyz

half3 Vibrance(half3 input, half value)
{
    half average = (input.r + input.g + input.b) / 3.0;
    half mx = max(input.r, max(input.g, input.b));
    half amt = (mx - average) * (-value * 3.0);
    half3 colour = lerp(input.rgb, mx.rrr, amt);

    return colour;
}

half3 Exposure(half3 input, half value)
{
    return input.rgb * pow(2.0, value);
}

half3 Contrast(half3 input, half value)
{
    float3 colorLog = LinearToLogC(input);
    colorLog = (colorLog - ACEScc_MIDGRAY) * value + ACEScc_MIDGRAY;
    return LogCToLinear(colorLog);
}

half3 Saturation(half3 input, half value)
{
    static const half3 _LUMINANCE_WEIGHT = half3(0.2126h, 0.7152h, 0.0722h);
    half grey = dot(input, _LUMINANCE_WEIGHT);
    half3 ds = half3(grey, grey, grey);
    half3 colour = lerp(ds, input, value);

    return colour;
}

half3 ColorFilter(half3 input, half3 value)
{
    // Color filter is just an unclipped multiplier
    return input * value;
}

half3 WhiteBalance(half3 c)
{
    // White balance in LMS space
    half3 lms = LinearToLMS(c);
    lms *= COLORBALANCE;
    return LMSToLinear(lms);
}

half3 GammaCorrection(half3 input, half value)
{
    return pow(abs(input.rgb), value.xxx);
}

half3 InverseGammaCorrection(half3 input, half value)
{
    return pow(abs(input.rgb), 1.0h * value.xxx);
}

half3 LumaBasedReinhardToneMapping(half3 color)
{
    float luma = AcesLuminance(color);
    float toneMappedLuma = luma / (1. + luma);
    color *= toneMappedLuma / luma;
    return color;
}

half3 WhitePreservingLumaBasedReinhardToneMapping(half3 color)
{
    float white = 2.;
    float luma = AcesLuminance(color);
    float toneMappedLuma = luma * (1. + luma / (white * white)) / (1. + luma);
    color *= toneMappedLuma / luma;
    return color;
}

void WhitePreservingLumaBasedReinhardToneMapping_half(half3 color, out half3 value)
{
    float white = 2.;
    float luma = AcesLuminance(color);
    float toneMappedLuma = luma * (1. + luma / (white * white)) / (1. + luma);
    color *= toneMappedLuma / luma;
    value = color;
}

half3 RDKNeutralTonemap(half3 x)
{
    // Tonemap
    const half a = 0.2;
    const half b = 0.29;
    const half c = 0.24;
    const half d = 0.272;
    const half e = 0.02;
    const half f = 0.3;
    const half whiteLevel = 4.3;
    const half whiteClip = 1.0;

    half3 whiteScale = (1.0).xxx / NeutralCurve(whiteLevel, a, b, c, d, e, f);
    x = NeutralCurve(x * whiteScale, a, b, c, d, e, f);
    x *= whiteScale;

    // Post-curve white point adjustment
    x /= whiteClip.xxx;

    return x;
}

float3 RDKAcesTonemap(float3 aces)
{

    // --- Glow module --- //
    float saturation = rgb_2_saturation(aces);
    float ycIn = rgb_2_yc(aces);
    float s = sigmoid_shaper((saturation - 0.4) / 0.2);
    float addedGlow = 1.0 + glow_fwd(ycIn, RRT_GLOW_GAIN * s, RRT_GLOW_MID);
    aces *= addedGlow;

    // --- Red modifier --- //
    float hue = rgb_2_hue(aces);
    float centeredHue = center_hue(hue, RRT_RED_HUE);
    float hueWeight;
    {
        //hueWeight = cubic_basis_shaper(centeredHue, RRT_RED_WIDTH);
        hueWeight = smoothstep(0.0, 1.0, 1.0 - abs(2.0 * centeredHue / RRT_RED_WIDTH));
        hueWeight *= hueWeight;
    }

    aces.r += hueWeight * saturation * (RRT_RED_PIVOT - aces.r) * (1.0 - RRT_RED_SCALE);

    // --- ACES to RGB rendering space --- //
    float3 acescg = max(0.0, ACES_to_ACEScg(aces));

    // --- Global desaturation --- //
    //acescg = mul(RRT_SAT_MAT, acescg);
    acescg = lerp(dot(acescg, AP1_RGB2Y).xxx, acescg, RRT_SAT_FACTOR.xxx);

    // Luminance fitting of *RRT.a1.0.3 + ODT.Academy.RGBmonitor_100nits_dim.a1.0.3*.
    // https://github.com/colour-science/colour-unity/blob/master/Assets/Colour/Notebooks/CIECAM02_Unity.ipynb
    // RMSE: 0.0012846272106
    #if defined(SHADER_API_SWITCH) // Fix floating point overflow on extremely large values.
        const float a = 2.785085 * 0.01;
        const float b = 0.107772 * 0.01;
        const float c = 2.936045 * 0.01;
        const float d = 0.887122 * 0.01;
        const float e = 0.806889 * 0.01;
        float3 x = acescg;
        float3 rgbPost = ((a * x + b)) / ((c * x + d) + e / (x + FLT_MIN));
    #else
        const float a = 2.785085;
        const float b = 0.107772;
        const float c = 2.936045;
        const float d = 0.887122;
        const float e = 0.806889;
        float3 x = acescg;
        float3 rgbPost = (x * (a * x + b)) / (x * (c * x + d) + e);
    #endif

    // Scale luminance to linear code value
    // float3 linearCV = Y_2_linCV(rgbPost, CINEMA_WHITE, CINEMA_BLACK);

    // Apply gamma adjustment to compensate for dim surround
    float3 linearCV = darkSurround_to_dimSurround(rgbPost);

    // Apply desaturation to compensate for luminance difference
    //linearCV = mul(ODT_SAT_MAT, color);
    linearCV = lerp(dot(linearCV, AP1_RGB2Y).xxx, linearCV, ODT_SAT_FACTOR.xxx);

    // Convert to display primary encoding
    // Rendering space RGB to XYZ
    float3 XYZ = mul(AP1_2_XYZ_MAT, linearCV);

    // Apply CAT from ACES white point to assumed observer adapted white point
    XYZ = mul(D60_2_D65_CAT, XYZ);

    // CIE XYZ to display primaries
    linearCV = mul(XYZ_2_REC709_MAT, XYZ);

    return linearCV;
}

half3 ApplyToneMapping(half3 input)
{
    #if _TONEMAP_ACES
        float3 aces = unity_to_ACES(input);
        input = RDKAcesTonemap(aces);
    #elif _TONEMAP_NEUTRAL
        input = RDKNeutralTonemap(input);
    #endif

    return saturate(input);
}

half3 ApplyColorAdjustments(half3 input)
{
    #if _TONEMAP_ACES || _TONEMAP_NEUTRAL
        input = ApplyToneMapping(input);
    #endif

    #if _COLORADJUSTMENTS
        input = WhiteBalance(input);
        input = Contrast(input, CONTRAST);
        input = ColorFilter(input, COLORFILTER);

        // Do NOT feed negative values to the following color ops
        input = max(0.0, input);
        
        input = Saturation(input, SATURATION);
        input = GammaCorrection(input, GAMMA);
    #endif

    return input;
}


