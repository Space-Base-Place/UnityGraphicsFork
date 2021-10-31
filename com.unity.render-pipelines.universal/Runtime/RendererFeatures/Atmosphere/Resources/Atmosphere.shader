Shader "Hidden/Atmosphere"
{
	Properties
	{
        [HideInInspector] _MainTex ("Texture", 2D) = "white" {}

		planetCentre ("Planet Centre", Vector) = (0.0, 0.0, 0.0, 0.0)
		atmosphereRadius ("Atmosphere Radius", float) = 300
		oceanRadius ("Ocean Radius", float) = 150
	    planetRadius ("Planet Radius", float) = 180

			// Paramaters
		intensity ("Intensity", float) = 0.2
		scatteringWavelengths ("Scattering Wavelengths", Vector) = (800, 530, 460, 0)
		scatteringStrength ("Scattering Strength", float) = 50
		densityFalloff ("Density Falloff", float) = 5
		inscatteringScale ("Inscattering Scale", float) = 1

		//hidden Properties
		//read/write from multiple places using custom editor + scriptablerenderfeature
		[HideInInspector] numInScatteringPoints ("numInScatteringPoints", int) = 10
		[HideInInspector] numOpticalDepthPoints ("numOpticalDepthPoints", int) = 10
		[HideInInspector] textureSize ("textureSize", int) = 128

		[HideInInspector] _RayOffset ("_RayOffset", float) = 1
		[HideInInspector] _ShadowStrength ("_ShadowStrength", float) = 1
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


			#pragma vertex vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.universal/Runtime/RendererFeatures/Atmosphere/Resources/Atmosphere.hlsl"

			ENDHLSL
		}
	}

}
