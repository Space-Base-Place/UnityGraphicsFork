using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class AtmosphericIllumination : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public int textureSize;
        [Range(0, 1)] public float sunsetZoneWidth;
        public Color skyColor;
        public Color sunsetColor;
        public Color baseColor;
        [Range(0, 2)] public float globalGIPower;
    }


    public Settings settings = new();


    class AtmosphericIlluminationPrepass : ScriptableRenderPass
    {
        private Settings settings;

        private int textureSize;
        private int threadgroupSize;
        private RenderTexture renderTexture;

        private ComputeShader computeShader;
        private ComputeBuffer lightBuffer;

        private PlanetShineLight[] planetShineLights;

        int kernelMain;
        int kernelClear;

        public AtmosphericIlluminationPrepass(Settings settings) : base()
        {
            base.profilingSampler = new("AtmosphericIllumination");
            this.settings = settings;
            textureSize = Mathf.NextPowerOfTwo(settings.textureSize);
        }

        public void InitializeResourcesIfRequired()
        {
            // Initialise Render Texture
            if (renderTexture == null || !renderTexture.IsCreated())
            {
                renderTexture = new RenderTexture(textureSize, textureSize, 0)
                {
                    enableRandomWrite = true,
                    dimension = TextureDimension.Tex3D,
                    volumeDepth = textureSize
                };
                renderTexture.Create();
            }


            // Initialise Compute Shader
            if (computeShader == null)
            {
                computeShader = Resources.Load<ComputeShader>("GITexture");

                kernelMain = computeShader.FindKernel("CSMain");
                kernelClear = computeShader.FindKernel("Clear");

                computeShader.SetTexture(kernelMain, "Result", renderTexture);
                computeShader.SetTexture(kernelClear, "Result", renderTexture);

                computeShader.SetInt("textureSize", textureSize);
                computeShader.SetFloat("sunsetZoneWidth", settings.sunsetZoneWidth);
                computeShader.SetFloat("globalGIPower", settings.globalGIPower);
                computeShader.SetFloat("textureInflation", AtmosphericIlluminationConstants.TextureInflation);

                computeShader.GetKernelThreadGroupSizes(0, out uint threadGroupX, out uint threadGroupY, out uint threadGroupZ);


                if (threadGroupX != threadGroupY || threadGroupY != threadGroupZ)
                    Debug.LogError("Invalid threadgroup size");

                threadgroupSize = (int)math.ceil(textureSize / (float)threadGroupX);

                ClearRenderTexture();
            }


            if (lightBuffer == null)
                lightBuffer = new ComputeBuffer(AtmosphericIlluminationConstants.MaxGIAffectors, AtmosphericIlluminationConstants.lightStride);

            if (planetShineLights == null)
                planetShineLights = new PlanetShineLight[AtmosphericIlluminationConstants.MaxGIAffectors];

        }

        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            InitializeResourcesIfRequired();
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            var atmosphere = FindClosestAtmosphere(camera);

            if (atmosphere == null || atmosphere.Data == null) // don't render if no data
                return;


            var currentAtmosphere = new CurrentAtmosphere(atmosphere);

            Profiler.BeginSample("Global Illumination Compute");

            UpdateLightBuffer();
            DispatchCompute(ref currentAtmosphere, ref renderingData);

            Profiler.EndSample();

            var cmd = CommandBufferPool.Get("Set GlobalIllumination");

            cmd.SetGlobalTexture(AtmosphericIlluminationConstants.GITexture, renderTexture);
            cmd.SetGlobalVector(AtmosphericIlluminationConstants.GIParams, currentAtmosphere.Params);
            cmd.SetKeyword(AtmosphericIlluminationConstants.GIKeyword, true);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void UpdateLightBuffer()
        {
            var numLights = AtmosphericIlluminationAffector.allAffectors.Count;

            if (numLights > AtmosphericIlluminationConstants.MaxGIAffectors)
                Debug.LogError($"Max Affectors is {AtmosphericIlluminationConstants.MaxGIAffectors}");

            for (int i = 0; i < numLights; i++)
            {
                planetShineLights[i] = new PlanetShineLight(AtmosphericIlluminationAffector.allAffectors[i]);
            }

            lightBuffer.SetData(planetShineLights);
            computeShader.SetBuffer(kernelMain, "lightBuffer", lightBuffer);
            computeShader.SetInt("numLights", numLights);
        }

        private void DispatchCompute(ref CurrentAtmosphere currentAtmosphere, ref RenderingData renderingData)
        {
            var data = currentAtmosphere.Data;

            computeShader.SetVector("params", currentAtmosphere.Params);
            computeShader.SetFloat("surfaceRadius01", data.planetRadius / data.atmosphereRadius);

            Vector3 mainLightDirection;
            if (renderingData.lightData.mainLightIndex >= 0)
            {
                var mainLight = renderingData.lightData.visibleLights[renderingData.lightData.mainLightIndex];
                mainLightDirection = mainLight.localToWorldMatrix * Vector3.forward;
            }
            else
            {
                mainLightDirection = Vector3.forward;
            }

            computeShader.SetVector("mainLightDirection", mainLightDirection);
            computeShader.SetVector("mainLightColor", settings.skyColor);
            computeShader.SetVector("sunsetColor", settings.sunsetColor);
            computeShader.SetVector("baseColor", settings.baseColor);

            computeShader.Dispatch(kernelMain, threadgroupSize, threadgroupSize, threadgroupSize);

            //Debug.Log($"Dispatching compute for {data.name}, threadgroup {threadgroupSize}, mainlight {mainLightDirection}, {settings.skyColor}, params {currentAtmosphere.Params}");

        }

        private void ClearRenderTexture()
        {      
            computeShader.Dispatch(kernelClear, threadgroupSize, threadgroupSize, threadgroupSize);
        }

        public void Dispose()
        {
            if (renderTexture != null)
                renderTexture.Release();
            renderTexture = null;

            lightBuffer?.Dispose();
            lightBuffer = null;
        }

        private Atmosphere FindClosestAtmosphere(Camera camera)
        {
            float closestDist = float.MaxValue;
            Atmosphere closestAtmosphere = null;
            foreach (var atmosphere in Atmosphere.allAtmospheres)
            {
                if (!atmosphere.enabled)
                    continue;
                var dist = (camera.transform.position - atmosphere.transform.position).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestAtmosphere = atmosphere;
                }
            }
            return closestAtmosphere;
        }
    }

    AtmosphericIlluminationPrepass atmosphericIlluminationPrepass;

    /// <inheritdoc/>
    public override void Create()
    {
        if (atmosphericIlluminationPrepass != null)
            atmosphericIlluminationPrepass.Dispose();

        atmosphericIlluminationPrepass = new(settings);

        // Configures where the render pass should be injected.
        atmosphericIlluminationPrepass.renderPassEvent = RenderPassEvent.BeforeRendering;

    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called every frame when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType != CameraType.Preview)
            renderer.EnqueuePass(atmosphericIlluminationPrepass);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && atmosphericIlluminationPrepass != null)
            atmosphericIlluminationPrepass.Dispose();
        atmosphericIlluminationPrepass = null;
    }

    public static class AtmosphericIlluminationConstants
    {
        public const float TextureInflation = 1.2f;
        public const string GITexture = "_GITexture";
        public const string GIParams = "_GIParams";

        private const string gIKeyword = "_USE_ATMOSPHERIC_GLOBAL_ILLUMINATION";
        public static GlobalKeyword GIKeyword = GlobalKeyword.Create(gIKeyword);

        public const int MaxGIAffectors = 4;
        public const int lightStride = sizeof(float) * 3 * 2;
    }

    internal struct PlanetShineLight
    {
        float3 positionWS;
        float3 color;

        public PlanetShineLight(AtmosphericIlluminationAffector gIAffector)
        {
            positionWS = gIAffector.transform.position;
            var color = gIAffector.color;
            this.color = new float3(color.r, color.g, color.b);
        }
    }

    internal struct CurrentAtmosphere
    {
        public AtmosphereData Data;
        public Vector4 Params;

        public CurrentAtmosphere(Atmosphere atmosphere)
        {
            Data = atmosphere.Data;
            Params = new Vector4(
                Data.planetCentre.x,
                Data.planetCentre.y,
                Data.planetCentre.z,
                1 / (Data.atmosphereRadius * AtmosphericIlluminationConstants.TextureInflation));
        }
    }
}


