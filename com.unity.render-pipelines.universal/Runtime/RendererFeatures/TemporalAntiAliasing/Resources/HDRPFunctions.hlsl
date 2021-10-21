// Functions imported from across the HDRP to support TAA

// com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesGlobal.cs.hlsl
float4 _RTHandleScale;
float4 _RTHandleScaleHistory;
float4 _RTHandlePostProcessScale;
float4 _RTHandlePostProcessScaleHistory;



// com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl

float2 ClampAndScaleUVForPoint(float2 UV)
{
    return min(UV, 1.0f) * _RTHandleScale.xy;
}



// com.unity.render-pipelines.high-definition/Runtime/Material/Builtin/BuiltinData.hlsl

// EncodeMotionVector / DecodeMotionVector code for now, i.e it must do nothing like it is doing currently.
// Design note: We assume that motion vector/distortion fit into a single buffer (i.e not spread on several buffer)
void EncodeMotionVector(float2 motionVector, out float4 outBuffer)
{
    // RT - 16:16 float
    outBuffer = float4(motionVector.xy, 0.0, 0.0);
}

bool PixelSetAsNoMotionVectors(float4 inBuffer)
{
    return inBuffer.x > 1.0f;
}

void DecodeMotionVector(float4 inBuffer, out float2 motionVector)
{
    motionVector = PixelSetAsNoMotionVectors(inBuffer) ? 0.0f : inBuffer.xy;
}