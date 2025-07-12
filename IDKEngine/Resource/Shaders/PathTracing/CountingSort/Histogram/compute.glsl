#version 460 core

AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;

uint GetItemCount();
uint GetItem(uint index);
uint GenerateKey(uint item);

void main()
{
    uint invocationId = gl_GlobalInvocationID.x;
    if (invocationId >= GetItemCount())
    {
        return;
    }

    uint item = GetItem(invocationId);
    uint key = GenerateKey(item);

    atomicAdd(workGroupPrefixSumSSBO.PrefixSum[key], 1u);
    cachedKeySSBO.Keys[invocationId] = key;
}

uint GetItemCount()
{
    return wavefrontPTSSBO.Counts[wavefrontPTSSBO.PingPongIndex];
}

uint GetItem(uint index)
{
    return wavefrontPTSSBO.AliveRayIndices[index];
}

uint GenerateKey(uint item)
{
    // This function should generate a key that is no larger than the prefix sum capacity

    vec3 rayOrigin = wavefrontRaySSBO.Rays[item].Origin;
    vec3 rayBoundsMin = KeyToFloat(Unpack(wavefrontPTSSBO.RayBoundsMin));
    vec3 rayBoundsMax = KeyToFloat(Unpack(wavefrontPTSSBO.RayBoundsMax));
    uint key = GetMorton(MapToZeroOne(rayOrigin, rayBoundsMin, rayBoundsMax));

    const uint bitsToSort = 16;
    const uint keyLength = 30; // morton code only uses lower 30 bits
    key >>= (30 - bitsToSort);
    
    return key;
}