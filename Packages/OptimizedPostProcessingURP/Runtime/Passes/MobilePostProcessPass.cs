using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Renders the post-processing effect stack.
    /// </summary>
    internal class MobilePostProcessPass : ScriptableRenderPass
    {
        RenderTextureDescriptor m_Descriptor;
        RTHandle m_Source;
        RTHandle m_Destination;

        RTHandle[] m_BloomMipDown;
        RTHandle[] m_BloomMipUp;

        RTHandle m_TempTarget;
        RTHandle m_TempTarget2;

        const string k_RenderPostProcessingTag = "Render Mobile PostProcessing Effects";
        private static readonly ProfilingSampler m_ProfilingRenderPostProcessing = new ProfilingSampler(k_RenderPostProcessingTag);

        PostProcessData m_Data;


        MobileBloom m_Bloom;
        MobileColorAdjustmentsAndToneMapping m_ColorAdjustments;

        // TODO: Add custom color adjustments here
        // TODO: Add custom tonemapping here
        Tonemapping m_Tonemapping;

        // Misc
        const int k_MaxPyramidSize = 12;
        readonly GraphicsFormat m_DefaultHDRFormat;

        Material m_FinalBlitMaterial;
        Material m_BloomMaterial;



        /// <summary>
        /// Creates a new <c>OculusBloomPostProcessPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <param name="data">The <c>PostProcessData</c> resources to use.</param>
        /// <param name="postProcessParams">The <c>PostProcessParams</c> run-time params to use.</param>
        /// <seealso cref="RenderPassEvent"/>
        /// <seealso cref="PostProcessData"/>
        /// <seealso cref="PostProcessParams"/>
        public MobilePostProcessPass(RenderPassEvent evt, PostProcessData data, Material blitMaterial, Material bloomMaterial, ref PostProcessParams postProcessParams)
        {
            base.profilingSampler = new ProfilingSampler(nameof(MobilePostProcessPass));
            renderPassEvent = evt;
            m_Data = data;

            m_FinalBlitMaterial = blitMaterial;
            m_BloomMaterial = bloomMaterial;

            // Bloom pyramid shader ids - can't use a simple stackalloc in the bloom function as we
            // unfortunately need to allocate strings
            ShaderConstants._BloomMipUp = new int[k_MaxPyramidSize];
            ShaderConstants._BloomMipDown = new int[k_MaxPyramidSize];
            m_BloomMipUp = new RTHandle[k_MaxPyramidSize];
            m_BloomMipDown = new RTHandle[k_MaxPyramidSize];

            for (int i = 0; i < k_MaxPyramidSize; i++)
            {
                ShaderConstants._BloomMipUp[i] = Shader.PropertyToID("_BloomMipUp" + i);
                ShaderConstants._BloomMipDown[i] = Shader.PropertyToID("_BloomMipDown" + i);
                // Get name, will get Allocated with descriptor later
                m_BloomMipUp[i] = RTHandles.Alloc(ShaderConstants._BloomMipUp[i], name: "_BloomMipUp" + i);
                m_BloomMipDown[i] = RTHandles.Alloc(ShaderConstants._BloomMipDown[i], name: "_BloomMipDown" + i);
            }

            m_DefaultHDRFormat = GraphicsFormat.B10G11R11_UFloatPack32;
        }


        /// <summary>
        /// Disposes used resources.
        /// </summary>
        public void Dispose()
        {
            foreach (var handle in m_BloomMipDown)
                handle?.Release();
            foreach (var handle in m_BloomMipUp)
                handle?.Release();

            m_TempTarget?.Release();
            m_TempTarget2?.Release();
        }

        /// <summary>
        /// Configures the pass.
        /// </summary>
        /// <param name="baseDescriptor"></param>
        /// <param name="source"></param>
        public void Setup(in RenderTextureDescriptor baseDescriptor, in RTHandle source)
        {
            m_Descriptor = baseDescriptor;
            m_Descriptor.useMipMap = false;
            m_Descriptor.autoGenerateMips = false;
            m_Source = source;
            m_Destination = k_CameraTarget;

        }

        /// <inheritdoc/>
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            overrideCameraTarget = true;
        }

        public bool CanRunOnTile()
        {
            // Check builtin & user effects here
            return false;
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Start by pre-fetching all builtin effect settings we need
            // Some of the color-grading settings are only used in the color grading lut pass
            var stack = VolumeManager.instance.stack;
            m_Bloom = stack.GetComponent<MobileBloom>();
            m_ColorAdjustments = stack.GetComponent<MobileColorAdjustmentsAndToneMapping>();

            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, m_ProfilingRenderPostProcessing))
            {
                Render(cmd, ref renderingData);
            }
        }

        RenderTextureDescriptor GetCompatibleDescriptor()
            => GetCompatibleDescriptor(m_Descriptor.width, m_Descriptor.height, m_Descriptor.graphicsFormat);

        RenderTextureDescriptor GetCompatibleDescriptor(int width, int height, GraphicsFormat format, DepthBits depthBufferBits = DepthBits.None)
            => GetCompatibleDescriptor(m_Descriptor, width, height, format, depthBufferBits);

        internal static RenderTextureDescriptor GetCompatibleDescriptor(RenderTextureDescriptor desc, int width, int height, GraphicsFormat format, DepthBits depthBufferBits = DepthBits.None)
        {
            desc.depthBufferBits = (int)depthBufferBits;
            desc.msaaSamples = 1;
            desc.width = width;
            desc.height = height;
            desc.graphicsFormat = format;
            return desc;
        }

        void Render(CommandBuffer cmd, ref RenderingData renderingData)
        {
            ref CameraData cameraData = ref renderingData.cameraData;
            ref ScriptableRenderer renderer = ref cameraData.renderer;

            // Setup projection matrix for cmd.DrawMesh()
            cmd.SetGlobalMatrix(ShaderConstants._FullscreenProjMat, GL.GetGPUProjectionMatrix(Matrix4x4.identity, true));
            ProfilingSampler oculusPPSampler = new ProfilingSampler("OculusBloom PostProcessing");

            using (new ProfilingScope(cmd, oculusPPSampler))
            {

                // Bloom goes first
                ProfilingSampler oculusBloomSampler = new ProfilingSampler("OculusBloom OptimizedBloom");

                bool bloomActive = m_Bloom != null && m_Bloom.IsActive();
                if (bloomActive)
                {
                    using (new ProfilingScope(cmd, oculusBloomSampler))
                        SetupBloom(cmd, m_Source);
                }

            }

            ResolveToScreen(cmd, renderer, m_Source, cameraData);
        }

        #region Bloom

        void SetupBloom(CommandBuffer cmd, RTHandle source)
        {
            // Start at half-res
            int tw = m_Descriptor.width >> 1;
            int th = m_Descriptor.height >> 1;


            // Determine the iteration count
            int maxSize = Mathf.Max(tw, th);
            int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
            int mipCount = Mathf.Clamp(iterations, 1, m_Bloom.maxIterations.value);


            // Pre-filtering parameters
            float clamp = m_Bloom.clamp.value;
            float threshold = Mathf.GammaToLinearSpace(m_Bloom.threshold.value);
            float thresholdKnee = threshold * 0.5f; // Hardcoded soft knee
            float scatter = Mathf.Lerp(0.05f, 0.95f, m_Bloom.scatter.value);

            // Material setup
            m_BloomMaterial.SetVector(ShaderConstants._Bloom_Params, new Vector4(scatter, clamp, threshold, thresholdKnee));
            m_FinalBlitMaterial.SetFloat(ShaderConstants._Bloom_Intenisty, m_Bloom.intensity.value);
            m_FinalBlitMaterial.SetColor(ShaderConstants._Bloom_Tint, m_Bloom.tint.value);

            var desc = GetStereoCompatibleDescriptor(tw, th, m_DefaultHDRFormat);

            for (int i = 0; i < mipCount; i++)
            {
                RenderingUtils.ReAllocateIfNeeded(ref m_BloomMipUp[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_BloomMipUp[i].name);
                RenderingUtils.ReAllocateIfNeeded(ref m_BloomMipDown[i], desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: m_BloomMipDown[i].name);
                desc.width = Mathf.Max(1, desc.width >> 1);
                desc.height = Mathf.Max(1, desc.height >> 1);
            }


            // cmd.Blit(source, BlitDstDiscardContent(cmd, ShaderConstants._BloomMipDown[0]), m_BloomMaterial, 0);
            Blitter.BlitCameraTexture(cmd, source, m_BloomMipDown[0], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, m_BloomMaterial, 0);

            // Downsample - gaussian pyramid
            var lastDown = m_BloomMipDown[0];
            for (int i = 1; i < mipCount; i++)
            {
                // Classic two pass gaussian blur - use mipUp as a temporary target
                //   First pass does 2x downsampling + 9-tap gaussian
                //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear filtering
                Blitter.BlitCameraTexture(cmd, lastDown, m_BloomMipUp[i], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, m_BloomMaterial, 1);
                Blitter.BlitCameraTexture(cmd, m_BloomMipUp[i], m_BloomMipDown[i], RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, m_BloomMaterial, 2);

                lastDown = m_BloomMipDown[i];
            }

            // Upsample (bilinear by default, HQ filtering does bicubic instead
            for (int i = mipCount - 2; i >= 0; i--)
            {
                var lowMip = (i == mipCount - 2) ? m_BloomMipDown[i + 1] : m_BloomMipUp[i + 1];
                var highMip = m_BloomMipDown[i];
                var dst = m_BloomMipUp[i];

                cmd.SetGlobalTexture(ShaderConstants._MainTexLowMip, lowMip);
                Blitter.BlitCameraTexture(cmd, highMip, dst, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare, m_BloomMaterial, 3);
            }


            // Cleanup
            for (int i = 0; i < k_MaxPyramidSize; i++)
            {
                cmd.ReleaseTemporaryRT(ShaderConstants._BloomMipDown[i]);
                if (i > 0) cmd.ReleaseTemporaryRT(ShaderConstants._BloomMipUp[i]);
            }


            cmd.SetGlobalTexture(ShaderConstants._Bloom_Texture, m_BloomMipUp[0]);

        }

        #endregion



        #region Color Adjustments

        void SetupColorAdjustments()
        {
            m_FinalBlitMaterial.EnableKeyword(ShaderConstants.ColorAdjusments);

            var lmsColorBalance = ColorUtils.ColorBalanceToLMSCoeffs(m_ColorAdjustments.temperature.value, m_ColorAdjustments.tint.value);
            var adjustmentParams = new Vector4(m_ColorAdjustments.gamma.value / 100f + 1f, m_ColorAdjustments.saturation.value / 100f + 1f, m_ColorAdjustments.contrast.value / 100f + 1f, 0.0f);

            m_FinalBlitMaterial.SetVector(ShaderConstants._ColorBalance, lmsColorBalance);
            m_FinalBlitMaterial.SetVector(ShaderConstants._ColorFilter, m_ColorAdjustments.colorFilter.value.linear);
            m_FinalBlitMaterial.SetVector(ShaderConstants._AdjustmentParams, adjustmentParams);

            switch (m_ColorAdjustments.mode.value)
            {

                case TonemappingMode.Neutral:
                    {
                        m_FinalBlitMaterial.EnableKeyword(ShaderKeywordStrings.TonemapNeutral);
                        m_FinalBlitMaterial.DisableKeyword(ShaderKeywordStrings.TonemapACES);

                        break;
                    }
                case TonemappingMode.ACES:
                    {
                        m_FinalBlitMaterial.EnableKeyword(ShaderKeywordStrings.TonemapACES);
                        m_FinalBlitMaterial.DisableKeyword(ShaderKeywordStrings.TonemapNeutral);

                        break;
                    }
                default:
                    {
                        m_FinalBlitMaterial.DisableKeyword(ShaderKeywordStrings.TonemapNeutral);
                        m_FinalBlitMaterial.DisableKeyword(ShaderKeywordStrings.TonemapACES);
                        break; // None
                    }
            }

        }

        #endregion

        #region Final Blit

        void ResolveToScreen(CommandBuffer cmd, ScriptableRenderer renderer, RTHandle source, CameraData cameraData)
        {
            ProfilingSampler oculusColorGradingSampler = new ProfilingSampler("OculusBloom ResolveToScreen");
            using (new ProfilingScope(cmd, oculusColorGradingSampler))
            {
                bool colorGradingActive = m_ColorAdjustments != null && m_ColorAdjustments.IsActive();
                if (colorGradingActive)
                {
                    SetupColorAdjustments();
                }
                else
                {
                    m_FinalBlitMaterial.DisableKeyword(ShaderConstants.ColorAdjusments);
                }

                var colorLoadAction = RenderBufferLoadAction.DontCare;
                if (m_Destination == k_CameraTarget && !cameraData.isDefaultViewport)
                    colorLoadAction = RenderBufferLoadAction.Load;

                // Note: We rendering to "camera target" we need to get the cameraData.targetTexture as this will get the targetTexture of the camera stack.
                // Overlay cameras need to output to the target described in the base camera while doing camera stack.
                RenderTargetIdentifier cameraTargetID = BuiltinRenderTextureType.CameraTarget;
#if ENABLE_VR && ENABLE_XR_MODULE
                if (cameraData.xr.enabled)
                    cameraTargetID = cameraData.xr.renderTarget;
#endif

                // Get RTHandle alias to use RTHandle apis
                RenderTargetIdentifier cameraTarget = cameraData.targetTexture != null ? new RenderTargetIdentifier(cameraData.targetTexture) : cameraTargetID;
                RTHandleStaticHelpers.SetRTHandleStaticWrapper(cameraTarget);
                var cameraTargetHandle = RTHandleStaticHelpers.s_RTHandleWrapper;

                // we require different load and store actions for  previewing the camera target in the Editor
                // When blitting to device we want to set both load and store to DONTCARE to optimize for the Tiled GPU

#if UNITY_EDITOR
                RenderingUtils.FinalBlit(cmd, ref cameraData, m_Source, cameraTargetHandle, colorLoadAction, RenderBufferStoreAction.Store, m_FinalBlitMaterial, 0);
#else
                RenderingUtils.FinalBlit(cmd, ref cameraData, m_Source, cameraTargetHandle, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, m_FinalBlitMaterial, 0);
#endif
                renderer.ConfigureCameraColorTarget(cameraTargetHandle);

            }
        }

        #endregion


        #region Internal utilities

        RenderTextureDescriptor GetStereoCompatibleDescriptor()
            => GetStereoCompatibleDescriptor(m_Descriptor.width, m_Descriptor.height, m_Descriptor.graphicsFormat, m_Descriptor.depthBufferBits);

        RenderTextureDescriptor GetStereoCompatibleDescriptor(int width, int height, GraphicsFormat format, int depthBufferBits = 0)
        {
            // Inherit the VR setup from the camera descriptor
            var desc = m_Descriptor;
            desc.depthBufferBits = depthBufferBits;
            desc.msaaSamples = 1;
            desc.width = width;
            desc.height = height;
            desc.graphicsFormat = format;
            return desc;
        }
        private BuiltinRenderTextureType BlitDstDiscardContent(CommandBuffer cmd, RenderTargetIdentifier rt)
        {

            // I had always assumed we needed to store the frame data to access it later but this is incorrect.
            // storing refers to a per framebuffer per frame write of the blit
            // once the texture has been generated the colour is stored in the texture?

            cmd.SetRenderTarget(new RenderTargetIdentifier(rt, 0, CubemapFace.Unknown, -1),
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
            return BuiltinRenderTextureType.CurrentActive;
        }


        // Precomputed shader ids to same some CPU cycles (mostly affects mobile)
        static class ShaderConstants
        {
            public static readonly int _MainTexLowMip = Shader.PropertyToID("_MainTexLowMip");
            public static readonly int _Bloom_Params = Shader.PropertyToID("_Bloom_Params");
            public static readonly int _Bloom_Intenisty = Shader.PropertyToID("_Bloom_Intensity");
            public static readonly int _Bloom_Tint = Shader.PropertyToID("_Bloom_Tint");
            public static readonly int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");
            public static readonly int _FullscreenProjMat = Shader.PropertyToID("_FullscreenProjMat");

            public static readonly int _ColorBalance = Shader.PropertyToID("_ColorBalance");
            public static readonly int _ColorFilter = Shader.PropertyToID("_ColorFilter");
            public static readonly int _AdjustmentParams = Shader.PropertyToID("_ColorAdjustmentParams");

            public const string ColorAdjusments = "_COLORADJUSTMENTS";

            public static int[] _BloomMipUp;
            public static int[] _BloomMipDown;
        }

        #endregion
    }
}
