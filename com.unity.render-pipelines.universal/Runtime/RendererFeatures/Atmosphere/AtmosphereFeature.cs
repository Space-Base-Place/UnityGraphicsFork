using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphereFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        [Range(0, 1)] public float noiseOffset = 1;
        [Range(0, 1)] public float shadowStrength = 0.5f;
        [Range(0, 1)] public float ditherStrength = 1;
        public float ditherScale = 1;
        public int numInScatteringPoints = 10;
        public int numOpticalDepthPoints = 10;
    }

    public Settings settings = new Settings();



    class AtmospherePass : ScriptableRenderPass
    {
        private RenderTargetIdentifier renderSource;
        private RenderTargetIdentifier renderTarget;
        int temporaryRTId = Shader.PropertyToID("_TempRT");

        private Settings settings;

        public void Setup(Settings settings)
        {
            this.settings = settings;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;

            cmd.GetTemporaryRT(temporaryRTId, renderingData.cameraData.cameraTargetDescriptor);
            renderSource = new RenderTargetIdentifier(temporaryRTId);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Atmosphere");

            bool isSceneCam = renderingData.cameraData.isSceneViewCamera;

            foreach (var atmosphere in Atmosphere.allAtmospheres)
            {
                if (atmosphere.Data == null)
                    continue;

                if (atmosphere.Data.Material == null)
                    atmosphere.Data.Setup();

                var material = atmosphere.Data.Material;

                var ditherStrength = isSceneCam ? 0 : settings.ditherStrength;
                var numInScatteringPoints = isSceneCam ? 50 : settings.numInScatteringPoints;
                var noiseOffset = isSceneCam ? 0 : settings.noiseOffset;

                material.SetFloat("ditherStrength", ditherStrength);
                material.SetInt("numInScatteringPoints", numInScatteringPoints);
                material.SetFloat("_RayOffset", noiseOffset);
                material.SetFloat("ditherScale", settings.ditherScale);
                material.SetFloat("_ShadowStrength", settings.shadowStrength);

                cmd.Blit(renderTarget, renderSource);
                Blit(cmd, renderSource, renderTarget, material);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(temporaryRTId);
        }
    }

    AtmospherePass m_AtmospherePass;

    Texture2D blueNoiseTex;

    /// <inheritdoc/>
    public override void Create()
    {
        name = "Atmosphere";


        m_AtmospherePass = new AtmospherePass();
        m_AtmospherePass.Setup(settings);
        ApplyAtmosphereSettings();

        // Configures where the render pass should be injected.
        m_AtmospherePass.renderPassEvent = settings.renderPassEvent;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Preview)
            return;

        renderer.EnqueuePass(m_AtmospherePass);
    }

    /// <summary>
    /// This is required on serialization to pass settings from the feature to all atmosphere objects
    /// </summary>
    public void ApplyAtmosphereSettings()
    {
        foreach (var atmosphere in Atmosphere.allAtmospheres)
        {
            atmosphere.Data.Setup();
        }
    }



}

