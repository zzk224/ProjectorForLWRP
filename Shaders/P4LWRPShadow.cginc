﻿//
// P4LWRPShadow.cginc
//
// Projector For LWRP
//
// Copyright (c) 2020 NYAHOON GAMES PTE. LTD.
//

#if !defined(P4LWRPSHADOW_CGINC_INCLUDED)
#define P4LWRPSHADOW_CGINC_INCLUDED

#include "../P4LWRP.cginc"
#include "Packages/com.unity.render-pipelines.lightweight/ShaderLibrary/Lighting.hlsl"

#if defined(LIGHTMAP_ON)
#define P4LWRP_LIGHTMAP_ON
#endif

#if (defined(P4LWRP_LIGHTMAP_ON) || defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW)) && !(defined(P4LWRP_ADDITIONAL_VERTEX_LIGHT_SHADOW) || defined(_ADDITIONAL_LIGHTS_VERTEX))
#define P4LWRP_PERPIXEL_SHADOWCOLOR
#endif

#if defined(P4LWRP_ADDITIONAL_VERTEX_LIGHT_SHADOW) && !defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW)
#define P4LWRP_ADDITIONAL_LIGHT_SHADOW
#endif

#if !defined(LIGHTMAP_ON)
#define P4LWRP_USE_LIGHTPROBES
#endif

#if defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW) && (!defined(P4LWRP_MAINLIGHT_BAKED) || !defined(P4LWRP_LIGHTMAP_ON))
#define P4LWRP_AMBIENT_INCLUDE_MAINLIGHT
#endif

#if defined(UNITY_HDR_ON)
#define P4LWRP_LIGHTCOLOR  half
#define P4LWRP_LIGHTCOLOR4 half4
#define P4LWRP_LIGHTCOLOR3 half3
#else
#define P4LWRP_LIGHTCOLOR  fixed
#define P4LWRP_LIGHTCOLOR4 fixed4
#define P4LWRP_LIGHTCOLOR3 fixed3
#endif

uniform fixed4 p4lwrp_ShadowMaskSelector;
uniform int p4lwrp_ShadowLightIndex;

struct P4LWRP_SHADOW_PROJECTOR_VERTEX
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
#if defined(MIXED_SHADOW_ON)
    float2 lightmapUV : TEXCOORD1;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

#if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
#define P4LWRP_LIGHTCOLOR_AND_FOG   P4LWRP_LIGHTCOLOR4
#define P4LWRP_SHADOWCOLOR_AND_FOG  fixed4
#define P4LWRP_TRANSFER_FOGCOORD(dst,opos)   dst = ComputeFogFactor((opos).z)
#else
#define P4LWRP_LIGHTCOLOR_AND_FOG   P4LWRP_LIGHTCOLOR3
#define P4LWRP_SHADOWCOLOR_AND_FOG  fixed3
#define P4LWRP_TRANSFER_FOGCOORD(dst,pos)
#endif

struct P4LWRP_SHADOW_PROJECTOR_V2F {
    float4 uvShadow  : TEXCOORD0;
#if defined(P4LWRP_LIGHTMAP_ON)
    half2 lightmapUV : TEXCOORD1;
#endif
#if defined(P4LWRP_PERPIXEL_SHADOWCOLOR)
#if defined(P4LWRP_LIGHTSOURCE_POINT)
    half3 lightColor : TEXCOORD2;
#elif defined(P4LWRP_LIGHTSOURCE_SPOT)
    half4 lightColor : TEXCOORD2; // w = spotAngleAttenuation.y
#else
    P4LWRP_LIGHTCOLOR3 lightColor : TEXCOORD2;
#endif
    P4LWRP_LIGHTCOLOR_AND_FOG ambientColor : TEXCOORD3;
    #if defined(P4LWRP_LIGHTSOURCE_POINT)
        half3 lightPos : TEXCOORD4;
    #elif defined(P4LWRP_LIGHTSOURCE_SPOT)
        half4 lightPos : TEXCOORD4; // w = spotAngleAttenuation.x
    #endif
#else
    P4LWRP_SHADOWCOLOR_AND_FOG shadowColor : TEXCOORD2;
#endif
	float4 pos : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

struct P4LWRP_ShadowLightData
{
    P4LWRP_LIGHTCOLOR3 color;
    half3 direction;
#if defined(P4LWRP_LIGHTSOURCE_POINT) || defined(P4LWRP_LIGHTSOURCE_SPOT)
    half3 relativePos;
#if defined(P4LWRP_LIGHTSOURCE_SPOT)
    half2 spotAngleAttenuation;
#endif
#endif
};

float P4LWRP_DistanceAttenuation_UsingRcp(float distanceSqr, float distanceRcp, half2 distanceAttenuation)
{
    float lightAtten = distanceRcp * distanceRcp;

#if SHADER_HINT_NICE_QUALITY
    // Use the smoothing factor also used in the Unity lightmapper.
    half factor = distanceSqr * distanceAttenuation.x;
    half smoothFactor = saturate(1.0h - factor * factor);
    smoothFactor = smoothFactor * smoothFactor;
#else
    // We need to smoothly fade attenuation to light range. We start fading linearly at 80% of light range
    // Therefore:
    // fadeDistance = (0.8 * 0.8 * lightRangeSq)
    // smoothFactor = (lightRangeSqr - distanceSqr) / (lightRangeSqr - fadeDistance)
    // We can rewrite that to fit a MAD by doing
    // distanceSqr * (1.0 / (fadeDistanceSqr - lightRangeSqr)) + (-lightRangeSqr / (fadeDistanceSqr - lightRangeSqr)
    // distanceSqr *        distanceAttenuation.y            +             distanceAttenuation.z
    half smoothFactor = saturate(distanceSqr * distanceAttenuation.x + distanceAttenuation.y);
#endif

    return lightAtten * smoothFactor;
}

P4LWRP_LIGHTCOLOR3 P4LWRP_CalculateMainLightLambert(half3 normalWS)
{
    float attenuation = unity_LightData.z;
#if defined(P4LWRP_USE_LIGHTPROBES)
    attenuation *= unity_ProbesOcclusion.x;
#endif
    attenuation *= saturate(dot(normalWS, _MainLightPosition.xyz));
    return attenuation * _MainLightColor.rgb;
}

P4LWRP_LIGHTCOLOR3 P4LWRP_CalculateAdditionalLightLambert(int index, float3 positionWS, half3 normalWS)
{
    float4 lightPositionWS = _AdditionalLightsPosition[index];
    half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[index];
    half4 spotDirection = _AdditionalLightsSpotDir[index];

    float3 lightVector = lightPositionWS.xyz - lightPositionWS.w * positionWS;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half distanceRcp = rsqrt(distanceSqr);
    lightVector *= distanceRcp;

    half attenuation = saturate(dot(lightVector, normalWS));
    attenuation *= P4LWRP_DistanceAttenuation_UsingRcp(distanceSqr, distanceRcp, distanceAndSpotAttenuation.xy);
    attenuation *= AngleAttenuation(spotDirection.xyz, lightVector, distanceAndSpotAttenuation.zw);

#if defined(P4LWRP_USE_LIGHTPROBES)
    half4 lightOcclusionProbeInfo = _AdditionalLightsOcclusionProbes[index];
    half probeOcclusion = max(unity_ProbesOcclusion[lightOcclusionProbeInfo.x], lightOcclusionProbeInfo.y);
    attenuation *= probeOcclusion;
#endif

    P4LWRP_LIGHTCOLOR3 color = _AdditionalLightsColor[index].rgb;
    return attenuation * color;
}

P4LWRP_ShadowLightData P4LWRP_GetMainLightData()
{
    P4LWRP_ShadowLightData light;
    light.direction = _MainLightPosition.xyz;
    light.color = _MainLightColor.rgb;
    P4LWRP_LIGHTCOLOR3 attenuation = unity_LightData.z;
#if defined(P4LWRP_USE_LIGHTPROBES)
    attenuation *= unity_ProbesOcclusion.x;
#endif
    light.color *= attenuation;

    return light;
}

P4LWRP_ShadowLightData P4LWRP_GetAdditionalLightData(int index, float3 positionWS)
{
    P4LWRP_ShadowLightData light;

#if defined(P4LWRP_LIGHTSOURCE_POINT) || defined(P4LWRP_LIGHTSOURCE_SPOT)
    float3 lightPositionWS = _AdditionalLightsPosition[index].xyz;
    float3 lightVector = lightPositionWS - positionWS;
    light.direction = lightVector; // do not normalize here. do in fragment shader.
    light.relativePos = lightVector;

    #if defined(P4LWRP_LIGHTSOURCE_SPOT)
        half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[p4lwrp_ShadowLightIndex];
        half4 spotDirection = _AdditionalLightsSpotDir[p4lwrp_ShadowLightIndex];
        half SdotL = dot(spotDirection, lightVector);
        light.spotAngleAttenuation.x = SdotL * distanceAndSpotAttenuation.z;
        light.spotAngleAttenuation.y = distanceAndSpotAttenuation.w;
    #endif
#else
    light.direction = _AdditionalLightsPosition[index].xyz;
#endif

    light.color = _AdditionalLightsColor[index].rgb;

#if defined(P4LWRP_USE_LIGHTPROBES)
    half4 lightOcclusionProbeInfo = _AdditionalLightsOcclusionProbes[index];
    half probeOcclusion = max(unity_ProbesOcclusion[lightOcclusionProbeInfo.x], lightOcclusionProbeInfo.y);
    light.color *= probeOcclusion;
#endif

    return light;
}

inline P4LWRP_LIGHTCOLOR3 P4LWRP_CalculateSH(half3 normalWS)
{
    return SampleSH(normalWS);
}

P4LWRP_LIGHTCOLOR3 P4LWRP_CalculateLambert(Light light, half3 normalWS)
{
    P4LWRP_LIGHTCOLOR3 color = light.color * light.distanceAttenuation;
    return LightingLambert(color, light.direction, normalWS);
}

P4LWRP_LIGHTCOLOR3 P4LWRP_CalculateAmbientColor(half3 normalWS, fixed3 positionWS)
{
    P4LWRP_LIGHTCOLOR3 ambientColor = P4LWRP_CalculateSH(normalWS);
#if defined(P4LWRP_AMBIENT_INCLUDE_ADDITIONAL_LIGHT) && (defined(_ADDITIONAL_LIGHTS_VERTEX) || defined(_ADDITIONAL_LIGHTS))
    int pixelLightCount = GetAdditionalLightsCount();
    for (int i = 0; i < pixelLightCount; ++i)
    {
        int index = GetPerObjectLightIndex(i);
#if defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW)
        half contribution = (p4lwrp_ShadowLightIndex == index) ? 0 : 1;
        ambientColor += contribution * P4LWRP_CalculateAdditionalLightLambert(index, positionWS, normalWS);
#else
        ambientColor += P4LWRP_CalculateAdditionalLightLambert(index, positionWS, normalWS);
#endif
    }
#endif
#if defined(P4LWRP_AMBIENT_INCLUDE_MAINLIGHT)
    ambientColor += P4LWRP_CalculateMainLightLambert(normalWS);
#endif
    return ambientColor;
}

P4LWRP_LIGHTCOLOR3 P4LWRP_CalculateVertexShadowColor(half3 normalWS, fixed3 positionWS)
{
    P4LWRP_LIGHTCOLOR3 shadowAdditionalLightColor = 0;
    P4LWRP_LIGHTCOLOR3 ambientColor = max(P4LWRP_CalculateSH(normalWS), 0.001f);
#if (defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW) || defined(P4LWRP_AMBIENT_INCLUDE_ADDITIONAL_LIGHT)) && (defined(_ADDITIONAL_LIGHTS_VERTEX) || defined(_ADDITIONAL_LIGHTS))
    int pixelLightCount = GetAdditionalLightsCount();
    for (int i = 0; i < pixelLightCount; ++i)
    {
        int index = GetPerObjectLightIndex(i);
        P4LWRP_LIGHTCOLOR3 lightColor = P4LWRP_CalculateAdditionalLightLambert(index, positionWS, normalWS);
        ambientColor += lightColor;
#if defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW) 
        shadowAdditionalLightColor = (p4lwrp_ShadowLightIndex == index) ? lightColor : shadowAdditionalLightColor;
#endif
    }
#elif defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW)
    shadowAdditionalLightColor = P4LWRP_CalculateAdditionalLightLambert(p4lwrp_ShadowLightIndex, positionWS, normalWS);
    return ambientColor / (ambientColor + shadowAdditionalLightColor);
#endif

    P4LWRP_LIGHTCOLOR3 mainLightColor = P4LWRP_CalculateMainLightLambert(normalWS);

#if defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW)
    ambientColor += mainLightColor;
    return (ambientColor - shadowAdditionalLightColor)/ambientColor;
#else
    return ambientColor / (mainLightColor + ambientColor);
#endif
}

P4LWRP_SHADOW_PROJECTOR_V2F P4LWRP_CalculateShadowProjectorParams(half3 worldNormal, fixed3 worldPos, float4 clipPos, float4 uvShadow)
{
	P4LWRP_SHADOW_PROJECTOR_V2F o;
    o.pos = clipPos;
    o.uvShadow = uvShadow;

#if defined(P4LWRP_PERPIXEL_SHADOWCOLOR)
#if defined(P4LWRP_ADDITIONAL_LIGHT_SHADOW)
	P4LWRP_ShadowLightData lightData = P4LWRP_GetAdditionalLightData(p4lwrp_ShadowLightIndex, worldPos);
    #if defined(P4LWRP_LIGHTSOURCE_POINT) || defined(P4LWRP_LIGHTSOURCE_SPOT)
        o.lightPos.xyz = lightData.relativePos;
        #if defined(P4LWRP_LIGHTSOURCE_SPOT)
            o.lightPos.w = lightData.spotAngleAttenuation.x;
            o.lightColor.w = lightData.spotAngleAttenuation.y;
        #endif
    #endif
#else
	P4LWRP_ShadowLightData lightData = P4LWRP_GetMainLightData();
#endif
    o.lightColor.xyz = lightData.color * dot(worldNormal, lightData.direction);
	o.ambientColor.xyz = P4LWRP_CalculateAmbientColor(worldNormal, worldPos);
    P4LWRP_TRANSFER_FOGCOORD(o.ambientColor.w, clipPOs);
#else
    o.shadowColor.xyz = P4LWRP_CalculateVertexShadowColor(worldNormal, worldPos);
    P4LWRP_TRANSFER_FOGCOORD(o.shadowColor.w, clipPOs);
#endif

    return o;
}

P4LWRP_LIGHTCOLOR3 P4LWRP_SampleLightmap(half2 lightmapUV)
{
#ifdef UNITY_LIGHTMAP_FULL_HDR
    bool encodedLightmap = false;
#else
    bool encodedLightmap = true;
#endif

    half4 decodeInstructions = half4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0h, 0.0h);

    // The shader library sample lightmap functions transform the lightmap uv coords to apply bias and scale.
    // However, lightweight pipeline already transformed those coords in vertex. We pass half4(1, 1, 0, 0) and
    // the compiler will optimize the transform away.
    half4 transformCoords = half4(1, 1, 0, 0);
        
#if defined(LIGHTMAP_ON)
    return SampleSingleLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), lightmapUV, transformCoords, encodedLightmap, decodeInstructions);
#else
    return half3(0.0, 0.0, 0.0);
#endif
}

void P4LWRP_ApplyLightmap(inout P4LWRP_LIGHTCOLOR3 lightColor, inout P4LWRP_LIGHTCOLOR3 ambientColor, half2 lightmapUV)
{
#if defined(P4LWRP_LIGHTMAP_ON)
    P4LWRP_LIGHTCOLOR3 bakedColor = P4LWRP_SampleLightmap(lightmapUV);
    #if defined(SHADOWS_SHADOWMASK) && defined(P4LWRP_MIXED_LIGHT_SHADOWMASK)
        // TODO: shadowmask may contains additonal lights which are included in ambientColor.
        // however, Lightweight RP does not support shadowmaks so far. we do not take much care about this case.
        fixed4 shadowMask = UNITY_SAMPLE_TEX2D(unity_ShadowMask, lightmapUV);
        lightColor *= dot(shadowMask, p4lwrp_ShadowMaskSelector);
    #endif
    #if defined(P4LWRP_MIXED_LIGHT_SUBTRACTIVE)
        P4LWRP_LIGHTCOLOR3 subtract = max(bakedColor - lightColor, _SubtractiveShadowColor.xyz);
        lightColor = max(bakedColor - subtract, P4LWRP_LIGHTCOLOR3(0,0,0));
        ambientColor += bakedColor - lightColor;
    #else
        ambientColor += bakedColor;
    #endif
#endif
}

#if defined(P4LWRP_PERPIXEL_SHADOWCOLOR)
P4LWRP_LIGHTCOLOR3 P4LWRP_CalculatePerPixelShadowLightColor(P4LWRP_SHADOW_PROJECTOR_V2F i)
{
#if defined(P4LWRP_LIGHTSOURCE_POINT) || defined(P4LWRP_LIGHTSOURCE_SPOT)
    half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[p4lwrp_ShadowLightIndex];
    half3 lightVector = i.lightPos.xyz;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    float distanceRcp = rsqrt(distanceSqr);
    half attenuation = P4LWRP_DistanceAttenuation_UsingRcp(distanceSqr, distanceRcp, distanceAndSpotAttenuation.xy);
#if defined(P4LWRP_LIGHTSOURCE_SPOT)
    half angleAttenuation = saturate(i.lightPos.w * distanceRcp + i.lightColor.w); // normalize light direction.
    attenuation *= angleAttenuation * angleAttenuation;
#endif
    return max(P4LWRP_LIGHTCOLOR3(0,0,0), i.lightColor.xyz * attenuation * distanceRcp); // normalize light direction.
#else
    return max(P4LWRP_LIGHTCOLOR3(0,0,0), i.lightColor.xyz);
#endif
}
#endif

fixed4 P4LWRP_CalculateShadowProjectorFragmentOutput(P4LWRP_SHADOW_PROJECTOR_V2F i, fixed3 shadow)
{
#if defined(P4LWRP_PERPIXEL_SHADOWCOLOR)
    P4LWRP_LIGHTCOLOR3 lightColor = P4LWRP_CalculatePerPixelShadowLightColor(i);
    P4LWRP_LIGHTCOLOR3 ambientColor = i.ambientColor.xyz;
#if defined(P4LWRP_LIGHTMAP_ON)
    P4LWRP_ApplyLightmap(lightColor, ambientColor, i.lightmapUV.xy);
#endif
    fixed3 shadowColor = (shadow * lightColor + ambientColor)/(lightColor + ambientColor);
    UNITY_APPLY_FOG_COLOR(i.ambientColor.w, shadowColor, fixed3(1,1,1));
#else
	fixed3 shadowColor = lerp(i.shadowColor, fixed3(1,1,1), shadow); // shadowColor = ambientColor/(lightColor + ambientColor)
    UNITY_APPLY_FOG_COLOR(i.shadowColor.w, shadowColor, fixed3(1,1,1));
#endif
    return fixed4(shadowColor, 1);
}

P4LWRP_SHADOW_PROJECTOR_V2F P4LWRP_ShadowProjectorVertexFunc(P4LWRP_SHADOW_PROJECTOR_VERTEX v)
{
    UNITY_SETUP_INSTANCE_ID(v);

	float3 worldPos = TransformObjectToWorld(v.vertex.xyz);
    half3 worldNormal = TransformObjectToWorldNormal(v.normal);
    float4 clipPos = TransformWorldToHClip(worldPos);
#if defined(FSR_RECEIVER)
	float4 shadowUV = mul(_FSRProjector, v.vertex);
#elif defined(FSR_PROJECTOR_FOR_LWRP)
	float4 uvShadow = mul(_FSRWorldToProjector, fixed4(worldPos, 1));
#else
	float4 uvShadow = mul(unity_Projector, v.vertex);
	uvShadow.z = mul(unity_ProjectorClip, v.vertex).x;
#endif
    P4LWRP_SHADOW_PROJECTOR_V2F o = P4LWRP_CalculateShadowProjectorParams(worldNormal, worldPos, clipPos, uvShadow);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    return o;
}

#endif // !defined(P4LWRPSHADOW_CGINC_INCLUDED)
