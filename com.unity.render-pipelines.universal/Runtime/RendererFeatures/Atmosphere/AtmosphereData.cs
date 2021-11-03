using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

[CreateAssetMenu(fileName = "Atmosphere", menuName = "Rendering/Atmosphere", order = 1)]
public class AtmosphereData : ScriptableObject
{
    [Header("Size")]
    public Vector3 planetCentre;
    [Min(0)] public float atmosphereRadius = 300;
    [Min(0)] public float oceanRadius = 150;
    [Min(0)] public float planetRadius = 180;

    [Header("Scattering")]
    public Vector3 scatteringWavelengths = new Vector3(800, 530, 460);
    [Range(0,1)] public float intensity = 0.2f;
    [Min(0)] public float densityFalloff = 5;
    [Min(0)] public float scatteringStrength = 50;


    public Material Material { get => material; }
    private Material material;
    RenderTexture bakedOpticalDepth;

    private void OnValidate()
    {
        Setup();
    }

    public void Setup()
    {
        if (material == null)
        {
            var shader = Shader.Find("Hidden/Atmosphere");

            if (shader == null)
                return;

            material = new Material(shader);
        }

        var blueNoiseTex = Resources.Load<Texture2D>("HDR_L_0");
        if (blueNoiseTex == null) Debug.LogError("Can't find noise texture");

        material.SetTexture("_BlueNoise", blueNoiseTex);

        material.SetFloat("atmosphereRadius", atmosphereRadius);
        material.SetFloat("oceanRadius", oceanRadius);
        material.SetFloat("planetRadius", planetRadius);

        material.SetVector("scatteringCoefficients", PreComputeScattering(scatteringWavelengths));
        material.SetFloat("intensity", intensity);
        material.SetFloat("densityFalloff", densityFalloff);
        material.SetFloat("scatteringStrength", scatteringStrength);

        BakeOpticalDepth();
    }

    public Vector3 PreComputeScattering(Vector3 wavelengths)
    {
        float scatterX = Mathf.Pow(400 / wavelengths.x, 4);
        float scatterY = Mathf.Pow(400 / wavelengths.y, 4);
        float scatterZ = Mathf.Pow(400 / wavelengths.z, 4);
        return new Vector3(scatterX, scatterY, scatterZ) * scatteringStrength;
    }

    public void BakeOpticalDepth()
    {
        const int textureSize = 128;
        const int numOpticalDepthPoints = 10;

        bakedOpticalDepth = new RenderTexture(textureSize, textureSize, 0)
        {
            filterMode = FilterMode.Bilinear,
            enableRandomWrite = true
        };
        bakedOpticalDepth.Create();


        var computeShader = Resources.Load<ComputeShader>("AtmosphereTexture");
        computeShader.SetInt("textureSize", textureSize);
        computeShader.SetInt("numOutScatteringSteps", numOpticalDepthPoints);
        computeShader.SetFloat("densityFalloff", densityFalloff);
        computeShader.SetTexture(0, "Result", bakedOpticalDepth);
        computeShader.Dispatch(0, textureSize, textureSize, 1);

        material.SetTexture("_BakedOpticalDepth", bakedOpticalDepth);
    }

    public void UpdatePlanetCentre(Vector3 newCentre)
    {
        planetCentre = newCentre;
        if (material != null)
            material.SetVector("planetCentre", planetCentre);
    }
}
