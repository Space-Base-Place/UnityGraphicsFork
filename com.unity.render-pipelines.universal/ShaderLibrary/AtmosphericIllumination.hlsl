#ifndef ATMOSPHERIC_ILLUMINATION_INCLUDED
#define ATMOSPHERIC_ILLUMINATION_INCLUDED

struct PlanetShineLight
{
    float3 positionWS;
    float3 color;
    float3 sunsetColor;
    float3 radius; //x: surface, y: atmosphere
};

StructuredBuffer<PlanetShineLight> _PlanetShineLightBuffer;
int _NumPlanetShineLights;


half3 _AGIAmbientColor;

float _SunsetZoneWidth;
float _GlobalGIPower;
float _AtmosGIPower;

half3 CalculateLightComponent(float3 positionWS, PlanetShineLight planetShineLight, float3 normal)
{
    float3 lightPos = planetShineLight.positionWS.xyz;
    float lightRadius = planetShineLight.radius.x;
    float lightAtmosRadius = planetShineLight.radius.y;
    float3 lightColor = planetShineLight.color;
    float3 sunsetColor = planetShineLight.sunsetColor;

    float3 lightDirection = positionWS - lightPos;
    float lightDist = length(lightDirection);
    lightDirection /= lightDist; // normalize while saving length

    // account for rotation of source to main light
    float intensity =  dot(_MainLightPosition.xyz, lightDirection) * 0.5 + 0.5;
    // account for size of source, larger planets generate more light
    intensity *= 1000 * lightRadius;// * lightRadius;
    // account for distance of source, inv sqr falloff
    intensity *= rcp(lightDist * lightDist);

    // Falloff beneath surface
    //float heightAboveSurface = (lightDist - lightRadius) / lightRadius;
    //float heightBelowSurface = 1 - saturate(-heightAboveSurface);
    float surfaceFactor = max(smoothstep(lightRadius * 0.75, lightRadius, lightDist), 0.1);
    
    // normal
    float NdotL = -dot(normal, lightDirection);

    float3 localUp = planetShineLight.positionWS - positionWS;
    float height = length(localUp);
    localUp /= height; // normalize while saving length
    float r = planetShineLight.radius.x;
    float heightAboveSurface = (height - r) / r;
    // account for shadowing from local body
    float UdotL = dot(localUp, lightDirection);
    // fade out shadow based on height
    UdotL = lerp(UdotL, 1, saturate(heightAboveSurface));
    // sharpen shadows around edge
    float shadow = smoothstep(-_SunsetZoneWidth, 0, UdotL);

    
    // additional component for light when within an atmosphere
    float inAtmosComponent = 0;
    float3 inAtmosColor = 0;
    if (lightAtmosRadius > 0)
    {
        float UdotML = dot(localUp, _MainLightPosition.xyz);
        float MLshadow = max(smoothstep(-_SunsetZoneWidth, _SunsetZoneWidth, -UdotML), 0.1);
        float atmosHeight = 1 - saturate((lightDist - lightRadius) / (lightAtmosRadius - lightRadius));
        atmosHeight = atmosHeight * atmosHeight;

        // ignoring normals, simulates general bounce lighting into shadows
        inAtmosComponent = MLshadow * atmosHeight * _AtmosGIPower;

        // we add 2 additional components to give color differentiation on the normals
        float3 BT = cross(localUp, _MainLightPosition.xyz);
        float3 T = cross(BT, localUp);
        float TdotN = dot(T, normal);

        // main component
        float b = saturate(-UdotML + _SunsetZoneWidth);
        inAtmosColor += saturate(b + TdotN * saturate(UdotML - _SunsetZoneWidth)) * b * lightColor;

        // sunset component
        float a = 1 - abs(max(UdotML, -0.9));
        inAtmosColor += saturate(a + TdotN * UdotML) * (1 - saturate(UdotML)) * sunsetColor;

        inAtmosColor *= atmosHeight * _AtmosGIPower;
    }

    float planetShineComponent = smoothstep(0.1, 0.12, NdotL) * saturate(NdotL) * intensity * shadow;
    float3 finalColor =  max(inAtmosComponent, planetShineComponent) * lightColor + inAtmosColor;
    //float3 finalColor = inAtmosColor;

    return surfaceFactor * finalColor;
}

half3 SampleAtmosphericIllumination(float3 positionWS, float3 normalWS)
{

    // Main Light
    half3 lightSum;// = CalculateLightComponent(position01, mainLightDirection, mainLightColor, sunsetColor);
    
    // Other Lights
    for (int j = 0; j < _NumPlanetShineLights; j++)
    {
        lightSum += CalculateLightComponent(positionWS, _PlanetShineLightBuffer[j], normalWS);
    }

    // Depth & Falloff - old. a bit more sophisticated than current implementation. left for reference.
    //float dist01 = length(position01) * textureInflation;
    //float atmosphereDepth01 = 1 - surfaceRadius01;
    //float undergroundFalloff = smoothstep(-0.8, 0.00, dist01 - surfaceRadius01);
    //float atmosphereFalloff = 1 - saturate((dist01 - surfaceRadius01) / atmosphereDepth01);
    //float falloffPower = undergroundFalloff * atmosphereFalloff;

    half3 finalColor = _GlobalGIPower * lightSum + _AGIAmbientColor;

    return finalColor;
}

#endif
