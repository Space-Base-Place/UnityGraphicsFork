#ifndef ATMOSPHERIC_ILLUMINATION_INCLUDED
#define ATMOSPHERIC_ILLUMINATION_INCLUDED

Texture3D _GITexture;
SamplerState GILinearClampSampler;
float4x4 AGI_TO_WORLD_MATRIX;
float4x4 WORLD_TO_AGI_MATRIX;

half3 SampleAtmosphericIllumination(float3 positionWS)
{
    float3 uvw = mul(WORLD_TO_AGI_MATRIX, float4(positionWS, 1)).xyz;
    return _GITexture.Sample(GILinearClampSampler, uvw).xyz;
}

#endif
