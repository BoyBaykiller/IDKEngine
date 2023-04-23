#version 460 core

AppInclude(shaders/include/Buffers.glsl)

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