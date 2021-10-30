using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphereFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class AtmosphereSettings
    {
        

        [Range(0, 1)] public float noiseOffset = 1;
        [Range(0, 1)] public float shadowStrength = 0.5f;
        public float ditherStrength = 1;
        public float ditherScale = 1;
        public int numInScatteringPoints = 10;
        public int numOpticalDepthPoints = 10;
        public int bakedTextureSize = 128;
    }

    public AtmosphereSettings settings = new AtmosphereSettings();



    class AtmospherePass : ScriptableRenderPass
    {
        private RenderTargetIdentifier renderSource;
        private RenderTargetIdentifier renderTarget;
        int temporaryRTId = Shader.PropertyToID("_TempRT");


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;

            cmd.GetTemporaryRT(temporaryRTId, renderingData.cameraData.cameraTargetDescriptor);
            renderSource = new RenderTargetIdentifier(temporaryRTId);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("Atmosphere");

            foreach (var atmosphere in Atmosphere.allAtmospheres)
            {
                var material = atmosphere.Data.material;
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

        blueNoiseTex = Resources.Load<Texture2D>("HDR_L_0");
        if (blueNoiseTex == null) Debug.LogError("Can't find noise texture");

        m_AtmospherePass = new AtmospherePass();

        ApplyAtmosphereSettings();

        // Configures where the render pass should be injected.
        m_AtmospherePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_AtmospherePass);
    }

    /// <summary>
    /// This is required on serialization to pass settings from the feature to all atmosphere objects
    /// </summary>
    public void ApplyAtmosphereSettings()
    {
        foreach (var atmosphere in Atmosphere.allAtmospheres)
        {
            var atmosphereData = atmosphere.Data;
            atmosphereData._BlueNoise = blueNoiseTex;
            atmosphereData.ditherStrength = settings.ditherStrength;
            atmosphereData.ditherScale = settings.ditherScale;
            atmosphereData.numInScatteringPoints = settings.numInScatteringPoints;
            atmosphereData.numOpticalDepthPoints = settings.numOpticalDepthPoints;
            atmosphereData.textureSize = settings.bakedTextureSize;
            atmosphereData.noiseOffset = settings.noiseOffset;
            atmosphereData.shadowStrength = settings.shadowStrength;

            atmosphereData.Setup();
        }
    }


    /////////////////////////////
    /// Bake Optical Depth
    /////////////////////////////
    public static void BakeOpticalDepth(Material material)
    {
        int textureSize = Mathf.NextPowerOfTwo(material.GetInt("textureSize"));

        var _BakedOpticalDepth = new RenderTexture(textureSize, textureSize, 0)
        {
            filterMode = FilterMode.Bilinear,
            enableRandomWrite = true
        };
        _BakedOpticalDepth.Create();

        var computeShader = Resources.Load<ComputeShader>("AtmosphereTexture");
        computeShader.SetInt("textureSize", textureSize);
        computeShader.SetInt("numOutScatteringSteps", material.GetInt("numOpticalDepthPoints"));
        computeShader.SetFloat("atmosphereRadius", material.GetFloat("inscatteringScale") + 1);
        computeShader.SetFloat("densityFalloff", material.GetFloat("densityFalloff"));
        computeShader.SetTexture(0, "Result", _BakedOpticalDepth);
        computeShader.Dispatch(0, textureSize, textureSize, 1);

        material.SetTexture("_BakedOpticalDepth", _BakedOpticalDepth);
    }
}

