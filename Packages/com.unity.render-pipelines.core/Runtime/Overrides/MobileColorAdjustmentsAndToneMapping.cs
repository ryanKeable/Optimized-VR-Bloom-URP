using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// A volume component that holds settings for the Color Adjustments effect.
    /// </summary>
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing/Mobile Color Adjustments", typeof(UniversalRenderPipeline))]
    [URPHelpURL("Post-Processing-Color-Adjustments")]
    public sealed class MobileColorAdjustmentsAndToneMapping : VolumeComponent, IPostProcessComponent
    {
        [Header("Color Adjustments")]

        /// <summary>
        /// Adjusts the overall exposure of the scene in EV100.
        /// This is applied after HDR effect and right before tonemapping so it won't affect previous effects in the chain.
        /// </summary>
        [Tooltip("Adjusts the overall exposure of the scene in EV100. This is applied after HDR effect and right before tonemapping so it won't affect previous effects in the chain.")]
        public ClampedFloatParameter gamma = new ClampedFloatParameter(0f, -100f, 100f);

        /// <summary>
        /// Controls the overall range of the tonal values.
        /// </summary>
        [Tooltip("Expands or shrinks the overall range of tonal values.")]
        public ClampedFloatParameter contrast = new ClampedFloatParameter(0f, -100f, 100f);

        /// <summary>
        /// Controls the intensity of all colors in the render.
        /// </summary>
        [Tooltip("Pushes the intensity of all colors.")]
        public ClampedFloatParameter saturation = new ClampedFloatParameter(0f, -100f, 100f);

        /// <summary>
        /// Specifies the color that URP tints the render to.
        /// </summary>
        [Tooltip("Tint the render by multiplying a color.")]
        public ColorParameter colorFilter = new ColorParameter(Color.white, true, false, true);


        [Header("White Balance")]
        /// <summary>
        /// Controls the color temperature URP uses for white balancing.
        /// </summary>
        [Tooltip("Sets the white balance to a custom color temperature.")]
        public ClampedFloatParameter temperature = new ClampedFloatParameter(0f, -100, 100f);

        /// <summary>
        /// Controls the white balance color to compensate for a green or magenta tint.
        /// </summary>
        [Tooltip("Sets the white balance to compensate for a green or magenta tint.")]
        public ClampedFloatParameter tint = new ClampedFloatParameter(0f, -100, 100f);

        [Header("ToneMapping")]

        /// <summary>
        /// Use this to select a tonemapping algorithm to use for color grading.
        /// </summary>
        [Tooltip("Select a tonemapping algorithm to use for the color grading process.")]
        public TonemappingModeParameter mode = new TonemappingModeParameter(TonemappingMode.None);

        /// <inheritdoc/>
        public bool IsActive() => true;

        /// <inheritdoc/>
        public bool IsTileCompatible() => true;
    }
}
