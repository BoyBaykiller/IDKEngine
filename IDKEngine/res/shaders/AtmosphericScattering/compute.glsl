// Atmospheric scattering code adapted to single pass compute shader
// Source: https://github.com/wwwtyro/glsl-atmosphere

#version 460 core
#define PI 3.14159265

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0) restrict writeonly uniform imageCube ImgResult;

vec2 Rsi(vec3 r0, vec3 rd, float sr);
vec3 Atmosphere(vec3 r, vec3 r0, vec3 pSun, float iSun, float rPlanet, float rAtmos, vec3 kRlh, float kMie, float shRlh, float shMie, float g);

uniform vec3 LightPos;
uniform float LightIntensity;
uniform int ISteps;
uniform int JSteps;

uniform mat4 InvViews[6];
uniform mat4 InvProjection;

AppInclude(include/Transformations.glsl)

void main()
{
    ivec3 imgCoord = ivec3(gl_GlobalInvocationID);
    vec2 ndc = vec2(imgCoord.xy) / imageSize(ImgResult) * 2.0 - 1.0;
    
    vec3 toCubemap = GetWorldSpaceDirection(InvProjection, InvViews[imgCoord.z], ndc);

    vec3 color = Atmosphere(
        toCubemap,                     // normalized ray direction
        vec3(0, 6376e3, 0),             // ray origin
        LightPos,                       // position of the sun
        LightIntensity,                 // intensity of the sun
        6371e3,                         // radius of the planet in meters
        6471e3,                         // radius of the atmosphere in meters
        vec3(5.5e-6, 13.0e-6, 22.4e-6), // Rayleigh scattering coefficient
        21e-6,                          // Mie scattering coefficient
        8e3,                            // Rayleigh scale height
        1.2e3,                          // Mie scale height
        0.758                           // Mie preferred scattering direction
    );

    //color = pow(color, vec3(2.2));
    //color = 1.0 - exp(-1.0 * color);
    imageStore(ImgResult, imgCoord, vec4(color, 1.0));
}

vec2 Rsi(vec3 r0, vec3 rd, float sr)
{
    // ray-sphere intersection that assumes
    // the sphere is centered at the origin.
    // No intersection when result.x > result.y
    float a = dot(rd, rd);
    float b = 2.0 * dot(rd, r0);
    float c = dot(r0, r0) - sr * sr;
    float d = b * b - 4.0 * a * c;
    
    if (d < 0.0) return vec2(1e5,-1e5);
    return vec2((-b - sqrt(d)) / (2.0 * a), (-b + sqrt(d)) / (2.0 * a));
}

vec3 Atmosphere(vec3 r, vec3 r0, vec3 pSun, float iSun, float rPlanet, float rAtmos, vec3 kRlh, float kMie, float shRlh, float shMie, float g)
{
    // Normalize the sun and view directions.
    pSun = normalize(pSun);
    r = normalize(r);

    // Calculate the step size of the primary ray.
    vec2 p = Rsi(r0, r, rAtmos);
    if (p.x > p.y) return vec3(0,0,0);
    p.y = min(p.y, Rsi(r0, r, rPlanet).x);
    float IStepsize = (p.y - p.x) / float(ISteps);

    // Initialize the primary ray time.
    float iTime = 0.0;

    // Initialize accumulators for Rayleigh and Mie scattering.
    vec3 totalRlh = vec3(0,0,0);
    vec3 totalMie = vec3(0,0,0);

    // Initialize optical depth accumulators for the primary ray.
    float iOdRlh = 0.0;
    float iOdMie = 0.0;

    // Calculate the Rayleigh and Mie phases.
    float mu = dot(r, pSun);
    float mumu = mu * mu;
    float gg = g * g;
    float pRlh = 3.0 / (16.0 * PI) * (1.0 + mumu);
    float pMie = 3.0 / (8.0 * PI) * ((1.0 - gg) * (mumu + 1.0)) / (pow(1.0 + gg - 2.0 * mu * g, 1.5) * (2.0 + gg));

    // Sample the primary ray.
    for (int i = 0; i < ISteps; i++) {

        // Calculate the primary ray sample position.
        vec3 iPos = r0 + r * (iTime + IStepsize * 0.5);

        // Calculate the height of the sample.
        float iHeight = length(iPos) - rPlanet;

        // Calculate the optical depth of the Rayleigh and Mie scattering for this step.
        float odStepRlh = exp(-iHeight / shRlh) * IStepsize;
        float odStepMie = exp(-iHeight / shMie) * IStepsize;

        // Accumulate optical depth.
        iOdRlh += odStepRlh;
        iOdMie += odStepMie;

        // Calculate the step size of the secondary ray.
        float JStepsize = Rsi(iPos, pSun, rAtmos).y / float(JSteps);

        // Initialize the secondary ray time.
        float jTime = 0.0;

        // Initialize optical depth accumulators for the secondary ray.
        float jOdRlh = 0.0;
        float jOdMie = 0.0;

        // Sample the secondary ray.
        for (int j = 0; j < JSteps; j++) {

            // Calculate the secondary ray sample position.
            vec3 jPos = iPos + pSun * (jTime + JStepsize * 0.5);

            // Calculate the height of the sample.
            float jHeight = length(jPos) - rPlanet;

            // Accumulate the optical depth.
            jOdRlh += exp(-jHeight / shRlh) * JStepsize;
            jOdMie += exp(-jHeight / shMie) * JStepsize;

            // Increment the secondary ray time.
            jTime += JStepsize;
        }

        // Calculate attenuation.
        vec3 attn = exp(-(kMie * (iOdMie + jOdMie) + kRlh * (iOdRlh + jOdRlh)));

        // Accumulate scattering.
        totalRlh += odStepRlh * attn;
        totalMie += odStepMie * attn;

        // Increment the primary ray time.
        iTime += IStepsize;
    }

    // Calculate and return the final color.
    return iSun * (pRlh * kRlh * totalRlh + pMie * kMie * totalMie);
}
