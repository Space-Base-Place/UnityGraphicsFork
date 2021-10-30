using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    [Range(0,2)] public float inscatteringScale = 0.5f;

    [HideInInspector] public Texture2D _BlueNoise;
    [HideInInspector] public float ditherStrength;
    [HideInInspector] public float ditherScale;
    [HideInInspector] public int numInScatteringPoints;
    [HideInInspector] public int numOpticalDepthPoints;
    [HideInInspector] public int textureSize;

    [HideInInspector] public float noiseOffset;
    [HideInInspector] public float shadowStrength;

    [HideInInspector] public Material material;
    RenderTexture bakedOpticalDepth;

    private void OnValidate()
    {
        Setup();
    }

    public void Setup()
    {
        var shader = Shader.Find("Hidden/Atmosphere");

        if (shader == null)
            return;

        material = new Material(shader);

        material.SetVector("planetCentre", planetCentre);
        material.SetFloat("atmosphereRadius", atmosphereRadius);
        material.SetFloat("oceanRadius", oceanRadius);
        material.SetFloat("planetRadius", planetRadius);

        material.SetVector("scatteringCoefficients", PreComputeScattering(scatteringWavelengths));
        material.SetFloat("intensity", intensity);
        material.SetFloat("densityFalloff", densityFalloff);
        material.SetFloat("scatteringStrength", scatteringStrength);
        material.SetFloat("inscatteringScale", inscatteringScale);

        material.SetTexture("_BlueNoise", _BlueNoise);
        Vector4 BlueNoiseParams = new Vector4(_BlueNoise.width, _BlueNoise.height, 0, 0);
        material.SetVector("_BlueNoiseParams", BlueNoiseParams);

        material.SetFloat("ditherStrength", ditherStrength);
        material.SetFloat("ditherScale", ditherScale);
        material.SetInt("numInScatteringPoints", numInScatteringPoints);
        material.SetInt("numOpticalDepthPoints", numOpticalDepthPoints);

        material.SetFloat("_RayOffset", noiseOffset);
        material.SetFloat("_ShadowStrength", shadowStrength);

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
        bakedOpticalDepth = new RenderTexture(textureSize, textureSize, 0)
        {
            filterMode = FilterMode.Bilinear,
            enableRandomWrite = true
        };
        bakedOpticalDepth.Create();

        var computeShader = Resources.Load<ComputeShader>("AtmosphereTexture");
        computeShader.SetInt("textureSize", textureSize);
        computeShader.SetInt("numOutScatteringSteps", numOpticalDepthPoints);
        computeShader.SetFloat("atmosphereRadius", material.GetFloat("inscatteringScale") + 1);
        computeShader.SetFloat("densityFalloff", material.GetFloat("densityFalloff"));
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
