using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Renders the post-processing effect stack.
    /// </summary>
    internal class OculusBloomPostProcessPass : ScriptableRenderPass
    {
        RenderTextureDescriptor m_Descriptor;
        RTHandle m_Source;
        RTHandle m_Destination;

        RTHandle[] m_BloomMipDown;
        RTHandle[] m_BloomMipUp;

        RTHandle m_TempTarget;
        RTHandle m_TempTarget2;

        const string k_RenderPostProcessingTag = "Render Oculus Bloom PostProcessing Effects";
        private static readonly ProfilingSampler m_ProfilingRenderPostProcessing = new ProfilingSampler(k_RenderPostProcessingTag);

        PostProcessData m_Data;


        Bloom m_Bloom;

        // TODO: Add custom color adjustments here
        // TODO: Add custom tonemapping here
        ColorAdjustments m_ColorAdjustments;
        Tonemapping m_Tonemapping;

        // Misc
        const int k_MaxPyramidSize = 16;
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
        public OculusBloomPostProcessPass(RenderPassEvent evt, PostProcessData data, Material blitMaterial, Material bloomMaterial, ref PostProcessParams postProcessParams)
        {
            base.profilingSampler = new ProfilingSampler(nameof(OculusBloomPostProcessPass));
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
            m_Bloom = stack.GetComponent<Bloom>();
            m_ColorAdjustments = stack.GetComponent<ColorAdjustments>();
            m_Tonemapping = stack.GetComponent<Tonemapping>();

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
            bool isSceneViewCamera = cameraData.isSceneViewCamera;

            // Don't use these directly unless you have a good reason to, use GetSource() and
            // GetDestination() instead
            RTHandle source = m_Source;
            RTHandle destination = null;

            RTHandle GetSource() => source;


            RTHandle GetDestination()
            {
                if (destination == null)
                {
                    RenderingUtils.ReAllocateIfNeeded(ref m_TempTarget, GetCompatibleDescriptor(), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempTarget");
                    destination = m_TempTarget;
                }
                else if (destination == m_Source && m_Descriptor.msaaSamples > 1)
                {
                    // Avoid using m_Source.id as new destination, it may come with a depth buffer that we don't want, may have MSAA that we don't want etc
                    RenderingUtils.ReAllocateIfNeeded(ref m_TempTarget2, GetCompatibleDescriptor(), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempTarget2");
                    destination = m_TempTarget2;
                }
                return destination;
            }


            // Setup projection matrix for cmd.DrawMesh()
            cmd.SetGlobalMatrix(ShaderConstants._FullscreenProjMat, GL.GetGPUProjectionMatrix(Matrix4x4.identity, true));


            ProfilingSampler oculusPPSampler = new ProfilingSampler("OculusBloom PP Sampler");

            // Combined post-processing stack

            using (new ProfilingScope(cmd, oculusPPSampler))
            {
                // Reset uber keywords

                // Bloom goes first
                bool bloomActive = m_Bloom.IsActive();
                if (bloomActive)
                {
                    using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.Bloom)))
                        SetupBloom(cmd, GetSource());
                }

                // Done with Uber, blit it
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

                RenderingUtils.FinalBlit(cmd, ref cameraData, GetSource(), cameraTargetHandle, colorLoadAction, RenderBufferStoreAction.Store, m_FinalBlitMaterial, 0);
                renderer.ConfigureCameraColorTarget(cameraTargetHandle);

            }
        }

        #region Bloom

        int tw;
        int th;
        int maxSize;
        int iterations;
        int mipCount;
        float threshold;
        float thresholdKnee;
        float thresholdNumerator;
        float scatter;
        const float bloomRenderSizeScalar = 0.5f;

        void ConfigureBloom()
        {

            tw = m_Descriptor.width;// >> 1;
            th = m_Descriptor.height;// >> 1;

            // Determine the iteration count
            maxSize = Mathf.Max(tw, th);
            iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
            mipCount = Mathf.Clamp(iterations, 1, k_MaxPyramidSize);

            // Pre-filtering parameters
            threshold = Mathf.GammaToLinearSpace(m_Bloom.threshold.value);
            thresholdKnee = threshold * 0.5f;
            thresholdNumerator = 4.0f * thresholdKnee + 0.0001f;
            scatter = Mathf.Lerp(0.05f, 0.95f, m_Bloom.scatter.value);

            // Material setup
            m_BloomMaterial.SetVector(ShaderConstants._Bloom_Params, new Vector4(scatter, threshold, thresholdKnee, thresholdNumerator));
            m_FinalBlitMaterial.SetFloat(ShaderConstants._Bloom_Intenisty, m_Bloom.intensity.value);
        }

        void SetupBloom(CommandBuffer cmd, RTHandle source)
        {
            ConfigureBloom();

            var desc = GetStereoCompatibleDescriptor(tw, th, m_DefaultHDRFormat);

            // bilinear performs first basic blur at half res
            // this way we do not discard too many pixels before the blur passes
            cmd.GetTemporaryRT(ShaderConstants._BloomMipDown[0], desc, FilterMode.Bilinear);
            cmd.GetTemporaryRT(ShaderConstants._BloomMipUp[0], desc, FilterMode.Bilinear);

            cmd.Blit(source, BlitDstDiscardContent(cmd, ShaderConstants._BloomMipDown[0]), m_BloomMaterial, 0);

            // Downsample - gaussian pyramid
            // we go down again before we blur
            int lastDown = ShaderConstants._BloomMipDown[0];
            for (int i = 1; i < mipCount - 1; i++)
            {
                tw = Mathf.Max(1, tw >> 1);
                th = Mathf.Max(1, th >> 1);

                int mipDown = ShaderConstants._BloomMipDown[i];
                int mipUp = ShaderConstants._BloomMipUp[i];

                desc.width = tw;
                desc.height = th;

                cmd.GetTemporaryRT(mipDown, desc, FilterMode.Bilinear);
                cmd.GetTemporaryRT(mipUp, desc, FilterMode.Bilinear);

                // Classic two pass gaussian blur - use mipUp as a temporary target
                //   First pass does 2x downsampling + 9-tap gaussian using a 5-tap filter + bilinear filtering
                //   Second pass does 9-tap gaussian using a 5-tap filter + bilinear filtering
                cmd.Blit(lastDown, BlitDstDiscardContent(cmd, mipUp), m_BloomMaterial, 1);
                cmd.Blit(mipUp, BlitDstDiscardContent(cmd, mipDown), m_BloomMaterial, 2);
                lastDown = mipDown;
            }

            for (int i = mipCount - 3; i >= 0; i--)
            {
                int lowMip = (i == mipCount - 2) ? ShaderConstants._BloomMipDown[i + 1] : ShaderConstants._BloomMipUp[i + 1];
                int highMip = ShaderConstants._BloomMipDown[i];
                // int highMip = (i == 0) ? ShaderConstants._BloomMipDown[2] : ShaderConstants._BloomMipDown[i];

                int dst = ShaderConstants._BloomMipUp[i];

                cmd.SetGlobalTexture(ShaderConstants._MainTexLowMip, lowMip);
                cmd.Blit(highMip, BlitDstDiscardContent(cmd, dst), m_BloomMaterial, 3);
            }

            // Cleanup
            for (int i = 0; i < k_MaxPyramidSize; i++)
            {
                cmd.ReleaseTemporaryRT(ShaderConstants._BloomMipDown[i]);
                if (i > 0) cmd.ReleaseTemporaryRT(ShaderConstants._BloomMipUp[i]);
            }


            cmd.SetGlobalTexture(ShaderConstants._Bloom_Texture, ShaderConstants._BloomMipUp[0]);

        }

        #endregion



        #region Color Grading

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
            public static readonly int _Bloom_RGBM = Shader.PropertyToID("_Bloom_RGBM");
            public static readonly int _Bloom_Texture = Shader.PropertyToID("_Bloom_Texture");

            public static readonly int _TempNoMSAATex = Shader.PropertyToID("_TempNoMSAATex");
            public static readonly int _FullscreenProjMat = Shader.PropertyToID("_FullscreenProjMat");
            public static readonly int _MSAACopyString = Shader.PropertyToID("_MSAACopyString");
            public static readonly int _BloomFilter = Shader.PropertyToID("_BloomFilter");
            public static int[] _BloomMipUp;
            public static int[] _BloomMipDown;
        }

        #endregion
    }
}
