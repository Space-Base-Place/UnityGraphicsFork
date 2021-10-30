Shader "Hidden/Outlines"
{

	HLSLINCLUDE

	#pragma target 4.5
	#pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

	TEXTURE2D_X(_MainTex);
        
	// Data pertaining to _MainTex's dimensions.
	// https://docs.unity3d.com/Manual/SL-PropertiesInPrograms.html
	float4 _MainTex_TexelSize;

	TEXTURE2D_X_HALF(_GBuffer0); //ALBEDO
	//TEXTURE2D_X_HALF(_GBuffer1); //SPECULAR, not needed	
	TEXTURE2D_X_HALF(_GBuffer2); //NORMAL
	SamplerState LinearClamp;
	SamplerState PointClamp;

    //TEXTURE2D_FLOAT(_DepthTex);
    //SAMPLER(sampler_DepthTex);


	float _Scale;
	float4 _Color;

	RW_TEXTURE2D(float4, _MotionVectors) : register(u1);

	float _DepthThreshold;
	float _DepthNormalThreshold;
	float _DepthNormalThresholdScale;

	float _NormalThreshold;



	#if UNITY_REVERSED_Z
	#define COMPARE_DEPTH(a, b) step(b, a)
	#else
	#define COMPARE_DEPTH(a, b) step(a, b)
	#endif

	// This matrix is populated in PostProcessOutline.cs.
	//float4x4 _ClipToView;

	// Combines the top and bottom colors using normal blending.
	// https://en.wikipedia.org/wiki/Blend_modes#Normal_blend_mode
	// This performs the same operation as Blend SrcAlpha OneMinusSrcAlpha.
	float4 alphaBlend(float4 top, float4 bottom)
	{
		float3 color = (top.rgb * top.a) + (bottom.rgb * (1 - top.a));
		float alpha = top.a + bottom.a * (1 - top.a);

		return float4(color, alpha);
	}


	float SampleSceneDepthLinearClamp(float2 uv)
	{
		return SAMPLE_TEXTURE2D_X(_CameraDepthTexture, LinearClamp, UnityStereoTransformScreenSpaceTex(uv)).r;
	}

	float Max4(float d1, float d2, float d3, float d4)
	{
		float max1 = max(d1, d2);
		float max2 = max(d3, d4);
		return max(max1, max2);
	}

	float Min4(float d1, float d2, float d3, float d4)
	{
		float min1 = min(d1, d2);
		float min2 = min(d3, d4);
		return min(min1, min2);
	}

    float SampleDepth(float2 uv)
    {
        return SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
    }


	struct Attributes
	{
		uint vertexID     : SV_VertexID;
		float4 positionHCS : POSITION;
		float4 uv : TEXCOORD0;
		UNITY_VERTEX_INPUT_INSTANCE_ID
	};

	struct Varyings
	{
		float4 positionCS : SV_POSITION;
		float2 uv : TEXCOORD0;
		float3 viewSpaceDir : TEXCOORD1;
	};

	Varyings Vert(Attributes input)
	{
		Varyings output;
		UNITY_SETUP_INSTANCE_ID(input);

		output.positionCS = float4(input.positionHCS.xyz, 1.0);
		output.uv = input.uv;

    //output.positionCS = GetQuadVertexPosition(input.vertexID);
    //output.positionCS.xy = output.positionCS.xy * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f); //convert to -1..1
    //output.uv = GetQuadTexCoord(input.vertexID) * _ScaleBias.xy + _ScaleBias.zw;

		//float3 viewVector = mul(unity_CameraInvProjection, output.vertex).xyz;
		//output.viewSpaceDir = mul(unity_CameraToWorld, float4(viewVector,0));
		//output.viewSpaceDir = mul(unity_CameraInvProjection, output.vertex).xyz;

	#if UNITY_UV_STARTS_AT_TOP
	//	output.uv = output.uv * float2(1.0, -1.0) + float2(0.0, 1.0);
		output.positionCS.y *= -1;
	#endif

		float3 viewVector = mul(unity_CameraInvProjection, float4(input.uv.xy * 2 - 1, 0, -1));
		output.viewSpaceDir = mul(unity_CameraToWorld, float4(viewVector,0));

		return output;
	}


	half4 Frag(Varyings input) : SV_Target0
	{
		float2 uv = input.uv;

		//float halfScaleFloor = floor(_Scale * 0.5);
		//float halfScaleCeil = ceil(_Scale * 0.5);
		float scale = floor(_Scale);

		// Sample the pixels in an X shape, roughly centered around i.texcoord.
		// As the _CameraDepthTexture and _CameraNormalsTexture default samplers
		// use point filtering, we use the above variables to ensure we offset
		// exactly one pixel at a time.
		float2 bl = uv - float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * scale;
		float2 tr = uv + float2(_MainTex_TexelSize.x, _MainTex_TexelSize.y) * scale;  
		float2 br = uv + float2(_MainTex_TexelSize.x * scale, -_MainTex_TexelSize.y * scale);
		float2 tl = uv + float2(-_MainTex_TexelSize.x * scale, _MainTex_TexelSize.y * scale);

		half3 normal0 = UnpackNormal(SAMPLE_TEXTURE2D(_GBuffer2, PointClamp, bl).rgb);
		half3 normal1 = UnpackNormal(SAMPLE_TEXTURE2D(_GBuffer2, PointClamp, tr).rgb);
		half3 normal2 = UnpackNormal(SAMPLE_TEXTURE2D(_GBuffer2, PointClamp, br).rgb);
		half3 normal3 = UnpackNormal(SAMPLE_TEXTURE2D(_GBuffer2, PointClamp, tl).rgb);

		float depth = SampleDepth(uv);
		float depth0 = SampleDepth(bl);
		float depth1 = SampleDepth(tr);
		float depth2 = SampleDepth(br);
		float depth3 = SampleDepth(tl);

		// Find closest fragment
		float4 neighborhood = float4(depth0, depth1, depth2, depth3);

		float maxDepth = Max4(depth0, depth1, depth2, depth3);
		float minDepth = Min4(depth0, depth1, depth2, depth3);

		bool fragIsOnSurface = abs(depth - minDepth) > abs(depth - maxDepth);

		// Normals

		float NdotV = 1 - dot(normal0, -input.viewSpaceDir);

		// Return a value in the 0...1 range depending on where NdotV lies 
		// between _DepthNormalThreshold and 1.
		float normalThreshold01 = saturate((NdotV - _DepthNormalThreshold) / (1 - _DepthNormalThreshold));
		// Scale the threshold, and add 1 so that it is in the range of 1..._NormalThresholdScale + 1.
		float normalThreshold = normalThreshold01 * _DepthNormalThresholdScale + 1;

		// Modulate the threshold by the existing depth value;
		// pixels further from the screen will require smaller differences
		// to draw an edge.
		float depthThreshold = _DepthThreshold * depth0 * normalThreshold;

		float depthFiniteDifference0 = depth1 - depth0;
		float depthFiniteDifference1 = depth3 - depth2;
		// edgeDepth is calculated using the Roberts cross operator.
		// The same operation is applied to the normal below.
		// https://en.wikipedia.org/wiki/Roberts_cross
		float edgeDepth = sqrt(pow(depthFiniteDifference0, 2) + pow(depthFiniteDifference1, 2)) * 100;
		edgeDepth = edgeDepth > depthThreshold ? 1 : 0;

		half3 normalFiniteDifference0 = normal1 - normal0;
		half3 normalFiniteDifference1 = normal3 - normal2;
		// Dot the finite differences with themselves to transform the 
		// three-dimensional values to scalars.
		float edgeNormal = sqrt(dot(normalFiniteDifference0, normalFiniteDifference0) + dot(normalFiniteDifference1, normalFiniteDifference1));
		edgeNormal = edgeNormal > _NormalThreshold ? 1 : 0;

		float isEdge = fragIsOnSurface ? max(edgeDepth, edgeNormal) : 0;
		

		//final color
		half4 edgeColor = half4(_Color.rgb, _Color.a * isEdge);
		half4 sceneColor = SAMPLE_TEXTURE2D(_MainTex, LinearClamp, uv);


		return alphaBlend(edgeColor, sceneColor);
	}

	half4 CopyFrag(Varyings input, out float outDepth : SV_Depth) : SV_Target0
	{
		float2 uv = input.uv;
		outDepth = SampleDepth(uv);
		return SAMPLE_TEXTURE2D(_MainTex, LinearClamp, uv);
	}

	float CopyDepth(Varyings input) : SV_Depth
	{
		float2 uv = input.uv;
		return SampleDepth(uv);
	}

	ENDHLSL

    SubShader
    {
        
        Pass // 0 - Outlines
        {
			Cull Off ZWrite Off ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }

		Pass // 1 - copy with depth
        {
			Cull Off ZWrite Off ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment CopyFrag

            ENDHLSL
        }

		Pass // 2 - copy depth only
        {
			Cull Off ZWrite On ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment CopyDepth

            ENDHLSL
        }
		
    }
}