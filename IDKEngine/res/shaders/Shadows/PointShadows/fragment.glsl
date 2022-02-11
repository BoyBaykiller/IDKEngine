#version 460 core
#extension GL_ARB_bindless_texture : require

struct Light
{
    vec3 Position;
    float Radius;
    vec3 Color;
    float _pad0;
};

struct PointShadow
{
    samplerCubeShadow Sampler;
    float NearPlane;
    float FarPlane;

    mat4 ProjViewMatrices[6];

    vec3 _pad0;
    int LightIndex;
};

layout(std140, binding = 2) uniform ShadowDataUBO
{
    PointShadow PointShadows[8];
    int PointCount;
} shadowDataUBO;

layout(std140, binding = 3) uniform LightsUBO
{
    Light Lights[128];
    int LightCount;
} lightsUBO;

in InOutVars
{
    vec3 FragPos;
} inData;

layout(location = 0) uniform int ShadowIndex;

void main()
{
    PointShadow pointShadow = shadowDataUBO.PointShadows[ShadowIndex];
    vec3 position = lightsUBO.Lights[pointShadow.LightIndex].Position;

    float twoDist = dot(inData.FragPos - position, inData.FragPos - position);
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;

    // map from [nearPlane; farPlane] to [0.0; 1.0]
    float depthValue = (twoDist - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    gl_FragDepth = depthValue;
}