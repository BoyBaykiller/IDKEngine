AppInclude(include/Constants.glsl)
AppInclude(include/Math.glsl)

uint Random_RNGSeed;

void InitializeRandomSeed(uint value)
{
    Random_RNGSeed = value;
}

uint GetPCGHash(inout uint seed)
{
    // Source: https://www.reedbeta.com/blog/hash-functions-for-gpu-rendering/

    seed = seed * 747796405u + 2891336453u;
    uint word = ((seed >> ((seed >> 28u) + 4u)) ^ seed) * 277803737u;
    return (word >> 22u) ^ word;
}

float GetRandomFloat01()
{
    return float(GetPCGHash(Random_RNGSeed)) / 4294967296.0;
}

float InterleavedGradientNoise(vec2 imgCoord, uint index)
{
    // Source: https://www.shadertoy.com/view/WsfBDf
    
    imgCoord += float(index) * 5.588238;
    return fract(52.9829189 * fract(0.06711056 * imgCoord.x + 0.00583715 * imgCoord.y));
}
