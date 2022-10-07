#ifndef ATMOSPHERIC_ILLUMINATION_INCLUDED
#define ATMOSPHERIC_ILLUMINATION_INCLUDED

struct PlanetShineLight
{
    float3 positionWS;
    float3 color;
    float3 radius; //x: surface, y: atmosphere
};

StructuredBuffer<PlanetShineLight> _PlanetShineLightBuffer;
int _NumPlanetShineLights;


half3 _SunsetColor;
half3 _AGIAmbientColor;
half3 _AGIAmbientColorX;
half3 _AGIAmbientColorY;
half3 _AGIAmbientColorZ;

float _SunsetZoneWidth;
float _GlobalGIPower;
float _AtmosGIPower;

half3 CalculateLightComponent(float3 positionWS, PlanetShineLight planetShineLight, float3 localUp, float3 normal, float heightAboveClosestBody)
{
    float3 lightPos = planetShineLight.positionWS.xyz;
    float lightRadius = planetShineLight.radius.x;
    float lightAtmosRadius = planetShineLight.radius.y;
    float3 lightColor = planetShineLight.color;

    float3 lightDirection = positionWS - lightPos;
    float lightDist = length(lightDirection);
    lightDirection /= lightDist; // normalize while saving length
    float heightAboveSurface = (lightDist - lightRadius) / lightRadius;

    // account for rotation of source to main light
    float intensity =  dot(_MainLightPosition.xyz, lightDirection) * 0.5 + 0.5;
    // account for size of source, larger planets generate more light
    intensity *= 1000 * lightRadius;// * lightRadius;
    // account for distance of source, inv sqr falloff
    intensity *= rcp(lightDist * lightDist);

    // Falloff beneath surface
    float surfaceFactor = smoothstep(lightRadius * 0.75, lightRadius, lightDist);
    
    // normal
    float NdotL = -dot(normal, lightDirection);


    // account for shadowing from local body
    float UdotL = dot(localUp, lightDirection);
    // fade out shadow based on height
    UdotL = lerp(UdotL, 1, saturate(heightAboveClosestBody));
    // sharpen shadows around edge
    float shadow = smoothstep(-_SunsetZoneWidth, 0, UdotL);

    
    // additional component for light when within an atmosphere
    float inAtmosComponent = 0;
    if (lightAtmosRadius > 0)
    {
        float NdotML = dot(normal, _MainLightPosition.xyz) * 0.5 + 0.5;
        float occlusion = -NdotL * 0.5 + 0.5;
        float UdotML = dot(lightDirection, _MainLightPosition.xyz);
        float MLshadow = smoothstep(-_SunsetZoneWidth, _SunsetZoneWidth, UdotML);
        float atmosHeight = 1 - saturate((lightDist - lightRadius) / (lightAtmosRadius - lightRadius));
        atmosHeight = atmosHeight * atmosHeight;
        //inAtmosComponent = NdotML * surfaceFactor * atmosHeight * MLshadow * _AtmosGIPower;
        inAtmosComponent = smoothstep(0, _SunsetZoneWidth, UdotML) * surfaceFactor * atmosHeight * _AtmosGIPower;
    }

    float heightBelowSurface = 1 - saturate(-heightAboveSurface);
    float planetShineComponent = smoothstep(0.1, 0.12, NdotL) * saturate(NdotL) * intensity * surfaceFactor * shadow;
    float finalIntensity =  max(inAtmosComponent, planetShineComponent);
    return heightBelowSurface * finalIntensity * lightColor;

    // this changes colour in sunset zone, TODO
    //float sunsetZone = 1 - smoothstep(0, _SunsetZoneWidth, abs(NdotL));
    //return lerp(lightColor, sunsetColor, sunsetZone) * intensity;
}

half3 SampleAtmosphericIllumination(float3 positionWS, float3 normalWS)
{
    // Find closest planet
    int closestIndex;
    float closestDist = 3.402823466e+38;
    for (int n = 0; n < _NumPlanetShineLights; n++)
    {
        float3 v = positionWS - _PlanetShineLightBuffer[n].positionWS;
        float dst = dot(v,v);
        if (dst < closestDist)
        {
            closestIndex = n;
            closestDist = dst;
        }
    }
    PlanetShineLight closestPlanet = _PlanetShineLightBuffer[closestIndex];

    float3 localUp = closestPlanet.positionWS - positionWS;
    float height = length(localUp);
    localUp /= height; // normalize while saving length
    float r = closestPlanet.radius.x;
    float heightAboveSurface = (height - r) / r;
    float heightShadowFactor = (heightAboveSurface / (4));

    // Main Light
    half3 lightSum;// = CalculateLightComponent(position01, mainLightDirection, mainLightColor, sunsetColor);
    
    // Other Lights
    for (int j = 0; j < _NumPlanetShineLights; j++)
    {
        lightSum += CalculateLightComponent(positionWS, _PlanetShineLightBuffer[j], localUp, normalWS, heightAboveSurface);
    }

    // Depth & Falloff - old. a bit more sophisticated than current implementation. left for reference.
    //float dist01 = length(position01) * textureInflation;
    //float atmosphereDepth01 = 1 - surfaceRadius01;
    //float undergroundFalloff = smoothstep(-0.8, 0.00, dist01 - surfaceRadius01);
    //float atmosphereFalloff = 1 - saturate((dist01 - surfaceRadius01) / atmosphereDepth01);
    //float falloffPower = undergroundFalloff * atmosphereFalloff;

    // AMBIENT
    half3 ambientX = abs(dot(normalWS, float3(1, 0, 0))) * _AGIAmbientColorX;
    half3 ambientY = abs(dot(normalWS, float3(0, 1, 0))) * _AGIAmbientColorY;
    half3 ambientZ = abs(dot(normalWS, float3(0, 0, 1))) * _AGIAmbientColorZ;

    half3 finalColor = _GlobalGIPower * lightSum + _AGIAmbientColor + ambientX + ambientY + ambientZ;

    return finalColor;
}

#endif
