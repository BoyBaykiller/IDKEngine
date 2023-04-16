#version 460 core
#extension GL_ARB_bindless_texture : require

struct PointShadow
{
    samplerCube Texture;
    samplerCubeShadow ShadowTexture;
    
    mat4 ProjViewMatrices[6];

    vec3 Position;
    float NearPlane;

    vec3 _pad0;
    float FarPlane;
};

layout(std140, binding = 1) uniform ShadowDataUBO
{
    #define GLSL_MAX_UBO_POINT_SHADOW_COUNT 16 // used in shader and client code - keep in sync!
    PointShadow PointShadows[GLSL_MAX_UBO_POINT_SHADOW_COUNT];
    int Count;
} shadowDataUBO;

in InOutVars
{
    vec3 FragPos;
} inData;

layout(location = 0) uniform int ShadowIndex;

void main()
{
    PointShadow pointShadow = shadowDataUBO.PointShadows[ShadowIndex];

    float twoDist = dot(inData.FragPos - pointShadow.Position, inData.FragPos - pointShadow.Position);
    float twoNearPlane = pointShadow.NearPlane * pointShadow.NearPlane;
    float twoFarPlane = pointShadow.FarPlane * pointShadow.FarPlane;

    // map from [nearPlane; farPlane] to [0.0; 1.0]
    float depthValue = (twoDist - twoNearPlane) / (twoFarPlane - twoNearPlane);
    
    gl_FragDepth = depthValue;
}