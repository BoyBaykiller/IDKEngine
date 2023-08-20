#ifndef PathTracing_ClosestHit_H
#define PathTracing_ClosestHit_H

#define USE_TLAS 0 // needs more optimization, for now only pays of on scenes with many blases

// Positive integer expression
#define MAX_BLAS_TREE_DEPTH AppInsert(MAX_BLAS_TREE_DEPTH)

AppInclude(include/IntersectionRoutines.glsl)

shared uint ClosestHit_SharedStack[gl_WorkGroupSize.x * gl_WorkGroupSize.y][MAX_BLAS_TREE_DEPTH];

struct BlasHitInfo
{
    uint TriangleIndex;
    vec3 Bary;
    float T;
};
bool IntersectBlas(Ray ray, uint firstIndex, float tMaxDist, out BlasHitInfo hitInfo, out uint debugNodeCounter)
{
    debugNodeCounter = 0;
    uint baseTriangle = firstIndex / 3;
    uint baseNode = 2 * baseTriangle;

    hitInfo.T = tMaxDist;

    float tMinLeft;
    float tMinRight;
    float tMax;
    
#if !USE_TLAS
    BlasNode rootNode = blasSSBO.Nodes[baseNode];
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
        BlasNode left = blasSSBO.Nodes[baseNode + stackTop];
        BlasNode right = blasSSBO.Nodes[baseNode + stackTop + 1];

        bool leftChildHit = RayCuboidIntersect(ray, left.Min, left.Max, tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayCuboidIntersect(ray, right.Min, right.Max, tMinRight, tMax) && tMinRight <= hitInfo.T;

        uint triCount = (leftChildHit ? left.TriCount : 0) + (rightChildHit ? right.TriCount : 0);
        if (triCount > 0)
        {
            uint first = (leftChildHit && (left.TriCount > 0)) ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
            for (uint j = first; j < first + triCount; j++)
            {
                vec3 bary;
                float hitT;
                int triIndex = int(baseTriangle + j);
                Triangle triangle = blasTriangleSSBO.Triangles[triIndex];
                if (RayTriangleIntersect(ray, triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position, bary, hitT) && hitT < hitInfo.T)
                {
                    hitInfo.TriangleIndex = triIndex;
                    hitInfo.Bary = bary;
                    hitInfo.T = hitT;
                }
            }

            leftChildHit = leftChildHit && (left.TriCount == 0);
            rightChildHit = rightChildHit && (right.TriCount == 0);
        }

        if (leftChildHit || rightChildHit)
        {
            if (leftChildHit && rightChildHit)
            {
                bool leftCloser = tMinLeft < tMinRight;
                stackTop = leftCloser ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                ClosestHit_SharedStack[gl_LocalInvocationIndex][stackPtr++] = leftCloser ? right.TriStartOrLeftChild : left.TriStartOrLeftChild;
            }
            else
            {
                stackTop = leftChildHit ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
            }
        }
        else
        {
            if (stackPtr == 0) break;
            stackTop = ClosestHit_SharedStack[gl_LocalInvocationIndex][--stackPtr];
        }
    }

    return hitInfo.T != tMaxDist;
}

struct HitInfo
{
    vec3 Bary;
    float T;
    int TriangleIndex;
    uint MeshID;
    uint InstanceID;
};
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
    uint stackPtr = 0;
    uint stackTop = 0;
    uint stack[32];
    while (true)
    {
        TlasNode parent = tlasSSBO.Nodes[stackTop];
        float tMin, tMax;
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

            BlasHitInfo blasHitInfo;
            uint thisDebugNodeCounter;
            if (IntersectBlas(localRay, cmd.FirstIndex, hitInfo.T, blasHitInfo, thisDebugNodeCounter))
            {
                hitInfo.TriangleIndex = int(blasHitInfo.TriangleIndex);
                hitInfo.Bary = blasHitInfo.Bary;
                hitInfo.T = blasHitInfo.T;

                hitInfo.MeshID = parent.BlasIndex;
                hitInfo.InstanceID = glInstanceID;
            }
            debugNodeCounter += thisDebugNodeCounter;

            if (stackPtr == 0) break;
            stackTop = stack[--stackPtr];
            continue;
        }
        

        uint leftChild = parent.LeftChild;
        uint rightChild = leftChild + 1;
        TlasNode left = tlasSSBO.Nodes[leftChild];
        TlasNode right = tlasSSBO.Nodes[rightChild];

        float tMinLeft, tMinRight;
        bool leftChildHit = RayCuboidIntersect(ray, left.Min, left.Max, tMinLeft, tMax) && tMinLeft <= hitInfo.T;
        bool rightChildHit = RayCuboidIntersect(ray, right.Min, right.Max, tMinRight, tMax) && tMinRight <= hitInfo.T;

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
        
        uint glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
        Ray localRay = WorldSpaceRayToLocal(ray, meshInstanceSSBO.MeshInstances[glInstanceID].InvModelMatrix);

        BlasHitInfo blasHitInfo;
        uint thisDebugNodeCounter;
        if (IntersectBlas(localRay, cmd.FirstIndex, hitInfo.T, blasHitInfo, thisDebugNodeCounter))
        {
            hitInfo.TriangleIndex = int(blasHitInfo.TriangleIndex);
            hitInfo.Bary = blasHitInfo.Bary;
            hitInfo.T = blasHitInfo.T;

            hitInfo.MeshID = i;
            hitInfo.InstanceID = glInstanceID;
        }
        debugNodeCounter += thisDebugNodeCounter;
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