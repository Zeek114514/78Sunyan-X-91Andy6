#ifndef VOXEL_STYLIZED_LIGHTING
#define VOXEL_STYLIZED_LIGHTING
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
half3 VoxelSoftLight(float3 positionWS, half3 normalWS)
{
    Light sun = GetMainLight(TransformWorldToShadowCoord(positionWS));
    half wrapped = smoothstep(-0.25h, 0.7h, dot(SafeNormalize(normalWS), sun.direction));
    half shadow = lerp(0.48h, 1.0h, sun.shadowAttenuation);
    half3 shade = half3(0.62h, 0.73h, 0.87h);
    half3 warm = half3(1.04h, 1.02h, 0.94h) * lerp(half3(1,1,1), saturate(sun.color), 0.25h);
    return lerp(shade, warm, wrapped * shadow);
}
#endif
