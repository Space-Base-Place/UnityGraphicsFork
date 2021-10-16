#ifndef ATMOSPHERIC_ILLUMINATION_INCLUDED
#define ATMOSPHERIC_ILLUMINATION_INCLUDED

Texture3D _GITexture;
SamplerState GILinearClampSampler;
float4 _GIParams;

half3 SampleAtmosphericIllumination(float3 positionWS)
{
    float3 centerWS = _GIParams.xyz;
    float oneOverRadius = _GIParams.w;
    float3 uvw = (positionWS - centerWS) * oneOverRadius * 0.5 + 0.5;
    return _GITexture.Sample(GILinearClampSampler, uvw);
}

#endif
