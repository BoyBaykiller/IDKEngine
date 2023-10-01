#ifndef PathTracing_ClosestHit_H
#define PathTracing_ClosestHit_H

// Needs more optimization.
// Currently only pays of on scenes with many BLAS'es.
// Also does not get rebuild automatically.
#define USE_TLAS 0

// Positive integer expression
#define MAX_BLAS_TREE_DEPTH AppInsert(MAX_BLAS_TREE_DEPTH)
#define MAX_TLAS_TREE_DEPTH AppInsert(MAX_TLAS_TREE_DEPTH)

AppInclude(include/IntersectionRoutines.glsl)

shared uint ClosestHit_SharedStack[gl_WorkGroupSize.x * gl_WorkGroupSize.y][MAX_BLAS_TREE_DEPTH];

struct HitInfo
{
    vec3 Bary;
    float T;
    int TriangleIndex;
    uint MeshID;
    uint InstanceID;
};

bool IntersectBlas(Ray ray, uint firstIndex, uint blasRootNodeIndex, inout HitInfo hitInfo, inout uint debugNodeCounter)
{
    uint baseTriangle = firstIndex / 3;

    bool hit = false;
    float tMinLeft;
    float tMinRight;
    float tMax;
    
#if !USE_TLAS
    BlasNode rootNode = blasSSBO.Nodes[blasRootNodeIndex];
    if (!(RayCuboidIntersect(ray, rootNode.Min, rootNode.Max, tMinLeft, tMax) && tMinLeft < hitInfo.T))
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

        bool leftChildHit = RayCuboidIntersect(ray, leftNode.Min, leftNode.Max, tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayCuboidIntersect(ray, rightNode.Min, rightNode.Max, tMinRight, tMax) && tMinRight <= hitInfo.T;

        uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
        if (triCount > 0)
        {
            uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
            for (uint j = first; j < first + triCount; j++)
            {
                vec3 bary;
                float hitT;
                int triIndex = int(baseTriangle + j);
                Triangle triangle = blasTriangleSSBO.Triangles[triIndex];
                if (RayTriangleIntersect(ray, triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position, bary, hitT) && hitT < hitInfo.T)
                {
                    hit = true;
                    hitInfo.TriangleIndex = triIndex;
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
                stackTop = leftCloser ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                ClosestHit_SharedStack[gl_LocalInvocationIndex][stackPtr++] = leftCloser ? rightNode.TriStartOrLeftChild : leftNode.TriStartOrLeftChild;
            }
            else
            {
                stackTop = leftChildHit ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackTop = ClosestHit_SharedStack[gl_LocalInvocationIndex][--stackPtr];
        }
    }

    return hit;
}

bool ClosestHit(Ray ray, out HitInfo hitInfo, out uint debugNodeCounter)
{
    hitInfo.T = IntersectionRoutines_NotHit;
    hitInfo.TriangleIndex = -1;
    debugNodeCounter = 0;

    if (IsTraceLights)
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
    // uint stack;
    while (true)
    {
        TlasNode parent = tlasSSBO.Nodes[stackTop];
        // float tMin;
        // if (!(RayCuboidIntersect(ray, parent.Min, parent.Max, tMin, tMax) && tMin <= hitInfo.T))
        // {
        //     if (stackPtr == 0) break;
        //     stackTop = stack[--stackPtr];
        //     continue;
        // }

        if (parent.LeftChild == 0)
        {
            DrawElementsCmd cmd = drawElementsCmdSSBO.DrawCommands[parent.BlasIndex];

            uint glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
            Ray localRay = WorldSpaceRayToLocal(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);

            if (IntersectBlas(localRay, cmd.FirstIndex, cmd.BlasRootNodeIndex, hitInfo, debugNodeCounter))
            {
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

        bool leftChildHit = RayCuboidIntersect(ray, leftNode.Min, leftNode.Max, tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayCuboidIntersect(ray, rightNode.Min, rightNode.Max, tMinRight, tMax) && tMinRight <= hitInfo.T;

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                bool leftCloser = tMinLeft < tMinRight;
                stackTop = leftCloser ? leftChild : rightChild;
                // TODO: Fix traversal occasionally using more than MAX_TLAS_TREE_DEPTH 
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
        Ray localRay = WorldSpaceRayToLocal(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);
        if (IntersectBlas(localRay, cmd.FirstIndex, cmd.BlasRootNodeIndex, hitInfo, debugNodeCounter))
        {
            hitInfo.MeshID = i;
            hitInfo.InstanceID = glInstanceID;
        }
    }
#endif

    return hitInfo.T != IntersectionRoutines_NotHit;
}

bool ClosestHit(Ray ray, out HitInfo hitInfo)
{
    uint debugNodeCounter;
    return ClosestHit(ray, hitInfo, debugNodeCounter);
}

#endif