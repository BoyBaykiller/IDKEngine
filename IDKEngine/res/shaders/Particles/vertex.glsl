#version 430 core
#define EPSILON 0.001
const float DRAG_COEF = log(0.998) * 176.0; // log(0.70303228048)

struct Particle
{
    vec3 Position;
    vec3 Velocity;
};

layout(std430, binding = 5) restrict buffer ParticlesSSBO
{
    Particle Particles[];
} particlesSSBO;

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

layout(location = 0) uniform float dT;
layout(location = 1) uniform vec3 pointOfMass;
layout(location = 2) uniform float isActive;
layout(location = 3) uniform float isRunning;
layout(location = 4) uniform mat4 projViewMatrix;

out InOutVars
{
    vec4 Color;
} outData;

void main()
{
    Particle particle = particlesSSBO.Particles[gl_VertexID];

    const vec3 toMass = pointOfMass - particle.Position;
    const float distSqured = max(dot(toMass, toMass), EPSILON * EPSILON);
    
    const vec3 acceleration = (5.0 * toMass * isRunning * isActive) / distSqured;
    particle.Velocity *= mix(1.0, exp(DRAG_COEF * dT), isRunning); // https://stackoverflow.com/questions/61812575/which-formula-to-use-for-drag-simulation-each-frame
    particle.Position += (dT * particle.Velocity + 0.5 * acceleration * dT * dT) * isRunning;
    particle.Velocity += acceleration * dT;
    particlesSSBO.Particles[gl_VertexID] = particle;

    outData.Color = vec4(vec3(0.1), 0.25);
    gl_Position = basicDataUBO.ProjView * vec4(particle.Position, 1.0);
}