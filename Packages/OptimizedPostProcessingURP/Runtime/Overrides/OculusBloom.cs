using System;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// A volume component that holds settings for the Color Adjustments effect.
    /// </summary>
    [Serializable, VolumeComponentMenuForRenderPipeline("Post-processing/Oculus Bloom", typeof(UniversalRenderPipeline))]
    [URPHelpURL("Post-Processing-Color-Adjustments")]
    public sealed class OculusBloom : VolumeComponent, IPostProcessComponent
    {
        /// <summary>
        /// Set the level of brightness to filter out pixels under this level.
        /// This value is expressed in gamma-space.
        /// A value above 0 will disregard energy conservation rules.
        /// </summary>
        [Header("Bloom")]
        [Tooltip("Filters out pixels under this level of brightness. Value is in gamma-space.")]
        public MinFloatParameter threshold = new MinFloatParameter(0.9f, 0f);

        /// <summary>
        /// Controls the strength of the bloom filter.
        /// </summary>
        [Tooltip("Strength of the bloom filter.")]
        public MinFloatParameter intensity = new MinFloatParameter(0f, 0f);

        /// <summary>
        /// Controls the extent of the veiling effect.
        /// </summary>
        [Tooltip("Set the radius of the bloom effect.")]
        public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0f, 1f);


        /// <summary>
        /// Set the maximum intensity that Unity uses to calculate Bloom.
        /// If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.
        /// </summary>
        [Tooltip("Set the maximum intensity that Unity uses to calculate Bloom. If pixels in your Scene are more intense than this, URP renders them at their current intensity, but uses this intensity value for the purposes of Bloom calculations.")]
        public MinFloatParameter clamp = new MinFloatParameter(65472f, 0f);

        /// <summary>
        /// Specifies the tint of the bloom filter.
        /// </summary>
        [Tooltip("Use the color picker to select a color for the Bloom effect to tint to.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);


        /// <summary>
        /// Controls the maximum number of iterations in the effect processing sequence.
        /// </summary>
        [Tooltip("The maximum number of iterations in the effect processing sequence."), AdditionalProperty]
        public ClampedIntParameter maxIterations = new ClampedIntParameter(6, 2, 8);

        /// <inheritdoc/>
        public bool IsActive() => intensity.value > 0f;


        /// <inheritdoc/>
        public bool IsTileCompatible() => true;
    }
}
