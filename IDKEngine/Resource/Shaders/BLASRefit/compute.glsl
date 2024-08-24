#version 460 core

AppInclude(include/Constants.glsl)
AppInclude(include/Box.glsl)
AppInclude(include/StaticStorageBuffers.glsl)

layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

void SetNodeBounds(uint index, Box bounds);
Box ComputeBoundingBox(uint start, uint count);

layout(location = 0) uniform uint RootNodeOffset;
layout(location = 1) uniform uint TriangleOffset;
layout(location = 2) uniform uint LeafIndicesOffset;
layout(location = 3) uniform uint LeafIndicesCount;


void main()
{
    if (gl_GlobalInvocationID.x >= LeafIndicesCount)
    {
        return;
    }
    
    uint leafId = blasLeafIndicesSSBO.Indices[LeafIndicesOffset + gl_GlobalInvocationID.x];

    GpuBlasNode leafNode = blasNodeSSBO.Nodes[RootNodeOffset + leafId];
    Box bounds = ComputeBoundingBox(TriangleOffset + leafNode.TriStartOrChild, leafNode.TriCount);
    leafNode.Min = bounds.Min;
    leafNode.Max = bounds.Max;

    blasNodeSSBO.Nodes[RootNodeOffset + leafId] = leafNode;

    // Writing only to Min & Max fields here (with SetNodeBounds(RootNodeOffset + leafId, bounds)) 
    // makes this shader run much slower for some reason
    blasNodesHostSSBO.Nodes[RootNodeOffset + leafId] = leafNode;

    int parentId = blasParentIndicesSSBO.Indices[RootNodeOffset + leafId];
    do
    {
        if (atomicExchange(blasRefitLocksSSBO.Locks[parentId], 1u) == 0u)
        {
            // Thread arrived first, meaning the other child is not refitted yet, terminate.
            return;
        }

        GpuBlasNode parent = blasNodeSSBO.Nodes[RootNodeOffset + parentId];
        GpuBlasNode left = blasNodeSSBO.Nodes[RootNodeOffset + parent.TriStartOrChild];
        GpuBlasNode right = blasNodeSSBO.Nodes[RootNodeOffset + parent.TriStartOrChild + 1];

        Box mergedBox = Box(left.Min, left.Max);
        BoxGrowToFit(mergedBox, Box(right.Min, right.Max));

        SetNodeBounds(RootNodeOffset + parentId, mergedBox);

        parentId = blasParentIndicesSSBO.Indices[RootNodeOffset + parentId];
    } while (parentId != -1);
}

void SetNodeBounds(uint index, Box bounds)
{
    blasNodeSSBO.Nodes[index].Min = bounds.Min;
    blasNodeSSBO.Nodes[index].Max = bounds.Max;

    // Also copy to CPU
    blasNodesHostSSBO.Nodes[index].Min = bounds.Max;
    blasNodesHostSSBO.Nodes[index].Max = bounds.Min;
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
