#ifndef Transformations_H
#define Transformations_H

#define DEPTH_ZERO_TO_ONE 0

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{
    vec4 rayEye = inverseProj * vec4(normalizedDeviceCoords, -1.0, 0.0);
    rayEye.zw = vec2(-1.0, 0.0);
    vec3 rayWorld = normalize((inverseView * rayEye).xyz);
    return rayWorld;
}

vec3 Interpolate(vec3 v0, vec3 v1, vec3 v2, vec3 bary)
{
    return v0 * bary.x + v1 * bary.y + v2 * bary.z;
}

vec2 Interpolate(vec2 v0, vec2 v1, vec2 v2, vec3 bary)
{
    return v0 * bary.x + v1 * bary.y + v2 * bary.z;
}

float GetLogarithmicDepth(float near, float far, float z)
{
#if DEPTH_ZERO_TO_ONE
    // Source: https://github.com/bnpr/Malt/blob/290d2f1169b1367415d3cba7a13ce75c763eeaba/Malt/Shaders/Lighting/Lighting.glsl#L212
    float depth = (far + near) / (far - near) - (2.0 * far * near) / (far - near) / z;
#else
    float depth = ((1.0 / z) - (1.0 / near)) / ((1.0 / far) - (1.0  / near));
#endif
    return depth;
}

vec3 UvDepthToNdc(vec3 uvAndDepth)
{
#if DEPTH_ZERO_TO_ONE
    return vec3(uvAndDepth.xy * 2.0 - 1.0, uvAndDepth.z);
#else
    return uvAndDepth * 2.0 - 1.0;
#endif
}

vec3 NdcToUvDepth(vec3 ndc)
{
#if DEPTH_ZERO_TO_ONE
    return vec3(ndc.xy * 0.5 + 0.5, ndc.z);
#else
    return ndc * 0.5 + 0.5;
#endif
}

vec3 NdcToWorldSpace(vec3 ndc, mat4 inverseProjView)
{
    vec4 worldPos = inverseProjView * vec4(ndc, 1.0);
    return worldPos.xyz / worldPos.w;
}

vec3 UvDepthToWorldSpace(vec3 uvAndDepth, mat4 inverseProjView)
{
    vec3 ndc = UvDepthToNdc(uvAndDepth);
    return NdcToWorldSpace(ndc, inverseProjView);
}

#endif