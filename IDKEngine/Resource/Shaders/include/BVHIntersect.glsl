#define USE_TLAS AppInsert(USE_TLAS)

#define MAX_BLAS_TREE_DEPTH max(AppInsert(MAX_BLAS_TREE_DEPTH) - 1, 1) // -1 because we skip root
#define MAX_TLAS_TREE_DEPTH max(24, 1)

AppInclude(include/StaticStorageBuffers.glsl)
AppInclude(include/IntersectionRoutines.glsl)
AppInclude(include/StaticUniformBuffers.glsl)

struct HitInfo
{
    vec2 BaryXY; // z = 1.0 - bary.x - bary.y
    float T;
    uint TriangleId;
    uint InstanceId;
};

#ifdef TRAVERSAL_STACK_USE_SHARED_STACK_SIZE
shared uint BlasTraversalStack[TRAVERSAL_STACK_USE_SHARED_STACK_SIZE][MAX_BLAS_TREE_DEPTH];
#else
uint BlasTraversalStack[MAX_BLAS_TREE_DEPTH];
#endif

void StackPush(inout uint stackPtr, uint newEntry);
uint StackPop(inout uint stackPtr);

bool IntersectBlas(Ray ray, uint rootNodeOffset, uint triangleOffset, inout HitInfo hitInfo, inout uint debugNodeCounter)
{
    bool hit = false;
    float tMinLeft;
    float tMinRight;
    
    #if !USE_TLAS
    GpuBlasNode rootNode = blasNodeSSBO.Nodes[rootNodeOffset];
    if (!(RayBoxIntersect(ray, Box(rootNode.Min, rootNode.Max), tMinLeft) && tMinLeft < hitInfo.T))
    {
        return false;
    }
    #endif

    uint stackPtr = 0;
    uint stackTop = 1;
    while (true)
    {
        debugNodeCounter++;
        GpuBlasNode leftNode = blasNodeSSBO.Nodes[rootNodeOffset + stackTop];
        GpuBlasNode rightNode = blasNodeSSBO.Nodes[rootNodeOffset + stackTop + 1];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight) && tMinRight <= hitInfo.T;

        uint summedTriCount = int(leftChildHit) * leftNode.TriCount + int(rightChildHit) * rightNode.TriCount;
        if (summedTriCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
            first += triangleOffset;
            for (uint i = first; i < first + summedTriCount; i++)
            {
                uvec3 indices = Unpack(blasTriangleIndicesSSBO.Indices[i]);

                vec3 p0 = Unpack(vertexPositionsSSBO.Positions[indices.x]);
                vec3 p1 = Unpack(vertexPositionsSSBO.Positions[indices.y]);
                vec3 p2 = Unpack(vertexPositionsSSBO.Positions[indices.z]);

                vec3 bary;
                float hitT;
                if (RayTriangleIntersect(ray, p0, p1, p2, bary, hitT) && hitT < hitInfo.T)
                {
                    hit = true;
                    hitInfo.TriangleId = i;
                    hitInfo.BaryXY = bary.xy;
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

bool IntersectBlasAny(Ray ray, uint rootNodeOffset, uint triangleOffset, inout HitInfo hitInfo)
{
    float tMinLeft;
    float tMinRight;
    
    #if !USE_TLAS
    GpuBlasNode rootNode = blasNodeSSBO.Nodes[rootNodeOffset];
    if (!(RayBoxIntersect(ray, Box(rootNode.Min, rootNode.Max), tMinLeft) && tMinLeft < hitInfo.T))
    {
        return false;
    }
    #endif

    uint stackPtr = 0;
    uint stackTop = 1;
    while (true)
    {
        GpuBlasNode leftNode = blasNodeSSBO.Nodes[rootNodeOffset + stackTop];
        GpuBlasNode rightNode = blasNodeSSBO.Nodes[rootNodeOffset + stackTop + 1];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight) && tMinRight <= hitInfo.T;

        uint summedTriCount = int(leftChildHit) * leftNode.TriCount + int(rightChildHit) * rightNode.TriCount;
        if (summedTriCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
            first += triangleOffset;
            for (uint i = first; i < first + summedTriCount; i++)
            {
                uvec3 indices = Unpack(blasTriangleIndicesSSBO.Indices[i]);
                vec3 p0 = Unpack(vertexPositionsSSBO.Positions[indices.x]);
                vec3 p1 = Unpack(vertexPositionsSSBO.Positions[indices.y]);
                vec3 p2 = Unpack(vertexPositionsSSBO.Positions[indices.z]);

                vec3 bary;
                float hitT;
                if (RayTriangleIntersect(ray, p0, p1, p2, bary, hitT) && hitT < hitInfo.T)
                {
                    hitInfo.TriangleId = i;
                    hitInfo.BaryXY = bary.xy;
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
    hitInfo.TriangleId = ~0u;
    debugNodeCounter = 0;

    if (traceLights)
    {
        float tMin;
        float tMax;
        for (int i = 0; i < lightsUBO.Count; i++)
        {
            GpuLight light = lightsUBO.Lights[i];
            if (RaySphereIntersect(ray, light.Position, light.Radius, tMin, tMax) && tMin < hitInfo.T)
            {
                hitInfo.T = tMin < 0.0 ? tMax : tMin;
                hitInfo.InstanceId = i;
            }
        }
    }

    #if USE_TLAS
    float tMinLeft;
    float tMinRight;

    uint stackPtr = 0;
    uint stackTop = 0;
    uint stack[MAX_TLAS_TREE_DEPTH];
    while (true)
    {
        GpuTlasNode parent = tlasSSBO.Nodes[stackTop];
        // float tMin;
        // if (!(RayBoxIntersect(ray, Box(parent.Min, parent.Max), tMin) && tMin <= hitInfo.T))
        // {
        //     if (stackPtr == 0) break;
        //     stackTop = stack[--stackPtr];
        //     continue;
        // }

        bool isLeaf = parent.IsLeafAndChildOrInstanceId >> 31 == 1;
        uint childOrInstanceId = parent.IsLeafAndChildOrInstanceId & ((1u << 31) - 1);
        if (isLeaf)
        {
            GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[childOrInstanceId];
            GpuDrawElementsCmd cmd = drawElementsCmdSSBO.Commands[meshInstance.MeshId];
            GpuMesh mesh = meshSSBO.Meshes[meshInstance.MeshId];

            mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);

            if (IntersectBlas(localRay, mesh.BlasRootNodeOffset, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
            {
                hitInfo.InstanceId = childOrInstanceId;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = childOrInstanceId;
        uint rightChild = leftChild + 1;
        GpuTlasNode leftNode = tlasSSBO.Nodes[leftChild];
        GpuTlasNode rightNode = tlasSSBO.Nodes[rightChild];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight) && tMinRight <= hitInfo.T;

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

    for (uint i = 0; i < meshInstanceSSBO.MeshInstances.length(); i++)
    {
        GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[i];
        GpuDrawElementsCmd cmd = drawElementsCmdSSBO.Commands[meshInstance.MeshId];
        GpuMesh mesh = meshSSBO.Meshes[meshInstance.MeshId];

        mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
        Ray localRay = RayTransform(ray, invModelMatrix);
        if (IntersectBlas(localRay, mesh.BlasRootNodeOffset, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
        {
            hitInfo.InstanceId = i;
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
    hitInfo.TriangleId = ~0u;

    if (traceLights)
    {
        float tMin;
        float tMax;
        for (int i = 0; i < lightsUBO.Count; i++)
        {
            GpuLight light = lightsUBO.Lights[i];
            if (RaySphereIntersect(ray, light.Position, light.Radius, tMin, tMax) && tMin < hitInfo.T)
            {
                hitInfo.T = tMin < 0.0 ? tMax : tMin;
                hitInfo.InstanceId = i;

                return true;
            }
        }
    }


    #if USE_TLAS
    float tMinLeft;
    float tMinRight;

    uint stackPtr = 0;
    uint stackTop = 0;
    uint stack[MAX_TLAS_TREE_DEPTH];
    while (true)
    {
        GpuTlasNode parent = tlasSSBO.Nodes[stackTop];
        // float tMin;
        // if (!(RayBoxIntersect(ray, Box(parent.Min, parent.Max), tMin) && tMin <= hitInfo.T))
        // {
        //     if (stackPtr == 0) break;
        //     stackTop = stack[--stackPtr];
        //     continue;
        // }

        bool isLeaf = parent.IsLeafAndChildOrInstanceId >> 31 == 1;
        uint childOrInstanceId = parent.IsLeafAndChildOrInstanceId & ((1u << 31) - 1);
        if (isLeaf)
        {
            GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[childOrInstanceId];
            GpuDrawElementsCmd cmd = drawElementsCmdSSBO.Commands[meshInstance.MeshId];
            GpuMesh mesh = meshSSBO.Meshes[meshInstance.MeshId];

            mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);

            if (IntersectBlasAny(localRay, mesh.BlasRootNodeOffset, cmd.FirstIndex / 3, hitInfo))
            {
                hitInfo.InstanceId = childOrInstanceId;

                return true;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = childOrInstanceId;
        uint rightChild = leftChild + 1;
        GpuTlasNode leftNode = tlasSSBO.Nodes[leftChild];
        GpuTlasNode rightNode = tlasSSBO.Nodes[rightChild];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight) && tMinRight <= hitInfo.T;

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

    for (uint i = 0; i < meshInstanceSSBO.MeshInstances.length(); i++)
    {
        GpuMeshInstance meshInstance = meshInstanceSSBO.MeshInstances[i];
        GpuDrawElementsCmd cmd = drawElementsCmdSSBO.Commands[meshInstance.MeshId];
        GpuMesh mesh = meshSSBO.Meshes[meshInstance.MeshId];

        mat4 invModelMatrix = mat4(meshInstance.InvModelMatrix);
        Ray localRay = RayTransform(ray, invModelMatrix);
        if (IntersectBlasAny(localRay, mesh.BlasRootNodeOffset, cmd.FirstIndex / 3, hitInfo))
        {
            hitInfo.InstanceId = i;
            return true;
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
