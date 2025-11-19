#version 460 core

AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(PathTracing/CountingSort/BlellochScan/include/Constants.glsl)

layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;

uint GetItemCount();
uint GetItem(uint index);
void SetItem(uint offset, uint item);

void main()
{
    uint invocationId = gl_GlobalInvocationID.x;
    if (invocationId >= GetItemCount())
    {
        return;
    }

    uint item = GetItem(invocationId);
    uint key = cachedKeySSBO.Keys[invocationId];

    uint blockOffset = workGroupSumsPrefixSumSSBO.Sums[key / BLOCK_WISE_PROGRAM_LOCAL_SIZE_X];
    uint globalOffset = blockOffset + atomicAdd(workGroupPrefixSumSSBO.PrefixSum[key], 1u);

    SetItem(globalOffset, item);

    if (invocationId == 0)
    {
        // Reset data for next ray bounce
        wavefrontPTSSBO.RayBoundsMin[wavefrontPTSSBO.PingPongIndex] = Pack(FloatToKey(vec3(FLOAT_MAX)));
        wavefrontPTSSBO.RayBoundsMax[wavefrontPTSSBO.PingPongIndex] = Pack(FloatToKey(vec3(FLOAT_MIN)));
    }
}

uint GetItem(uint index)
{
    return wavefrontPTSSBO.AliveRayIndices[index];
}

void SetItem(uint offset, uint item)
{
    sortedRayIndicesSSBO.Indices[offset] = item;
}

uint GetItemCount()
{
    return wavefrontPTSSBO.Counts[wavefrontPTSSBO.PingPongIndex];
}