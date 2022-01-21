using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

public class OutlinesFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Color color;
        [Min(0)] public float scale = 1;
        [Min(0)] public float depthThreshold = 0.2f;
        [Min(0)] public float normalThreshold = 0.4f;
        [Min(0)] public float depthNormalThreshold = 0.5f;
        [Min(0)] public float depthNormalThresholdScale = 7f;
    }

    public Settings settings = new Settings();

    class OutlinesPass : ScriptableRenderPass
    {
        private Material material;

        private RenderTargetIdentifier renderTarget;
        private RenderTargetIdentifier depthTarget;
        private RenderTargetHandle tempRenderTarget;
        private RenderTargetHandle tempDepthTarget;
        private RenderTargetHandle tempMotionVectors;

        static int _MainTex = Shader.PropertyToID("_MainTex");
        static int _DepthTex = Shader.PropertyToID("_DepthTex");

        static int cameraDepthTexture = Shader.PropertyToID("_CameraDepthTexture");
        static int motionVectorsTexture = Shader.PropertyToID("_MotionVectorTexture");

        public void Setup(Settings settings)
        {
            var shader = Shader.Find("Hidden/Outlines");

            if (shader == null)
            {
                Debug.LogError("Shader is null");
                return;
            }

            material = new Material(shader);
            material.SetColor("_Color", settings.color);
            material.SetFloat("_Scale", settings.scale);
            material.SetFloat("_DepthThreshold", settings.depthThreshold);
            material.SetFloat("_NormalThreshold", settings.normalThreshold);
            material.SetFloat("_DepthNormalThreshold", settings.depthNormalThreshold);
            material.SetFloat("_DepthNormalThresholdScale", settings.depthNormalThresholdScale);

            tempRenderTarget.Init("_TempRenderTarget");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;
            depthTarget = renderingData.cameraData.renderer.cameraDepthTarget;
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;

            cmd.GetTemporaryRT(tempRenderTarget.id, descriptor);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get("Outlines");

            cmd.SetRenderTarget(tempRenderTarget.Identifier());
            cmd.SetGlobalTexture(_MainTex, renderTarget);
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, 0);
            Blit(cmd, tempRenderTarget.Identifier(), renderTarget);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(tempRenderTarget.id);
        }


        /// <summary>
        /// Returns true if new target created
        /// </summary>
        private bool EnsureRenderTarget(ref RenderTexture rt, int width, int height, RenderTextureFormat format, FilterMode filterMode, int depthBits = 0, int antiAliasing = 1)
        {
            if (rt != null && (rt.width != width || rt.height != height || rt.format != format || rt.filterMode != filterMode || rt.antiAliasing != antiAliasing))
            {
                RenderTexture.ReleaseTemporary(rt);
                rt = null;
            }
            if (rt == null)
            {
                rt = RenderTexture.GetTemporary(width, height, depthBits, format, RenderTextureReadWrite.Default, antiAliasing);
                rt.filterMode = filterMode;
                rt.wrapMode = TextureWrapMode.Clamp;
                //rt.enableRandomWrite = true;
                return true;// new target
            }
            return false;// same target
        }

        /// <summary>
        /// Returns true if new target created
        /// </summary>
        private bool EnsureRenderTarget(ref RenderTexture rt, RenderTextureDescriptor descriptor, FilterMode filterMode)
        {
            return EnsureRenderTarget(ref rt, descriptor.width, descriptor.height, descriptor.colorFormat, filterMode, descriptor.depthBufferBits, descriptor.msaaSamples);
        }
    }

    OutlinesPass outlinesPass;
    CopyDepthPass copyDepthPass;
    
    RenderTargetHandle m_CameraDepthAttachment;
    RenderTargetHandle m_DepthCopy;

    Material m_CopyDepthMaterial;

    /// <inheritdoc/>
    public override void Create()
    {
        outlinesPass = new OutlinesPass();

        outlinesPass.Setup(settings);
        outlinesPass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox - 2;

        //m_CameraDepthAttachment.Init("_CameraDepthTexture");
        //m_DepthCopy.Init("_DepthCopy");
        //m_CopyDepthMaterial = CoreUtils.CreateEngineMaterial("Hidden/Universal Render Pipeline/CopyDepth");
        //copyDepthPass = new CopyDepthPass(RenderPassEvent.AfterRenderingSkybox + 3, m_CopyDepthMaterial);
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Preview)
            return;

        renderer.EnqueuePass(outlinesPass);
        
        //copyDepthPass.Setup(m_DepthCopy, m_CameraDepthAttachment);
        //renderer.EnqueuePass(copyDepthPass);
    }




}


