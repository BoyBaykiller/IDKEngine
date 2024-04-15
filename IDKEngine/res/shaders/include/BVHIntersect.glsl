#define USE_TLAS AppInsert(USE_TLAS)

#define MAX_BLAS_TREE_DEPTH AppInsert(MAX_BLAS_TREE_DEPTH) - 1
#define MAX_TLAS_TREE_DEPTH 20

#define DECLARE_BVH_TRAVERSAL_STORAGE_BUFFERS
AppInclude(include/StaticStorageBuffers.glsl)

AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

struct HitInfo
{
    vec3 Bary;
    float T;
    uvec3 VertexIndices;
    uint InstanceID;
};

#ifdef TRAVERSAL_STACK_USE_SHARED_STACK_SIZE
shared uint BlasTraversalStack[TRAVERSAL_STACK_USE_SHARED_STACK_SIZE][MAX_BLAS_TREE_DEPTH];
#else
uint BlasTraversalStack[MAX_BLAS_TREE_DEPTH];
#endif

void StackPush(inout uint stackPtr, uint newEntry);
uint StackPop(inout uint stackPtr);

bool IntersectBlas(Ray ray, uint blasRootNodeIndex, uint blasFirstTriangleIndex, inout HitInfo hitInfo, inout uint debugNodeCounter)
{
    bool hit = false;
    float tMinLeft;
    float tMinRight;
    float tMax;
    
    #if !USE_TLAS
    BlasNode rootNode = blasSSBO.Nodes[blasRootNodeIndex];
    if (!(RayBoxIntersect(ray, Box(rootNode.Min, rootNode.Max), tMinLeft, tMax) && tMinLeft < hitInfo.T))
    {
        return false;
    }
    #endif

    uint stackPtr = 0;
    uint stackTop = 1;
    while (true)
    {
        debugNodeCounter++;
        BlasNode leftNode = blasSSBO.Nodes[blasRootNodeIndex + stackTop];
        BlasNode rightNode = blasSSBO.Nodes[blasRootNodeIndex + stackTop + 1];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight, tMax) && tMinRight <= hitInfo.T;

        uint summedTriCount = int(leftChildHit) * leftNode.TriCount + int(rightChildHit) * rightNode.TriCount;
        if (summedTriCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
            for (uint j = first; j < first + summedTriCount; j++)
            {
                uvec3 indices = Unpack(blasTriangleIndicesSSBO.Indices[blasFirstTriangleIndex + j]);

                vec3 v0 = Unpack(vertexPositionsSSBO.VertexPositions[indices.x]);
                vec3 v1 = Unpack(vertexPositionsSSBO.VertexPositions[indices.y]);
                vec3 v2 = Unpack(vertexPositionsSSBO.VertexPositions[indices.z]);

                vec3 bary;
                float hitT;
                if (RayTriangleIntersect(ray, v0, v1, v2, bary, hitT) && hitT < hitInfo.T)
                {
                    hit = true;
                    hitInfo.VertexIndices = indices;
                    hitInfo.Bary = bary;
                    hitInfo.T = hitT;
                }
            }
            
            if (leftNode.TriCount > 0) leftChildHit = false;
            if (rightNode.TriCount > 0) rightChildHit = false;
        }

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                bool leftCloser = tMinLeft < tMinRight;
                stackTop = leftCloser ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                
                StackPush(stackPtr, leftCloser ? rightNode.TriStartOrChild : leftNode.TriStartOrChild);
            }
            else
            {
                stackTop = leftChildHit ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackTop = StackPop(stackPtr);
        }
    }

    return hit;
}

bool IntersectBlasAny(Ray ray, uint blasRootNodeIndex, uint blasFirstTriangleIndex, inout HitInfo hitInfo)
{
    float tMinLeft;
    float tMinRight;
    float tMax;
    
    #if !USE_TLAS
    BlasNode rootNode = blasSSBO.Nodes[blasRootNodeIndex];
    if (!(RayBoxIntersect(ray, Box(rootNode.Min, rootNode.Max), tMinLeft, tMax) && tMinLeft < hitInfo.T))
    {
        return false;
    }
    #endif

    uint stackPtr = 0;
    uint stackTop = 1;
    while (true)
    {
        BlasNode leftNode = blasSSBO.Nodes[blasRootNodeIndex + stackTop];
        BlasNode rightNode = blasSSBO.Nodes[blasRootNodeIndex + stackTop + 1];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight, tMax) && tMinRight <= hitInfo.T;

        uint summedTriCount = int(leftChildHit) * leftNode.TriCount + int(rightChildHit) * rightNode.TriCount;
        if (summedTriCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
            for (uint j = first; j < first + summedTriCount; j++)
            {
                uvec3 indices = Unpack(blasTriangleIndicesSSBO.Indices[blasFirstTriangleIndex + j]);
                vec3 v0 = Unpack(vertexPositionsSSBO.VertexPositions[indices.x]);
                vec3 v1 = Unpack(vertexPositionsSSBO.VertexPositions[indices.y]);
                vec3 v2 = Unpack(vertexPositionsSSBO.VertexPositions[indices.z]);

                vec3 bary;
                float hitT;
                if (RayTriangleIntersect(ray, v0, v1, v2, bary, hitT) && hitT < hitInfo.T)
                {
                    hitInfo.VertexIndices = indices;
                    hitInfo.Bary = bary;
                    hitInfo.T = hitT;

                    return true;
                }
            }

            if (leftNode.TriCount > 0) leftChildHit = false;
            if (rightNode.TriCount > 0) rightChildHit = false;
        }

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                stackTop = leftNode.TriStartOrChild;

                StackPush(stackPtr, rightNode.TriStartOrChild);
            }
            else
            {
                stackTop = leftChildHit ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackTop = StackPop(stackPtr);
        }
    }

    return false;
}

bool TraceRay(Ray ray, out HitInfo hitInfo, out uint debugNodeCounter, bool traceLights, float maxDist)
{
    hitInfo.T = maxDist;
    hitInfo.VertexIndices = uvec3(0);
    debugNodeCounter = 0;

    if (traceLights)
    {
        float tMin;
        float tMax;
        for (int i = 0; i < lightsUBO.Count; i++)
        {
            Light light = lightsUBO.Lights[i];
            if (RaySphereIntersect(ray, light.Position, light.Radius, tMin, tMax) && tMin < hitInfo.T)
            {
                hitInfo.T = tMin < 0.0 ? tMax : tMin;
                hitInfo.InstanceID = i;
            }
        }
    }


    #if USE_TLAS
    float tMinLeft;
    float tMinRight;
    float tMax;

    uint stackPtr = 0;
    uint stackTop = 0;
    uint stack[MAX_TLAS_TREE_DEPTH];
    while (true)
    {
        TlasNode parent = tlasSSBO.Nodes[stackTop];
        // float tMin;
        // if (!(RayBoxIntersect(ray, Box(parent.Min, parent.Max), tMin, tMax) && tMin <= hitInfo.T))
        // {
        //     if (stackPtr == 0) break;
        //     stackTop = stack[--stackPtr];
        //     continue;
        // }

        bool isLeaf = parent.IsLeafAndChildOrInstanceID >> 31 == 1;
        uint childOrInstanceID = parent.IsLeafAndChildOrInstanceID & ((1u << 31) - 1);
        if (isLeaf)
        {
            MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[childOrInstanceID];
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[meshInstance.MeshIndex];
            Mesh mesh = meshSSBO.Meshes[meshInstance.MeshIndex];

            mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);

            if (IntersectBlas(localRay, mesh.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
            {
                hitInfo.InstanceID = childOrInstanceID;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = childOrInstanceID;
        uint rightChild = leftChild + 1;
        TlasNode leftNode = tlasSSBO.Nodes[leftChild];
        TlasNode rightNode = tlasSSBO.Nodes[rightChild];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight, tMax) && tMinRight <= hitInfo.T;

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                bool leftCloser = tMinLeft < tMinRight;
                stackTop = leftCloser ? leftChild : rightChild;
                stack[stackPtr++] = leftCloser ? rightChild : leftChild;
            }
            else
            {
                stackTop = leftChildHit ? leftChild : rightChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
        }
    }
    #else
    for (uint i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[i];
        Mesh mesh = meshSSBO.Meshes[i];
        for (uint j = 0; j < mesh.InstanceCount; j++)
        {
            uint instanceID = cmd.BaseInstance + j;
            mat4 invModelMatrix = mat4(meshInstanceSSBO.MeshInstances[instanceID].InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);
            if (IntersectBlas(localRay, mesh.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
            {
                hitInfo.InstanceID = instanceID;
            }
        }
    }
    #endif

    return hitInfo.T != maxDist;
}

bool TraceRay(Ray ray, out HitInfo hitInfo, bool traceLights, float maxDist)
{
    uint debugNodeCounter;
    return TraceRay(ray, hitInfo, debugNodeCounter, traceLights, maxDist);
}

bool TraceRayAny(Ray ray, out HitInfo hitInfo, bool traceLights, float maxDist)
{
    hitInfo.T = maxDist;
    hitInfo.VertexIndices = uvec3(0);

    if (traceLights)
    {
        float tMin;
        float tMax;
        for (int i = 0; i < lightsUBO.Count; i++)
        {
            Light light = lightsUBO.Lights[i];
            if (RaySphereIntersect(ray, light.Position, light.Radius, tMin, tMax) && tMin < hitInfo.T)
            {
                hitInfo.T = tMin < 0.0 ? tMax : tMin;
                hitInfo.InstanceID = i;

                return true;
            }
        }
    }


    #if USE_TLAS
    float tMinLeft;
    float tMinRight;
    float tMax;

    uint stackPtr = 0;
    uint stackTop = 0;
    uint stack[MAX_TLAS_TREE_DEPTH];
    while (true)
    {
        TlasNode parent = tlasSSBO.Nodes[stackTop];
        // float tMin;
        // if (!(RayBoxIntersect(ray, Box(parent.Min, parent.Max), tMin, tMax) && tMin <= hitInfo.T))
        // {
        //     if (stackPtr == 0) break;
        //     stackTop = stack[--stackPtr];
        //     continue;
        // }

        bool isLeaf = parent.IsLeafAndChildOrInstanceID >> 31 == 1;
        uint childOrInstanceID = parent.IsLeafAndChildOrInstanceID & ((1u << 31) - 1);
        if (isLeaf)
        {
            MeshInstance meshInstance = meshInstanceSSBO.MeshInstances[childOrInstanceID];
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[meshInstance.MeshIndex];
            Mesh mesh = meshSSBO.Meshes[meshInstance.MeshIndex];

            mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);

            if (IntersectBlasAny(localRay, mesh.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo))
            {
                hitInfo.InstanceID = childOrInstanceID;

                return true;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = childOrInstanceID;
        uint rightChild = leftChild + 1;
        TlasNode leftNode = tlasSSBO.Nodes[leftChild];
        TlasNode rightNode = tlasSSBO.Nodes[rightChild];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight, tMax) && tMinRight <= hitInfo.T;

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                bool leftCloser = tMinLeft < tMinRight;
                stackTop = leftCloser ? leftChild : rightChild;
                stack[stackPtr++] = leftCloser ? rightChild : leftChild;
            }
            else
            {
                stackTop = leftChildHit ? leftChild : rightChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
        }
    }
    #else

    for (uint i = 0; i < meshSSBO.Meshes.length(); i++)
    {
        DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[i];
        Mesh mesh = meshSSBO.Meshes[i];
        for (uint j = 0; j < mesh.InstanceCount; j++)
        {
            uint instanceID = cmd.BaseInstance + j;
            mat4 invModelMatrix = mat4(meshInstanceSSBO.MeshInstances[instanceID].InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);
            if (IntersectBlasAny(localRay, mesh.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo))
            {
                hitInfo.InstanceID = instanceID;
                return true;
            }
        }
    }
    #endif

    return false;
}

void StackPush(inout uint stackPtr, uint newEntry)
{
#ifdef TRAVERSAL_STACK_USE_SHARED_STACK_SIZE
    BlasTraversalStack[gl_LocalInvocationIndex][stackPtr++] = newEntry;
#else
    BlasTraversalStack[stackPtr++] = newEntry;
#endif
}


uint StackPop(inout uint stackPtr)
{
#ifdef TRAVERSAL_STACK_USE_SHARED_STACK_SIZE
    return BlasTraversalStack[gl_LocalInvocationIndex][--stackPtr];
#else
    return BlasTraversalStack[--stackPtr];
#endif
}
