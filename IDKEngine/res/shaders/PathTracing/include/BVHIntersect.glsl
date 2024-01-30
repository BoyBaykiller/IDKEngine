#ifndef PathTracing_BVHIntersect_H
#define PathTracing_BVHIntersect_H

// Needs more optimization.
// Currently only pays of on scenes with many BLAS'es.
// Also does not get rebuild automatically.
#define USE_TLAS 0

#define MAX_BLAS_TREE_DEPTH 22
#define MAX_TLAS_TREE_DEPTH 18 // TODO: Fix traversal occasionally using more than CPU side MAX_TLAS_TREE_DEPTH  

AppInclude(include/Ray.glsl)
AppInclude(include/IntersectionRoutines.glsl)

struct PackedUVec3 { uint x, y, z; };
uvec3 UintsToUVec3(PackedUVec3 uints)
{
    return uvec3(uints.x, uints.y, uints.z);
}

struct PackedVec3 { float x, y, z; };
vec3 FloatsToVec3(PackedVec3 floats)
{
    return vec3(floats.x, floats.y, floats.z);
}

layout(std430, binding = 6) restrict readonly buffer BlasTriangleIndicesSSBO
{
    PackedUVec3 Indices[];
} blasTriangleIndicesSSBO;

layout(std430, binding = 12) restrict readonly buffer VertexPositionsSSBO
{
    PackedVec3 VertexPositions[];
} vertexPositionsSSBO;


#ifdef TRAVERSAL_STACK_DONT_USE_SHARED_MEM
uint BlasTraversalStack[MAX_BLAS_TREE_DEPTH];
#else
shared uint BlasTraversalStack[gl_WorkGroupSize.x * gl_WorkGroupSize.y][MAX_BLAS_TREE_DEPTH];
#endif

struct HitInfo
{
    vec3 Bary;
    float T;
    uvec3 VertexIndices;
    uint MeshID;
    uint InstanceID;
};

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
    uint stackTopNode = 1;
    while (true)
    {
        debugNodeCounter++;
        BlasNode leftNode = blasSSBO.Nodes[blasRootNodeIndex + stackTopNode];
        BlasNode rightNode = blasSSBO.Nodes[blasRootNodeIndex + stackTopNode + 1];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight, tMax) && tMinRight <= hitInfo.T;

        uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
        if (triCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
            for (uint j = first; j < first + triCount; j++)
            {
                uvec3 indices = UintsToUVec3(blasTriangleIndicesSSBO.Indices[blasFirstTriangleIndex + j]);

                vec3 v0 = FloatsToVec3(vertexPositionsSSBO.VertexPositions[indices.x]);
                vec3 v1 = FloatsToVec3(vertexPositionsSSBO.VertexPositions[indices.y]);
                vec3 v2 = FloatsToVec3(vertexPositionsSSBO.VertexPositions[indices.z]);

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

            leftChildHit = leftChildHit && (leftNode.TriCount == 0);
            rightChildHit = rightChildHit && (rightNode.TriCount == 0);
        }

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                bool leftCloser = tMinLeft < tMinRight;
                stackTopNode = leftCloser ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                
                #ifdef TRAVERSAL_STACK_DONT_USE_SHARED_MEM
                BlasTraversalStack[stackPtr] = leftCloser ? rightNode.TriStartOrLeftChild : leftNode.TriStartOrLeftChild;
                #else
                BlasTraversalStack[gl_LocalInvocationIndex][stackPtr] = leftCloser ? rightNode.TriStartOrLeftChild : leftNode.TriStartOrLeftChild;
                #endif
                
                stackPtr++;
            }
            else
            {
                stackTopNode = leftChildHit ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackPtr--;

            #ifdef TRAVERSAL_STACK_DONT_USE_SHARED_MEM
            stackTopNode = BlasTraversalStack[stackPtr];
            #else
            stackTopNode = BlasTraversalStack[gl_LocalInvocationIndex][stackPtr];
            #endif
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
    uint stackTopNode = 1;
    while (true)
    {
        BlasNode leftNode = blasSSBO.Nodes[blasRootNodeIndex + stackTopNode];
        BlasNode rightNode = blasSSBO.Nodes[blasRootNodeIndex + stackTopNode + 1];

        bool leftChildHit = RayBoxIntersect(ray, Box(leftNode.Min, leftNode.Max), tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayBoxIntersect(ray, Box(rightNode.Min, rightNode.Max), tMinRight, tMax) && tMinRight <= hitInfo.T;

        uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
        if (triCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
            for (uint j = first; j < first + triCount; j++)
            {
                uvec3 indices = UintsToUVec3(blasTriangleIndicesSSBO.Indices[blasFirstTriangleIndex + j]);
                vec3 v0 = FloatsToVec3(vertexPositionsSSBO.VertexPositions[indices.x]);
                vec3 v1 = FloatsToVec3(vertexPositionsSSBO.VertexPositions[indices.y]);
                vec3 v2 = FloatsToVec3(vertexPositionsSSBO.VertexPositions[indices.z]);

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

            leftChildHit = leftChildHit && (leftNode.TriCount == 0);
            rightChildHit = rightChildHit && (rightNode.TriCount == 0);
        }

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                stackTopNode = leftNode.TriStartOrLeftChild;

                #ifdef TRAVERSAL_STACK_DONT_USE_SHARED_MEM
                BlasTraversalStack[stackPtr] = rightNode.TriStartOrLeftChild;
                #else
                BlasTraversalStack[gl_LocalInvocationIndex][stackPtr] = rightNode.TriStartOrLeftChild;
                #endif
                
                stackPtr++;
            }
            else
            {
                stackTopNode = leftChildHit ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackPtr--;

            #ifdef TRAVERSAL_STACK_DONT_USE_SHARED_MEM
            stackTopNode = BlasTraversalStack[stackPtr];
            #else
            stackTopNode = BlasTraversalStack[gl_LocalInvocationIndex][stackPtr];
            #endif
        }
    }

    return false;
}

bool BVHRayTrace(Ray ray, out HitInfo hitInfo, out uint debugNodeCounter, bool traceLights, float maxDist)
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
                hitInfo.T = tMin;
                hitInfo.MeshID = i;
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

        bool isLeaf = parent.IsLeafAndLeftChildOrInstanceID >> 31 == 1;
        uint leftChildOrInstanceID = parent.IsLeafAndLeftChildOrInstanceID & ((1u << 31) - 1);
        if (isLeaf)
        {
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[parent.BlasIndex];
            Mesh mesh = meshSSBO.Meshes[parent.BlasIndex];

            mat4 invModelMatrix = mat4(meshInstanceSSBO.MeshInstances[leftChildOrInstanceID].InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);

            if (IntersectBlas(localRay, mesh.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
            {
                hitInfo.MeshID = parent.BlasIndex;
                hitInfo.InstanceID = leftChildOrInstanceID;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = leftChildOrInstanceID;
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
                if (stackPtr >= MAX_TLAS_TREE_DEPTH) { break; }
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
                hitInfo.MeshID = i;
                hitInfo.InstanceID = instanceID;
            }
        }
    }
    #endif

    return hitInfo.T != maxDist;
}

bool BVHRayTrace(Ray ray, out HitInfo hitInfo, bool traceLights, float maxDist)
{
    uint debugNodeCounter;
    return BVHRayTrace(ray, hitInfo, debugNodeCounter, traceLights, maxDist);
}

bool BVHRayTraceAny(Ray ray, out HitInfo hitInfo, bool traceLights, float maxDist)
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
                hitInfo.T = tMin;
                hitInfo.MeshID = i;

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

        bool isLeaf = parent.IsLeafAndLeftChildOrInstanceID >> 31 == 1;
        uint leftChildOrInstanceID = parent.IsLeafAndLeftChildOrInstanceID & ((1u << 31) - 1);
        if (isLeaf)
        {
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[parent.BlasIndex];
            Mesh mesh = meshSSBO.Meshes[parent.BlasIndex];

            mat4 invModelMatrix = mat4(meshInstanceSSBO.MeshInstances[leftChildOrInstanceID].InvModelMatrix);
            Ray localRay = RayTransform(ray, invModelMatrix);

            if (IntersectBlasAny(localRay, mesh.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo))
            {
                hitInfo.MeshID = parent.BlasIndex;
                hitInfo.InstanceID = leftChildOrInstanceID;

                return true;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = leftChildOrInstanceID;
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
                if (stackPtr >= MAX_TLAS_TREE_DEPTH) { break; }
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
                hitInfo.MeshID = i;
                hitInfo.InstanceID = instanceID;
                return true;
            }
        }
    }
    #endif

    return false;
}


#endif