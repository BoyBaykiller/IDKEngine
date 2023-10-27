#ifndef Transformations_H
#define Transformations_H

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
    // Source: https://github.com/bnpr/Malt/blob/290d2f1169b1367415d3cba7a13ce75c763eeaba/Malt/Shaders/Lighting/Lighting.glsl#L212
    float depth = (far + near) / (far - near) - (2.0 * far * near) / (far - near) / z;

    return depth;
}

vec3 PerspectiveTransform(vec3 ndc, mat4 matrix)
{
    vec4 worldPos = matrix * vec4(ndc, 1.0);
    return worldPos.xyz / worldPos.w;
}

vec3 PerspectiveTransformUvDepth(vec3 uvAndDepth, mat4 matrix)
{
    vec3 ndc;
    ndc.xy = uvAndDepth.xy * 2.0 - 1.0;
    ndc.z = uvAndDepth.z;
    return PerspectiveTransform(ndc, matrix);
}

mat3 GetTBN(mat4 matrix, vec3 tangent, vec3 normal)
{
    vec3 T = normalize((matrix * vec4(tangent, 0.0)).xyz);
    vec3 N = normalize((matrix * vec4(normal, 0.0)).xyz);
    T = normalize(T - dot(T, N) * N);
    vec3 B = cross(N, T);
    mat3 TBN = mat3(T, B, N);
    return TBN;
}

float MapRangeToAnOther(float value, float valueMin, float valueMax, float mapMin, float mapMax)
{
    return (value - valueMin) / (valueMax - valueMin) * (mapMax - mapMin) + mapMin;
}

#endif