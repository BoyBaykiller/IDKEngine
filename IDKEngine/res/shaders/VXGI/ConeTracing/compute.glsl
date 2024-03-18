#version 460 core
#extension GL_ARB_bindless_texture : require

AppInclude(include/Random.glsl)
AppInclude(include/Constants.glsl)
AppInclude(include/Transformations.glsl)

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform image2D ImgResult;
layout(binding = 0) uniform sampler3D SamplerVoxelsAlbedo;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    mat4 PrevView;
    vec3 ViewPos;
    uint Frame;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
    float DeltaRenderTime;
    float Time;
} basicDataUBO;

layout(std140, binding = 3) uniform TaaDataUBO
{
    vec2 Jitter;
    int SampleCount;
    float MipmapBias;
    int TemporalAntiAliasingMode;
} taaDataUBO;

layout(std140, binding = 4) uniform SkyBoxUBO
{
    samplerCube Albedo;
} skyBoxUBO;

layout(std140, binding = 5) uniform VoxelizerDataUBO
{
    mat4 OrthoProjection;
    vec3 GridMin;
    float _pad0;
    vec3 GridMax;
    float _pad1;
} voxelizerDataUBO;

layout(std140, binding = 6) uniform GBufferDataUBO
{
    sampler2D AlbedoAlpha;
    sampler2D NormalSpecular;
    sampler2D EmissiveRoughness;
    sampler2D Velocity;
    sampler2D Depth;
} gBufferDataUBO;

vec3 IndirectLight(vec3 point, vec3 incomming, vec3 normal, float specularChance, float roughness);
float GetMaterialVariance(float specularChance, float roughness);

uniform float NormalRayOffset;
uniform int MaxSamples;
uniform float GIBoost;
uniform float GISkyBoxBoost;
uniform float StepMultiplier;
uniform bool IsTemporalAccumulation;

AppInclude(VXGI/include/TraceCone.glsl)

void main()
{
    ivec2 imgCoord = ivec2(gl_GlobalInvocationID.xy);
    vec2 uv = (imgCoord + 0.5) / imageSize(ImgResult);

    float depth = texelFetch(gBufferDataUBO.Depth, imgCoord, 0).r;
    if (depth == 1.0)
    {
        imageStore(ImgResult, imgCoord, vec4(0.0));
        return;
    }

    vec3 fragPos = PerspectiveTransformUvDepth(vec3(uv, depth), basicDataUBO.InvProjView);
    vec3 normal = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).rgb;
    float specular = texelFetch(gBufferDataUBO.NormalSpecular, imgCoord, 0).a;
    float roughness = texelFetch(gBufferDataUBO.EmissiveRoughness, imgCoord, 0).a;

    vec3 viewDir = fragPos - basicDataUBO.ViewPos;
    vec3 indirectLight = IndirectLight(fragPos, viewDir, normal, specular, roughness) * GIBoost;

    imageStore(ImgResult, imgCoord, vec4(indirectLight, 1.0));
}

vec3 IndirectLight(vec3 position, vec3 incomming, vec3 normal, float specularChance, float roughness)
{    
    vec3 irradiance = vec3(0.0);
    float materialVariance = GetMaterialVariance(specularChance, roughness);
    uint samples = uint(mix(1.0, float(MaxSamples), materialVariance));

    bool taaEnabled = taaDataUBO.TemporalAntiAliasingMode != TEMPORAL_ANTI_ALIASING_MODE_NO_AA;
    uint noiseIndex = (IsTemporalAccumulation && taaEnabled) ? (basicDataUBO.Frame % taaDataUBO.SampleCount) * MaxSamples : 0u;
    for (uint i = 0; i < samples; i++)
    {
        float rnd0 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 0);
        float rnd1 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 1);
        float rnd2 = InterleavedGradientNoise(vec2(gl_GlobalInvocationID.xy), noiseIndex + 2);
        noiseIndex++;
        
        Ray coneRay;
        coneRay.Origin = position;
        coneRay.Direction = CosineSampleHemisphere(normal, rnd1, rnd0);

        
        const float maxConeAngle = 0.32;
        const float minConeAngle = 0.005;
        float coneAngle;
        if (specularChance > rnd2)
        {
            vec3 reflectionDir = reflect(incomming, normal);
            reflectionDir = normalize(mix(reflectionDir, coneRay.Direction, roughness));
            coneRay.Direction = reflectionDir;
            
            coneAngle = mix(minConeAngle, maxConeAngle, roughness);
        }
        else
        {
            coneAngle = maxConeAngle;
        }

        // Experimental: For low cone angles we can get away with a lower step size 
        // float adaptiveStepSizeAdjustment = MapRangeToAnOther(coneAngle, minConeAngle, maxConeAngle, 1.0, 3.0); 

        vec4 coneTrace = TraceCone(coneRay, normal, coneAngle, StepMultiplier, NormalRayOffset, 0.99);
        coneTrace += (1.0 - coneTrace.a) * (texture(skyBoxUBO.Albedo, coneRay.Direction) * GISkyBoxBoost);
        
        irradiance += coneTrace.rgb;
    }
    irradiance /= float(samples);

    return irradiance;
}

float GetMaterialVariance(float specularChance, float roughness)
{
    float diffuseChance = 1.0 - specularChance;
    float perceivedFinalRoughness = 1.0 - (specularChance * (1.0 - roughness));
    return mix(perceivedFinalRoughness, 1.0, diffuseChance);
}

