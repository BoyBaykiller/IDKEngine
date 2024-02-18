#ifndef Transformations_H
#define Transformations_H

vec3 GetWorldSpaceDirection(mat4 inverseProj, mat4 inverseView, vec2 normalizedDeviceCoords)
{   
    vec4 rayView;
    rayView.xy = mat2(inverseProj) * normalizedDeviceCoords;
    rayView.z = -1.0;
    rayView.w = 0.0;

    vec3 rayWorld = normalize((inverseView * rayView).xyz);
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

// Source: https://learnopengl.com/Advanced-OpenGL/Depth-testing
float GetLogarithmicDepth(float near, float far, float viewZ)
{
    // https://www.desmos.com/calculator/yexmazn9yq
    float depth = (1.0 / viewZ - 1.0 / near) / (1.0 / far - 1.0 / near);
    return depth;
}

float LogarithmicDepthToLinearViewDepth(float near, float far, float ndcZ) 
{
    // https://www.desmos.com/calculator/yexmazn9yq
    float depth = (2.0 * near * far) / (far + near - ndcZ * (far - near));
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

mat3 GetTBN(vec3 tangent, vec3 normal)
{
    vec3 T = tangent;
    vec3 N = normal;
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