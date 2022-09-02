Shader "Hidden/Outlines"
{

    HLSLINCLUDE

    #pragma target 4.5
    #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
    #pragma multi_compile_fragment _ _USE_GBUFFER_OBJECTID

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    TEXTURE2D_X(_MainTex);
    TEXTURE2D_X(_CanvasTex);
        
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
    float _DepthThresholdWidth;

    float _NormalThreshold;
    float _NormalThresholdWidth;

    float _DepthNormalThreshold;
    float _DepthNormalThresholdScale;

    #if _USE_GBUFFER_OBJECTID
    #define CURRENT_OBJECT_ID GBUFFER_OBJECTID_TEX
#else
    #define CURRENT_OBJECT_ID _CurrentObjectIDTexture
#endif

    TEXTURE2D_X(CURRENT_OBJECT_ID);

    float _FlatColor;

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

    float LinearEyeDepth(float z)
    {
        return rcp(_ZBufferParams.z * z + _ZBufferParams.w);
    }

    float SampleDepth(float2 uv)
    {
        //return SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, uv);
        return LinearEyeDepth(SampleSceneDepth(uv));
    }

    float LoadDepth(int2 coords)
    {
        return LoadSceneDepth(coords);
        //return LinearEyeDepth(LoadSceneDepth(coords));
    }

    half4 LoadObjectID(int2 coords)
    {
        return LOAD_TEXTURE2D(CURRENT_OBJECT_ID, coords);
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

    Varyings VertFullscreen(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);

        output.positionCS = float4(input.positionHCS.xyz, 1.0);
        output.uv = input.uv.xy;

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

        float3 viewVector = mul(unity_CameraInvProjection, float4(input.uv.xy * 2 - 1, 0, -1)).xyz;
        output.viewSpaceDir = mul(unity_CameraToWorld, float4(viewVector,0)).xyz;

        return output;
    }

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);

        float3 positionWS = TransformObjectToWorld( input.positionHCS.xyz );
        output.positionCS = TransformWorldToHClip( positionWS );
        output.uv = input.uv.xy;
        output.viewSpaceDir = 0;
        return output;
    }


    half4 SobelFilter(Varyings input, out float outDepth)
    {
        // Sobel filter kernel
        const int2 texAddrOffsets[8] = {
            int2(-1, -1), 
            int2( 0, -1),
            int2( 1, -1),
            int2(-1,  0),
            int2( 1,  0),
            int2(-1,  1),
            int2( 0,  1),
            int2( 1,  1),
        };

        float depths[8];
        half3 normals[8];
        half objIds[8];

        float2 uv = input.uv;

        int scale = floor(_Scale);
        int2 coords = int2(uv * _MainTex_TexelSize.zw);

        // Setup depth info
        float depth = LoadDepth(coords);
        float minDepth = depth, maxDepth = depth;

        // Setup normal info
        float3 normal = UnpackNormal(LOAD_TEXTURE2D(_GBuffer2, coords).rgb);
        float NdotV = 1 - dot(normal, -input.viewSpaceDir);
        // Return a value in the 0...1 range depending on where NdotV lies 
        // between _DepthNormalThreshold and 1.
        float normalThreshold01 = saturate((NdotV - _DepthNormalThreshold) / (1 - _DepthNormalThreshold));
        // Scale the threshold, and add 1 so that it is in the range of 1..._NormalThresholdScale + 1.
        float normalThreshold = normalThreshold01 * _DepthNormalThresholdScale + 1;

        // Get objectId;
        half objId = LoadObjectID(coords).r;

        // Collect data
        for (int i = 0; i < 8; i++)
        {
            int2 offsetCoord = clamp(coords + texAddrOffsets[i] * scale, 0, _MainTex_TexelSize.zw);
            depths[i] = LoadDepth(offsetCoord);
            minDepth = min(depths[i], minDepth);
            maxDepth = max(depths[i], maxDepth);
            normals[i] = UnpackNormal(LOAD_TEXTURE2D(_GBuffer2, offsetCoord).rgb);
            objIds[i] = LoadObjectID(offsetCoord).r;
        }

        // Sobel operator on depth
        float x = depths[0] + 2 * depths[3] + depths[5] - depths[2] - 2 * depths[4] - depths[7];
        float y = depths[0] + 2 * depths[1] + depths[2] - depths[5] - 2 * depths[6] - depths[7];
        float edgeDepth = sqrt(x*x + y*y);

        // Sobel operator on normal
        half3 n = normals[0] + 2 * normals[3] + normals[5] - normals[2] - 2 * normals[4] - normals[7];
        half3 m = normals[0] + 2 * normals[1] + normals[2] - normals[5] - 2 * normals[6] - normals[7];
        float edgeNormal = sqrt(dot(n, n) + dot(m, m));

        // Sobel operator on objectId
        float u = objIds[0] + 2 * objIds[3] + objIds[5] - objIds[2] - 2 * objIds[4] - objIds[7];
        float v = objIds[0] + 2 * objIds[1] + objIds[2] - objIds[5] - 2 * objIds[6] - objIds[7];
        float edgeId = sqrt(u*u + v*v);
        

        float dThresh = _DepthThreshold * depth * normalThreshold;
        float depthT = smoothstep(dThresh, dThresh + _DepthThresholdWidth, edgeDepth);
        float normalT = smoothstep(_NormalThreshold, _NormalThreshold + _NormalThresholdWidth, edgeNormal);
        float edgeT = step(0.001, edgeId);
        //return half4(edgeT,0,0,1);

        float isEdge = max(edgeT, max(depthT, normalT));
                //return half4(isEdge,0,0,1);

        //return half4((abs(maxDepth - minDepth) < _DepthThreshold ? 1 : 0).xxx, 1);
        bool isOnSurface = abs(depth - minDepth) > abs(depth - maxDepth); // || abs(maxDepth - minDepth) < _DepthThreshold;
        float finalEdge = isOnSurface ? isEdge : 0;
        //return half4(finalEdge,0,0,1);

        //final color
        //half4 edgeColor = half4(_Color.rgb, min(_Color.a, isEdge * maxDepth * 100));
        half4 edgeColor = half4(_Color.rgb, _Color.a * isEdge);
        half4 sceneColor = LOAD_TEXTURE2D(_MainTex, coords);

        outDepth = isEdge > 0 ? maxDepth : depth;

        //return half4(depthT,0,0,1);

        return alphaBlend(edgeColor, sceneColor);
    }

    half4 RobertsCrossFilter(Varyings input, out float outDepth)
    {


        float depths[4];
        half3 normals[4];
        half objIds[4];

		int f = -floor(_Scale * 0.5);
		int c = ceil(_Scale * 0.5);

        // RobertsCross filter kernel
        int2 texAddrOffsets[4] = {
            int2(f, f), 
            int2(c, f),
            int2(c, c),
            int2(f, c),
        };

        int2 coords = int2(input.uv * _MainTex_TexelSize.zw);

        // Setup depth info
        float depth = LoadDepth(coords);
        float minDepth = depth, maxDepth = depth;

        // Setup normal info
        float3 normal = UnpackNormal(LOAD_TEXTURE2D(_GBuffer2, coords).rgb);
        float NdotV = 1 - dot(normal, -input.viewSpaceDir);
        // Return a value in the 0...1 range depending on where NdotV lies 
        // between _DepthNormalThreshold and 1.
        float normalThreshold01 = saturate((NdotV - _DepthNormalThreshold) / (1 - _DepthNormalThreshold));
        // Scale the threshold, and add 1 so that it is in the range of 1..._NormalThresholdScale + 1.
        float normalThreshold = normalThreshold01 * _DepthNormalThresholdScale + 1;

        // Get objectId;
        half objId = LoadObjectID(coords).r;

        // Collect data
        for (int i = 0; i < 4; i++)
        {
            int2 offsetCoord = clamp(coords + texAddrOffsets[i], 0, _MainTex_TexelSize.zw);
            depths[i] = LoadDepth(offsetCoord);
            minDepth = min(depths[i], minDepth);
            maxDepth = max(depths[i], maxDepth);
            normals[i] = UnpackNormal(LOAD_TEXTURE2D(_GBuffer2, offsetCoord).rgb);
            objIds[i] = LoadObjectID(offsetCoord).r;
        }

        // Sobel operator on depth
        float x = depths[2] - depths[0];
        float y = depths[3] - depths[1];
        float edgeDepth = sqrt(x*x + y*y);

        // Sobel operator on normal
        half3 n = normals[2] - normals[0];
        half3 m = normals[3] - normals[1];
        float edgeNormal = sqrt(dot(n, n) + dot(m, m));

        // Sobel operator on objectId
        float u = objIds[2] - objIds[0];
        float v = objIds[3] - objIds[1];
        float edgeId = sqrt(u*u + v*v);
        

        float dThresh = _DepthThreshold * depth * normalThreshold;
        float depthT = smoothstep(dThresh, dThresh + _DepthThresholdWidth, edgeDepth);
        float normalT = smoothstep(_NormalThreshold, _NormalThreshold + _NormalThresholdWidth, edgeNormal);
        float edgeT = step(0.001, edgeId);

        float isEdge = max(edgeT, max(depthT, normalT));

        bool isOnSurface = abs(depth - minDepth) > abs(depth - maxDepth);
        float finalEdge = isOnSurface ? isEdge : 0;

        //final color
        half4 edgeColor = half4(_Color.rgb, _Color.a * isEdge);
        half4 sceneColor = LOAD_TEXTURE2D(_MainTex, coords);

        outDepth = isEdge > 0 ? maxDepth : depth;

        //return half4(edgeDepth,0,0,1);

        return alphaBlend(edgeColor, sceneColor);
    }

    half4 RobertsCrossFilterOld(Varyings input)
    {
        float2 uv = input.uv;
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
        float maxDepth = Max4(depth0, depth1, depth2, depth3);
        float minDepth = Min4(depth0, depth1, depth2, depth3);

        // check if this fragment 
        bool fragIsOnSurface = abs(depth - minDepth) < abs(depth - maxDepth);

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
        float depthThreshold = _DepthThreshold * depth * normalThreshold;

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

        
    half4 SobelFilterSimple(Varyings input)
    {
        // Sobel filter kernel
        const int2 texAddrOffsets[8] = {
            int2(-1, -1), 
            int2( 0, -1),
            int2( 1, -1),
            int2(-1,  0),
            int2( 1,  0),
            int2(-1,  1),
            int2( 0,  1),
            int2( 1,  1),
        };

        float depths[8];
        half3 normals[8];
        half objIds[8];

        float2 uv = input.uv;

        int scale = floor(_Scale);
        int2 coords = int2(uv * _MainTex_TexelSize.zw);

        // Get objectId;
        half objId = LOAD_TEXTURE2D(_CanvasTex, coords).r;
        //return half4(objId,0,0,1);

        // Collect data
        for (int i = 0; i < 8; i++)
        {
            int2 offsetCoord = clamp(coords + texAddrOffsets[i] * scale, 0, _MainTex_TexelSize.zw);
            objIds[i] = LOAD_TEXTURE2D(_CanvasTex, offsetCoord).r;
        }

        // Sobel operator on objectId
        float u = objIds[0] + 2 * objIds[3] + objIds[5] - objIds[2] - 2 * objIds[4] - objIds[7];
        float v = objIds[0] + 2 * objIds[1] + objIds[2] - objIds[5] - 2 * objIds[6] - objIds[7];
        float edgeId = sqrt(u*u + v*v);

        //final color
        half4 edgeColor = half4(_Color.rgb, saturate(_Color.a * edgeId));
        half4 sceneColor = LOAD_TEXTURE2D(_MainTex, coords);
        //return half4(edgeId,0,0,1);

        return alphaBlend(edgeColor, sceneColor);
    }

    
    struct PixelData
    {
        half4  color : SV_Target;
        float  depth : SV_Depth;
    };

    PixelData Frag(Varyings input)
    {
        PixelData pd;
        float depth;
        //half4 color = SobelFilter(input, depth);
        half4 color = RobertsCrossFilter(input, depth);
        pd.color = color;
        pd.depth = depth;
        return pd;
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

    
    float Flat(Varyings input) : SV_Target0
    {
        return _FlatColor;
    }

        
    half4 SimpleFrag(Varyings input) : SV_Target0
    {
        return SobelFilterSimple(input);
    }


    ENDHLSL

    SubShader
    {
        
        Pass // 0 - Outlines
        {
            Name "Outlines"
            Cull Off ZWrite On ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex VertFullscreen
            #pragma fragment Frag

            ENDHLSL
        }

        Pass // 1 - copy with depth
        {
            Name "CopyWithDepth"
            Cull Off ZWrite Off ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex VertFullscreen
            #pragma fragment CopyFrag

            ENDHLSL
        }

        Pass // 2 - copy depth only
        {
            Name "CopyDepth"
            Cull Off ZWrite On ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex VertFullscreen
            #pragma fragment CopyDepth

            ENDHLSL
        }

        
        Pass // 3 - flat draw
        {
            Name "FlatDraw"
            Cull Off ZWrite On ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex Vert
            #pragma fragment Flat

            ENDHLSL
        }
                
        Pass // 4 - simple sobel
        {
            Name "SimpleSobel"
            Cull Off ZWrite On ZTest Always 

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex VertFullscreen
            #pragma fragment SimpleFrag

            ENDHLSL
        }
    }
}
