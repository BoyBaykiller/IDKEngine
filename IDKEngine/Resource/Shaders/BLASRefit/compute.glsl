#version 460 core

AppInclude(include/Math.glsl)
AppInclude(include/Box.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

void SetNodeBounds(uint index, Box bounds);
Box ComputeBoundingBox(uint start, uint count);

layout(location = 0) uniform uint BlasIndex;

void main()
{
    GpuBlasDesc blasDesc = blasDescSSBO.Descs[BlasIndex];

    if (gl_GlobalInvocationID.x >= blasDesc.LeafIndicesCount)
    {
        return;
    }
    
    uint leafId = blasLeafIndicesSSBO.Indices[blasDesc.LeafIndicesOffset + gl_GlobalInvocationID.x];

    GpuBlasNode leafNode = blasNodeSSBO.Nodes[blasDesc.NodeOffset + leafId];
    Box bounds = ComputeBoundingBox(blasDesc.GeometryDesc.TriangleOffset + leafNode.TriStartOrChild, leafNode.TriCount);
    SetNodeBounds(blasDesc.NodeOffset + leafId, bounds);

    int parentId = blasParentIndicesSSBO.Indices[blasDesc.NodeOffset + leafId];
    do
    {
        if (atomicExchange(blasRefitLocksSSBO.Locks[parentId], 1u) == 0u)
        {
            // Thread arrived first, meaning the other child is not refitted yet, terminate.
            return;
        }

        GpuBlasNode parent = blasNodeSSBO.Nodes[blasDesc.NodeOffset + parentId];
        GpuBlasNode left = blasNodeSSBO.Nodes[blasDesc.NodeOffset + parent.TriStartOrChild];
        GpuBlasNode right = blasNodeSSBO.Nodes[blasDesc.NodeOffset + parent.TriStartOrChild + 1];

        Box mergedBox = Box(left.Min, left.Max);
        BoxGrowToFit(mergedBox, Box(right.Min, right.Max));

        SetNodeBounds(blasDesc.NodeOffset + parentId, mergedBox);

        parentId = blasParentIndicesSSBO.Indices[blasDesc.NodeOffset + parentId];
    } while (parentId != -1);
}

void SetNodeBounds(uint index, Box bounds)
{
    blasNodeSSBO.Nodes[index].Min = bounds.Min;
    blasNodeSSBO.Nodes[index].Max = bounds.Max;
}

Box ComputeBoundingBox(uint start, uint count)
{
    Box box = CreateBoxEmpty();
    for (uint i = start; i < start + count; i++)
    {
        uvec3 indices = Unpack(blasTriangleIndicesSSBO.Indices[i]);

        vec3 p0 = Unpack(vertexPositionsSSBO.Positions[indices.x]);
        vec3 p1 = Unpack(vertexPositionsSSBO.Positions[indices.y]);
        vec3 p2 = Unpack(vertexPositionsSSBO.Positions[indices.z]);

        BoxGrowToFit(box, p0);
        BoxGrowToFit(box, p1);
        BoxGrowToFit(box, p2);
    }
    return box;
}
