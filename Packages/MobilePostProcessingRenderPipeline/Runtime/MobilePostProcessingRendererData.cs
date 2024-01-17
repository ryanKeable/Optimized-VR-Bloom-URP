#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using ShaderKeywordFilter = UnityEditor.ShaderKeywordFilter;
#endif
using System;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Assertions;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Class containing resources needed for the <c>UniversalRenderer</c>.
    /// </summary>
    [Serializable, ReloadGroup, ExcludeFromPreset]
    [URPHelpURL("urp-universal-renderer")]
    public class MobilePostProcessingRendererData : UniversalRendererData, ISerializationCallbackReceiver
    {
#if UNITY_EDITOR
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812")]
        internal class CreateMobilePostProcessingRendererAsset : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                var instance = CreateInstance<MobilePostProcessingRendererData>();
                AssetDatabase.CreateAsset(instance, pathName);
                ResourceReloader.ReloadAllNullIn(instance, UniversalRenderPipelineAsset.packagePath);
                Selection.activeObject = instance;
            }
        }

        [MenuItem("Assets/Create/Rendering/Mobile PostProcessing Rendererer", priority = CoreUtils.Sections.section3 + CoreUtils.Priorities.assetsCreateRenderingMenuPriority + 2)]
        static void CreateMobilePostProcessingRendererData()
        {
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, CreateInstance<CreateMobilePostProcessingRendererAsset>(), "New Custom Universal Renderer Data.asset", null, null);
        }

#endif

        public MobilePostProcessingRendererShaderResources mobilePostProcessingRendererShaders = null;

        const int k_LatestAssetVersion = 2;




        /// <summary>
        /// Class containing shader resources used in URP.
        /// </summary>
        [Serializable, ReloadGroup]
        public sealed class MobilePostProcessingRendererShaderResources
        {
            /// <summary>
            /// Blit shader.
            /// </summary>
            [Reload("Shaders/MobilePostProcessing/MobilePostProcessing_FinalBlit.shader")]
            public Shader finalBlitPS;

            /// <summary>
            /// Copy Depth shader.
            /// </summary>
            [Reload("Shaders/MobilePostProcessing/MobilePostProcessing_Bloom.shader")]
            public Shader bloomPS;


        }


        /// <inheritdoc/>
        protected override ScriptableRenderer Create()
        {
            if (!Application.isPlaying)
            {
                ReloadAllNullProperties();
            }
            return new MobilePostProcessingRendererer(this);
        }

        /// <inheritdoc/>
        protected override void OnEnable()
        {

            if (mobilePostProcessingRendererShaders == null)
                return;

            base.OnEnable();
        }

        private void ReloadAllNullProperties()
        {
#if UNITY_EDITOR
            ResourceReloader.TryReloadAllNullIn(this, UniversalRenderPipelineAsset.packagePath);

            if (postProcessData != null)
                ResourceReloader.TryReloadAllNullIn(postProcessData, UniversalRenderPipelineAsset.packagePath);

#if ENABLE_VR && ENABLE_XR_MODULE
            ResourceReloader.TryReloadAllNullIn(xrSystemData, UniversalRenderPipelineAsset.packagePath);
#endif
#endif
        }

    }
}
