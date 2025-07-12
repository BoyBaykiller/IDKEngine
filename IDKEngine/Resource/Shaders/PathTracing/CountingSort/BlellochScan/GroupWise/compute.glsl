#version 460 core
#extension GL_KHR_shader_subgroup_arithmetic : require

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(PathTracing/CountingSort/BlellochScan/include/Constants.glsl)

layout(local_size_x = BLOCK_WISE_PROGRAM_LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

shared uint SharedSubgroupSumsPrefixSum[gl_WorkGroupSize.x / MIN_SUBGROUP_SIZE];
shared uint SharedGroupPrefixSums[gl_WorkGroupSize.x]; 

void main()
{
    uint invocationId = gl_GlobalInvocationID.x;
    uint count = workGroupPrefixSumSSBO.PrefixSum[invocationId];

    SharedGroupPrefixSums[gl_LocalInvocationIndex] = subgroupExclusiveAdd(count);
    SharedSubgroupSumsPrefixSum[gl_SubgroupID] = subgroupAdd(count);

    barrier();

    if (gl_LocalInvocationIndex == 0)
    {
        uint groupSum = 0;
        for (uint i = 0; i < gl_NumSubgroups; i++)
        {
            uint temp = SharedSubgroupSumsPrefixSum[i];
            SharedSubgroupSumsPrefixSum[i] = groupSum;
            groupSum += temp;
        }

        workGroupSumsPrefixSumSSBO.Sums[gl_WorkGroupID.x] = groupSum;
    }

    barrier();

    uint subgroupOffset = SharedSubgroupSumsPrefixSum[gl_SubgroupID];
    uint groupPrefixSum = subgroupOffset + SharedGroupPrefixSums[gl_LocalInvocationIndex];
   
    workGroupPrefixSumSSBO.PrefixSum[invocationId] = groupPrefixSum;
}