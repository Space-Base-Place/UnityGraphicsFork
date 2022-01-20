Shader "Hidden/Atmosphere"
{

	Properties
	{
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}
	}

	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			HLSLPROGRAM

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #pragma multi_compile _ FIXED_RAY_LENGTH

			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/Runtime/RendererFeatures/Atmosphere/Resources/Atmosphere.hlsl"

			ENDHLSL
		}
	}

}
