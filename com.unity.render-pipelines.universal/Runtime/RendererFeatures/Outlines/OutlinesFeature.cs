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
        [Min(0)] public float depthThresholdWidth = 0.2f;
        [Min(0)] public float normalThreshold = 0.4f;
        [Min(0)] public float normalThresholdWidth = 0.4f;
        [Min(0)] public float depthNormalThreshold = 0.5f;
        [Min(0)] public float depthNormalThresholdScale = 7f;
        public RenderPassEvent RenderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        public bool sobelFilter;
    }

    public Settings settings = new Settings();


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
        outlinesPass.renderPassEvent = settings.RenderPassEvent - 2;

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






    class OutlinesPass : ScriptableRenderPass
    {
        private Material material;
        MaterialPropertyBlock mpb;

        private RenderTargetIdentifier renderTarget;
        private RenderTargetIdentifier depthTarget;
        private RenderTargetHandle tempRenderTarget;
        private RenderTargetHandle tempDepthTarget;

        public void Setup(Settings settings)
        {
            var shader = Shader.Find("Hidden/Outlines");

            if (shader == null)
            {
                Debug.LogError("Shader is null");
                return;
            }

            material = new Material(shader);
            mpb = new();
            mpb.SetColor(OutlinesIDs._Color, settings.color);
            mpb.SetFloat(OutlinesIDs._Scale, settings.scale);
            mpb.SetFloat(OutlinesIDs._DepthThreshold, settings.depthThreshold);
            mpb.SetFloat(OutlinesIDs._DepthThresholdWidth, settings.depthThresholdWidth);
            mpb.SetFloat(OutlinesIDs._NormalThreshold, settings.normalThreshold);
            mpb.SetFloat(OutlinesIDs._NormalThresholdWidth, settings.normalThresholdWidth);
            mpb.SetFloat(OutlinesIDs._DepthNormalThreshold, settings.depthNormalThreshold);
            mpb.SetFloat(OutlinesIDs._DepthNormalThresholdScale, settings.depthNormalThresholdScale);

            LocalKeyword sobelFilter = new(shader, OutlinesIDs.SOBEL_FILTER);
            material.SetKeyword(sobelFilter, settings.sobelFilter);

            tempRenderTarget.Init("_TempRenderTarget");
            tempDepthTarget.Init("_TempDepthTarget");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;
            depthTarget = renderingData.cameraData.renderer.cameraDepthTarget;
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;

            Debug.Assert(descriptor.height > 0);
            Debug.Assert(descriptor.msaaSamples > 0);

            cmd.GetTemporaryRT(tempRenderTarget.id, descriptor);
            descriptor.colorFormat = RenderTextureFormat.Depth;
            descriptor.depthBufferBits = 32; //TODO: do we really need this. double check;
            descriptor.msaaSamples = 1;
            cmd.GetTemporaryRT(tempDepthTarget.id, descriptor, FilterMode.Point);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }

            
            CommandBuffer cmd = CommandBufferPool.Get("Outlines");
            
            cmd.SetGlobalTexture(OutlinesIDs._MainTex, renderTarget);

            cmd.SetRenderTarget(tempRenderTarget.Identifier(), tempDepthTarget.Identifier());

            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, OutlinesIDs.OutlinesPassIndex, mpb);
            //CoreUtils.DrawFullScreen(cmd, material, tempRenderTarget.id, mpb, OutlinesIDs.OutlinesPassIndex);

            cmd.SetGlobalTexture("_CameraDepthTexture", tempDepthTarget.Identifier());
            //cmd.SetRenderTarget(depthTarget, depthTarget);

            //cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, OutlinesIDs.DepthOnlyPassIndex, mpb);

            Blit(cmd, tempRenderTarget.Identifier(), renderTarget);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(tempRenderTarget.id);
            cmd.ReleaseTemporaryRT(tempDepthTarget.id);
        }

    }

}

internal static class OutlinesIDs
{
    internal static int _MainTex = Shader.PropertyToID("_MainTex");
    internal static int _CanvasTex = Shader.PropertyToID("_CanvasTex");
    internal static int _Color = Shader.PropertyToID("_Color");
    internal static int _FlatColor = Shader.PropertyToID("_FlatColor");
    internal static int _Scale = Shader.PropertyToID("_Scale");
    internal static int _DepthThreshold = Shader.PropertyToID("_DepthThreshold");
    internal static int _DepthThresholdWidth = Shader.PropertyToID("_DepthThresholdWidth");
    internal static int _NormalThreshold = Shader.PropertyToID("_NormalThreshold");
    internal static int _NormalThresholdWidth = Shader.PropertyToID("_NormalThresholdWidth");
    internal static int _DepthNormalThreshold = Shader.PropertyToID("_DepthNormalThreshold");
    internal static int _DepthNormalThresholdScale = Shader.PropertyToID("_DepthNormalThresholdScale");

    internal static string SOBEL_FILTER = "SOBEL_FILTER";

    internal static int OutlinesPassIndex = 0;
    internal static int CopyDepthPassIndex = 1;
    internal static int DepthOnlyPassIndex = 2;
    internal static int FlatColorPassIndex = 3;
    internal static int SimpleSobelPassIndex = 4;
}
