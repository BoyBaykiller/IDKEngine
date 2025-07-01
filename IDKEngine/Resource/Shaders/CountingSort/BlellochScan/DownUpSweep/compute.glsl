#version 460 core

AppInclude(include/Math.glsl)
AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(CountingSort/BlellochScan/include/Constants.glsl)

layout(local_size_x = DOWN_UP_SWEEP_PROGRAM_LOCAL_SIZE_X, local_size_y = 1, local_size_z = 1) in;

shared uint SharedValues[gl_WorkGroupSize.x * 2];

void main()
{
    uint invocationId = gl_LocalInvocationIndex;
    int blellochScanSteps = CeilLog2Int(workGroupSumsPrefixSumSSBO.Sums.length());

    SharedValues[invocationId * 2 + 0] = workGroupSumsPrefixSumSSBO.Sums[invocationId * 2 + 0];
    SharedValues[invocationId * 2 + 1] = workGroupSumsPrefixSumSSBO.Sums[invocationId * 2 + 1];
    barrier();

    for (int i = 0; i < blellochScanSteps; i++)
    {
        uint offset = 1u << i;
        uint stride = 1u << (i + 1);
        uint invocations = SharedValues.length() / stride;

        if (invocationId < invocations)
        {
            uint otherId = (invocationId + 1) * stride - 1;
            uint firstId = otherId - offset;

            uint first = SharedValues[firstId];
            uint other = SharedValues[otherId];

            SharedValues[otherId] = first + other;
        }

        barrier();
    }

    if (gl_LocalInvocationIndex == 0)
    {
        SharedValues[SharedValues.length() - 1] = 0;
    }
    
    for (int i = blellochScanSteps - 1; i >= 0; i--)
    {
        barrier();

        uint offset = 1u << i;
        uint stride = 1u << (i + 1);
        uint invocations = DivUp(SharedValues.length() - offset, stride);

        if (invocationId < invocations)
        {
            uint otherId = (invocationId + 1) * stride - 1;
            uint firstId = otherId - offset;
            otherId = min(otherId, SharedValues.length() - 1);

            uint first = SharedValues[otherId];
            uint other = SharedValues[firstId] + first;

            SharedValues[firstId] = first;
            SharedValues[otherId] = other;
        }
    }

    barrier();

    workGroupSumsPrefixSumSSBO.Sums[invocationId * 2 + 0] = SharedValues[invocationId * 2 + 0];
    workGroupSumsPrefixSumSSBO.Sums[invocationId * 2 + 1] = SharedValues[invocationId * 2 + 1];
}