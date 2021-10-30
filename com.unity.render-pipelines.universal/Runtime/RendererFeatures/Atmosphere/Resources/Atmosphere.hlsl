#include "Math.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"

struct appdata
{
    float4 vertex : POSITION;
    float4 uv : TEXCOORD0;
};

struct v2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewVector : TEXCOORD1;
};

v2f vert(appdata v)
{
    v2f output;
    output.pos = TransformObjectToHClip(v.vertex);
    output.uv = v.uv;
					// Camera space matches OpenGL convention where cam forward is -z. In unity forward is positive z.
					// (https://docs.unity3d.com/ScriptReference/Camera-cameraToWorldMatrix.html)
    float3 viewVector = mul(unity_CameraInvProjection, float4(v.uv.xy * 2 - 1, 0, -1));
    output.viewVector = mul(unity_CameraToWorld, float4(viewVector, 0));
    return output;
}

			//float4 testColor;

sampler2D _BlueNoise;
float4 _BlueNoiseParams;
#define _BlueNoiseSize _BlueNoiseParams.xy

sampler2D _MainTex;
sampler2D _BakedOpticalDepth;

float3 planetCentre;
float atmosphereRadius;
float oceanRadius;
float planetRadius;

			// Paramaters
int numInScatteringPoints;
int numOpticalDepthPoints;
float intensity;
float4 scatteringCoefficients;
float ditherStrength;
float ditherScale;
float densityFalloff;

float _RayOffset;
float _ShadowStrength;


float2 pixelPos(float2 uv)
{
    float width = _ScreenParams.x;
    float height = _ScreenParams.y;
				//float minDim = min(width, height);
    //float scale = 1000;
    float x = uv.x * width;
    float y = uv.y * height;
    return float2(x, y);
}

float2 blueNoiseUV(float2 uv)
{
    float2 pixel = pixelPos(uv);
    return pixel / _BlueNoiseSize;
}


float nrand(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

half shadowAtPoint(float3 positionWorldSpace)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWorldSpace);
    Light mainLight = GetMainLight(shadowCoord);

    half shadow = mainLight.shadowAttenuation;
    return shadow;
}
		
float densityAtPoint(float3 densitySamplePoint)
{
    float heightAboveSurface = length(densitySamplePoint - planetCentre) - planetRadius;
    float height01 = heightAboveSurface / (atmosphereRadius - planetRadius);
    float localDensity = exp(-height01 * densityFalloff) * (1 - height01);
    return localDensity;
}
			
float opticalDepth(float3 rayOrigin, float3 rayDir, float rayLength)
{
    float3 densitySamplePoint = rayOrigin;
    float stepSize = rayLength / (numOpticalDepthPoints - 1);
    float opticalDepth = 0;

    for (int i = 0; i < numOpticalDepthPoints; i++)
    {
        float localDensity = densityAtPoint(densitySamplePoint);
        opticalDepth += localDensity * stepSize;
        densitySamplePoint += rayDir * stepSize;
    }
    return opticalDepth;
}

float opticalDepthBaked(float3 rayOrigin, float3 rayDir)
{
    float height = length(rayOrigin - planetCentre) - planetRadius;
    float height01 = saturate(height / (atmosphereRadius - planetRadius));

    float uvX = 1 - (dot(normalize(rayOrigin - planetCentre), rayDir) * .5 + .5);
    return tex2Dlod(_BakedOpticalDepth, float4(uvX, height01, 0, 0));
}

float opticalDepthBaked2(float3 rayOrigin, float3 rayDir, float rayLength)
{
    float3 endPoint = rayOrigin + rayDir * rayLength;
    float d = dot(rayDir, normalize(rayOrigin - planetCentre));
    float opticalDepth = 0;

    const float blendStrength = 1.5;
    float w = saturate(d * blendStrength + .5);
				
    float d1 = opticalDepthBaked(rayOrigin, rayDir) - opticalDepthBaked(endPoint, rayDir);
    float d2 = opticalDepthBaked(endPoint, -rayDir) - opticalDepthBaked(rayOrigin, -rayDir);

    opticalDepth = lerp(d2, d1, w);
    return opticalDepth;
}
			
float3 calculateLight(float3 rayOrigin, float3 rayDir, float rayLength, float3 originalCol, float2 uv)
{
				//float blueNoise = tex2Dlod(_BlueNoise, float4(squareUV(uv) * ditherScale,0,0));
    float rand1 = nrand(_Time.xy);
    //float rand2 = tex2D(_BlueNoise, squareUV(uv + rand1)).x * 2 - 1;
    float blueNoise = tex2Dlod(_BlueNoise, float4(blueNoiseUV(uv + rand1) * ditherScale, 0, 0));
    float dither = (blueNoise - 0.5) * ditherStrength;
    float rand2 = blueNoise * 2 - 1;
    
				
    float stepSize = rayLength / (numInScatteringPoints - 1);
    float3 stepVec = rayDir * stepSize;
    float3 randomOffset = rand2 * stepVec * _RayOffset;
    float3 inScatterPoint = rayOrigin + randomOffset;
    float3 inScatteredLight = 0;
    float viewRayOpticalDepth = 0;
    
    float3 dirToSun = _MainLightPosition.xyz;

    for (int i = 0; i < numInScatteringPoints; i++)
    {
        float sunRayLength = raySphere(planetCentre, atmosphereRadius, inScatterPoint, dirToSun).y;
        float sunRayOpticalDepth = opticalDepthBaked(inScatterPoint + dirToSun * ditherStrength, dirToSun);
        float localDensity = densityAtPoint(inScatterPoint);
        viewRayOpticalDepth = opticalDepthBaked2(rayOrigin, rayDir, stepSize * i);
        float3 transmittance = exp(-(sunRayOpticalDepth + viewRayOpticalDepth) * scatteringCoefficients);

					//half shadow = shadowAtPoint(inScatterPoint) * 0.5 * (blueNoise * 0.1 + 1) + 0.5;
        half shadow = lerp(1, shadowAtPoint(inScatterPoint), _ShadowStrength);

        inScatteredLight += localDensity * transmittance * shadow;
        inScatterPoint += stepVec;
    }
    inScatteredLight *= scatteringCoefficients * intensity * stepSize / planetRadius;
    inScatteredLight += blueNoise * 0.01;

				// Attenuate brightness of original col (i.e light reflected from planet surfaces)
				// This is a hacky mess, TODO: figure out a proper way to do this
    const float brightnessAdaptionStrength = 0.15;
    const float reflectedLightOutScatterStrength = 3;
    float brightnessAdaption = dot(inScatteredLight, 1) * brightnessAdaptionStrength;
    float brightnessSum = viewRayOpticalDepth * intensity * reflectedLightOutScatterStrength + brightnessAdaption;
    float reflectedLightStrength = exp(-brightnessSum);
    float hdrStrength = saturate(dot(originalCol, 1) / 3 - 1);
    reflectedLightStrength = lerp(reflectedLightStrength, 1, hdrStrength);
    float3 reflectedLight = originalCol * reflectedLightStrength;

    float3 finalCol = reflectedLight + inScatteredLight;

				
    return finalCol;
}



float4 frag(v2f i) : SV_Target
{

    float4 originalCol = tex2D(_MainTex, i.uv);
    float sceneDepthNonLinear = SampleSceneDepth(i.uv);
    float sceneDepth = LinearEyeDepth(sceneDepthNonLinear, _ZBufferParams) * length(i.viewVector);
				//return sceneDepth;
				
    float3 rayOrigin = _WorldSpaceCameraPos;
    float3 rayDir = normalize(i.viewVector);
				
    float dstToOcean = raySphere(planetCentre, oceanRadius, rayOrigin, rayDir);
    float dstToSurface = min(sceneDepth, dstToOcean);
				
    float2 hitInfo = raySphere(planetCentre, atmosphereRadius, rayOrigin, rayDir);
    float dstToAtmosphere = hitInfo.x;
    float dstThroughAtmosphere = min(hitInfo.y, dstToSurface - dstToAtmosphere);
				
    if (dstThroughAtmosphere > 0)
    {
        const float epsilon = 0.0001;
        float3 pointInAtmosphere = rayOrigin + rayDir * (dstToAtmosphere + epsilon);
        float3 light = calculateLight(pointInAtmosphere, rayDir, dstThroughAtmosphere - epsilon * 2, originalCol, i.uv);
        return float4(light, 1);
    }
    return originalCol;
}