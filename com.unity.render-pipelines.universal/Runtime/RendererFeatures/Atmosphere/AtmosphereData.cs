using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

[CreateAssetMenu(fileName = "Atmosphere", menuName = "Rendering/Atmosphere", order = 1)]
public class AtmosphereData : ScriptableObject
{
    [Header("Size")]
    [HideInInspector] public Vector3 planetCentre;
    [Min(0)] public float atmosphereRadius = 300;
    [Min(0)] public float oceanRadius = 150;
    [Min(0)] public float planetRadius = 180;

    [Header("Scattering")]
    public Vector3 scatteringWavelengths = new Vector3(800, 530, 460);
    [Range(0,1)] public float intensity = 0.2f;
    [Min(0)] public float densityFalloff = 5;
    [Min(0)] public float scatteringStrength = 50;

    const int textureSize = 128;
    //const int numOpticalDepthPoints = 10;

    [HideInInspector]
    public bool fixedRayLength;

    public Material Material { get => material; }
    private Material material;
    private Texture2D blueNoiseTex;
    RenderTexture bakedOpticalDepth;

    private void OnValidate()
    {
        Clear();
        SetupIfRequired();
    }

    public void SetupIfRequired()
    {
        if (material == null)
        {
            var shader = Shader.Find("Hidden/Atmosphere");

            if (shader == null)
            {
                Debug.LogError("Can't find atmosphere shader!", this);
                return;
            }

            material = CoreUtils.CreateEngineMaterial(shader);

            LocalKeyword localKeyword = new(shader, "FIXED_RAY_LENGTH");
            material.SetKeyword(localKeyword, fixedRayLength);

            material.SetFloat("atmosphereRadius", atmosphereRadius);
            material.SetFloat("oceanRadius", oceanRadius);
            material.SetFloat("planetRadius", planetRadius);

            material.SetVector("scatteringCoefficients", PreComputeScattering(scatteringWavelengths));
            material.SetFloat("intensity", intensity);
            material.SetFloat("densityFalloff", densityFalloff);
            //material.SetFloat("scatteringStrength", scatteringStrength);

            // Dither Texture
            var blueNoiseTex = Resources.Load<Texture2D>("HDR_L_0");

            if (blueNoiseTex == null)
                Debug.LogError("Can't find noise texture");

            material.SetTexture("_BlueNoise", blueNoiseTex);

            // Bake Optical Depth
            bakedOpticalDepth = new RenderTexture(textureSize, textureSize, 0)
            {
                graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R32_SFloat,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                autoGenerateMips = false,
                enableRandomWrite = true
            };
            bakedOpticalDepth.Create();

            material.SetTexture("_BakedOpticalDepth", bakedOpticalDepth);

            BakeOpticalDepth();
        }
    }

    public void Clear()
    {
        material = null;

        if (bakedOpticalDepth != null)
            bakedOpticalDepth.Release();
        bakedOpticalDepth = null;
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
        
        var computeShader = Resources.Load<ComputeShader>("AtmosphereTexture");
        computeShader.SetInt("textureSize", textureSize);
        //computeShader.SetInt("numOutScatteringSteps", numOpticalDepthPoints);
        computeShader.SetFloat("densityFalloff", densityFalloff);
        computeShader.SetTexture(0, "Result", bakedOpticalDepth);

        LocalKeyword localKeyword = new(computeShader, "FIXED_RAY_LENGTH");
        computeShader.SetKeyword(localKeyword, fixedRayLength);

        computeShader.Dispatch(0, textureSize, textureSize, 1);
    }

    public void UpdatePlanetCentre(Vector3 newCentre)
    {
        planetCentre = newCentre;
        if (material != null)
            material.SetVector("planetCentre", planetCentre);
    }
}
