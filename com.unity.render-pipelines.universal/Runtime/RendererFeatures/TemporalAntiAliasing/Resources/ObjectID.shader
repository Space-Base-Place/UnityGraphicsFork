Shader "Hidden/ObjectID"
{

    HLSLINCLUDE

        #pragma target 4.5
        #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


        struct Attributes
        {
            float4 positionOS   : POSITION;
            float4 objectID : TEXCOORD7;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float objectID   : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings ObjectIDVertex(Attributes input)
        {
            Varyings output;

            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

	    //#if UNITY_UV_STARTS_AT_TOP
		//    output.positionCS.y *= -1;
	    //#endif

            output.objectID = input.objectID.w;

            return output;
        }

        float ObjectIDFragment(Varyings input) : SV_TARGET0
        {
//return 1;
           return input.objectID;
        }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Name "ObjectID"
            //Tags{"LightMode" = "ObjectID"}

            ZWrite Off ZTest LEqual Blend Off Cull Off

            HLSLPROGRAM
                #pragma vertex ObjectIDVertex
                #pragma fragment ObjectIDFragment
            ENDHLSL
        }


    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
