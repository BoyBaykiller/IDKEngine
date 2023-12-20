#ifndef PathTracing_BVHIntersect_H
#define PathTracing_BVHIntersect_H

// Needs more optimization.
// Currently only pays of on scenes with many BLAS'es.
// Also does not get rebuild automatically.
#define USE_TLAS 0

#define MAX_BLAS_TREE_DEPTH 18
#define MAX_TLAS_TREE_DEPTH 10 // TODO: Fix traversal occasionally using more than CPU side MAX_TLAS_TREE_DEPTH  

AppInclude(include/Ray.glsl)
AppInclude(include/IntersectionRoutines.glsl)

// Minus one because we store the stack top in a seperate local variable instead of shared
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
                vec3 bary;
                float hitT;
                uint triIndex = blasFirstTriangleIndex + j;
                BlasTriangle triangle = blasTriangleSSBO.Triangles[triIndex];
                if (RayTriangleIntersect(ray, triangle.Position0, triangle.Position1, triangle.Position2, bary, hitT) && hitT < hitInfo.T)
                {
                    hit = true;
                    hitInfo.VertexIndices = uvec3(triangle.VertexIndex0, triangle.VertexIndex1, triangle.VertexIndex2);
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
                vec3 bary;
                float hitT;
                uint triIndex = blasFirstTriangleIndex + j;
                BlasTriangle triangle = blasTriangleSSBO.Triangles[triIndex];
                if (RayTriangleIntersect(ray, triangle.Position0, triangle.Position1, triangle.Position2, bary, hitT) && hitT < hitInfo.T)
                {
                    hitInfo.VertexIndices = uvec3(triangle.VertexIndex0, triangle.VertexIndex1, triangle.VertexIndex2);
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

        if (parent.LeftChild == 0)
        {
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[parent.BlasIndex];

            uint glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
            Ray localRay = RayTransform(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);

            if (IntersectBlas(localRay, cmd.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
            {
                hitInfo.VertexIndices += cmd.BaseVertex;
                hitInfo.MeshID = parent.BlasIndex;
                hitInfo.InstanceID = glInstanceID;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = parent.LeftChild;
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
        
        uint glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
        Ray localRay = RayTransform(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);
        if (IntersectBlas(localRay, cmd.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo, debugNodeCounter))
        {
            hitInfo.VertexIndices += cmd.BaseVertex;
            hitInfo.MeshID = i;
            hitInfo.InstanceID = glInstanceID;
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

        if (parent.LeftChild == 0)
        {
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[parent.BlasIndex];

            uint glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
            Ray localRay = RayTransform(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);

            if (IntersectBlasAny(localRay, cmd.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo))
            {
                hitInfo.VertexIndices += cmd.BaseVertex;
                hitInfo.MeshID = parent.BlasIndex;
                hitInfo.InstanceID = glInstanceID;

                return true;
            }

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = parent.LeftChild;
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
        
        uint glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
        Ray localRay = RayTransform(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);
        if (IntersectBlasAny(localRay, cmd.BlasRootNodeIndex, cmd.FirstIndex / 3, hitInfo))
        {
            hitInfo.VertexIndices += cmd.BaseVertex;
            hitInfo.MeshID = i;
            hitInfo.InstanceID = glInstanceID;
            return true;
        }
    }
    #endif

    return false;
}


#endif