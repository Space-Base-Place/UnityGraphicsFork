Shader "Hidden/LightBubbles"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        //_BubbleBufferTex ("BufferTexture", 2D) = "white" {}
        _Color ("Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _LightInstanceIntensity ("_LightInstanceIntensity", float) = 1
        _EdgeSharpness ("_EdgeSharpness", float) = 0.3
    }

    HLSLINCLUDE

    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    
    half _LightInstanceIntensity;
    half _EdgeSharpness;

    float4 _CurrentLightColor;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS  : NORMAL;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
	    float3 normalWS  : NORMAL;
        float3 screenUV : TEXCOORD1;
        float3 viewDirWS : TEXCOORD2;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vertex(Attributes input) //copied from StencilDeferred.shader
    {
        Varyings output = (Varyings)0;

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, output);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
        output.positionCS = vertexInput.positionCS;
        output.normalWS = TransformObjectToWorldNormal(normalize(input.normalOS)); //sphere 

        output.screenUV = output.positionCS.xyw;
        #if UNITY_UV_STARTS_AT_TOP
        output.screenUV.xy = output.screenUV.xy * float2(0.5, -0.5) + 0.5 * output.screenUV.z;
        #else
        output.screenUV.xy = output.screenUV.xy * 0.5 + 0.5 * output.screenUV.z;
        #endif

        //float3 viewDirWS = mul(unity_CameraInvProjection, float4(output.screenUV.xy, 0, -1));
		output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);

        return output;
    }

    half4 FragWhite(Varyings input) : SV_Target
    {
        return half4(1.0, 1.0, 1.0, 1.0);
    }

    half4 FragNormalBlur(Varyings input) : SV_Target
    {
        // we get blur for free by using sphere normals
        half blur = dot(input.viewDirWS, normalize(input.normalWS));
        blur *= blur;
        blur = smoothstep(0, _EdgeSharpness, blur) * saturate(_LightInstanceIntensity);
        return half4(blur * _CurrentLightColor.xyz, 0.0);
    }





    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Overlay" "RenderPipeline" = "UniversalPipeline" }

        // 0 - Stencil pass
        Pass
        {
            Name "Stencil Volume"

            ZTest GEqual
            ZWrite Off
            ZClip false
            Cull Back
            ColorMask 0

            Stencil {
                Ref 1
                Comp Always
                Pass Replace
                ZFail Invert
            }

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex Vertex
            #pragma fragment FragWhite

            ENDHLSL
        }

        // 1 - Light Bubble pass
        Pass
        {
            Name "Light Bubble"

            ZTest Always
            ZWrite Off
            ZClip false
            Cull Front
            Blend OneMinusDstColor One  

            Stencil {
                Ref 1
                Comp NotEqual
                Pass Zero
                Fail Zero
                ZFail Zero
            }

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            #pragma vertex Vertex
            #pragma fragment FragNormalBlur

            ENDHLSL
        }

        // 2 - blend pass
        Pass
        {
            Name "Blend Pass"
            Cull Off ZWrite Off ZTest Always

            HLSLPROGRAM
            #pragma exclude_renderers gles gles3 glcore
            #pragma target 4.5

            //  todo: compare scene depth with depth of front of sphere to blend smoothly into the ground
            // requires ZWrite On for stencil pass into a copy of the depth texture 
            // also for rear face when inside sphere??? too hard, looks fine
            //#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #pragma vertex ScreenVertex
            #pragma fragment FragBlend

            sampler2D _MainTex;
            sampler2D _BubbleBufferTex;
            half4 _Color;

            struct ScreenAttributes 
            {
		        float4 vertex : POSITION;
		        float4 uv : TEXCOORD0;
	        };

	        struct ScreenVaryings 
            {
		        float4 pos : SV_POSITION;
		        float2 uv : TEXCOORD0;
	        };

            ScreenVaryings ScreenVertex(ScreenAttributes input) 
            {
                ScreenVaryings output;
		        output.pos = TransformObjectToHClip(input.vertex);
		        output.uv = input.uv;
                return output;
            }

            half4 FragBlend(ScreenVaryings input) : SV_Target
            {
                half4 originalCol = tex2D(_MainTex, input.uv);
                half3 bufferCol = tex2D(_BubbleBufferTex, input.uv).rgb;
                half3 newCol = saturate(bufferCol) * _Color.rgb * _Color.a;
                return half4(originalCol.rgb + newCol,1);
            }

            ENDHLSL
        }
    }
}
