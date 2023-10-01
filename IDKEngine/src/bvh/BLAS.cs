using System;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class BLAS
    {
        public const float TriangleIntersectCost = 1.18f; // Ray-Triangle test is empirically 1.1834x computationally slower than Ray-Box test, excluding memory fetch
        public const int SAH_SAMPLES = 8;

        public struct HitInfo
        {
            public GpuTriangle Triangle;
            public Vector3 Bary;
            public float T;
        }

        public GpuBlasNode Root => Nodes[0];
        public Box RootBounds => new Box(Root.Min, Root.Max);
        public int TreeDepth { get; private set; }


        public readonly GpuTriangle[] Triangles;
        public GpuBlasNode[] Nodes;
        private int nodesUsed;
        public BLAS(GpuTriangle[] triangles)
        {
            Triangles = triangles;
            Nodes = Array.Empty<GpuBlasNode>();
        }

        public void Build()
        {
            Nodes = new GpuBlasNode[2 * Triangles.Length];

            ref GpuBlasNode root = ref Nodes[nodesUsed++];
            root.TriStartOrLeftChild = 0;
            root.TriCount = (uint)Triangles.Length;
            UpdateNodeBounds(ref root);
            Subdivide(ref root);

            // Handle edge case of the root node being leaf, by creating a child node as a copy of the root node
            if (nodesUsed == 1)
            {
                Nodes[nodesUsed++] = root;
            }

            // +1 because the specifics of the traversal algorithm which tests always this and the next node at once
            Array.Resize(ref Nodes, nodesUsed + 1);

            TreeDepth = (int)MathF.Ceiling(MathF.Log2(nodesUsed));
        }

        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new HitInfo();
            hitInfo.T = tMaxDist;

            if (!BVH.CPU_USE_TLAS)
            {
                ref readonly GpuBlasNode rootNode = ref Nodes[0];
                if (!(MyMath.RayCuboidIntersect(ray, rootNode.Min, rootNode.Max, out float tMinRoot, out float tMaxRoot) && tMinRoot < hitInfo.T))
                {
                    return false;
                }
            }

            uint stackPtr = 0;
            uint stackTop = 1;
            uint* stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GpuBlasNode left = ref Nodes[stackTop];
                ref readonly GpuBlasNode right = ref Nodes[stackTop + 1];
                bool leftChildHit = MyMath.RayCuboidIntersect(ray, left.Min, left.Max, out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = MyMath.RayCuboidIntersect(ray, right.Min, right.Max, out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                uint triCount = (leftChildHit ? left.TriCount : 0) + (rightChildHit ? right.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && (left.TriCount > 0)) ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                    for (uint k = first; k < first + triCount; k++)
                    {
                        ref readonly GpuTriangle triangle = ref Triangles[k];
                        if (MyMath.RayTriangleIntersect(ray, triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position, out Vector3 bary, out float t) && t < hitInfo.T)
                        {
                            hitInfo.Triangle = triangle;
                            hitInfo.Bary = bary;
                            hitInfo.T = t;
                        }
                    }

                    leftChildHit = leftChildHit && (left.TriCount == 0);
                    rightChildHit = rightChildHit && (right.TriCount == 0);
                }

                // Push closest hit child to the stack at last
                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                        stack[stackPtr++] = leftCloser ? right.TriStartOrLeftChild : left.TriStartOrLeftChild;
                    }
                    else
                    {
                        stackTop = leftChildHit ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                    }
                }
                else
                {
                    // Here: On a leaf node or didn't hit any children which means we should traverse up
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                }
            }

            return hitInfo.T != tMaxDist;
        }

        private void Subdivide(ref GpuBlasNode parentNode)
        {
            float splitCost = FindBestSplitAxis(parentNode, out int splitAxis, out float splitPos);
            float triangleIntersectCost = TriangleIntersectCost * parentNode.TriCount;
            if (splitCost >= triangleIntersectCost)
            {
                return;
            }

            uint start = parentNode.TriStartOrLeftChild;
            uint end = start + parentNode.TriCount;

            uint mid = start;
            for (uint i = start; i < end; i++)
            {
                ref GpuTriangle tri = ref Triangles[(int)i];
                if ((tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f < splitPos)
                {
                    MathHelper.Swap(ref tri, ref Triangles[(int)mid++]);
                }
            }

            parentNode.TriStartOrLeftChild = (uint)nodesUsed;
            parentNode.TriCount = 0;

            ref GpuBlasNode leftChild = ref Nodes[nodesUsed++];
            leftChild.TriStartOrLeftChild = start;
            leftChild.TriCount = mid - start;

            ref GpuBlasNode rightChild = ref Nodes[nodesUsed++];
            rightChild.TriStartOrLeftChild = mid;
            rightChild.TriCount = end - mid;

            UpdateNodeBounds(ref leftChild);
            UpdateNodeBounds(ref rightChild);

            Subdivide(ref leftChild);
            Subdivide(ref rightChild);
        }

        private float FindBestSplitAxis(in GpuBlasNode parentNode, out int splitAxis, out float splitPos)
        {
            splitAxis = 0;
            splitPos = 0;

            Box uniformDivideArea = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                ref readonly GpuTriangle tri = ref Triangles[(int)(parentNode.TriStartOrLeftChild + i)];
                Vector3 centroid = (tri.Vertex0.Position + tri.Vertex1.Position + tri.Vertex2.Position) / 3.0f;
                uniformDivideArea.GrowToFit(centroid);
            }

            float bestSplitCost = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (uniformDivideArea.Min[i] == uniformDivideArea.Max[i])
                {
                    continue;
                }

                float scale = (uniformDivideArea.Max[i] - uniformDivideArea.Min[i]) / (SAH_SAMPLES + 1);
                for (int j = 0; j < SAH_SAMPLES; j++)
                {
                    float currentSplitPos = uniformDivideArea.Min[i] + (j + 1) * scale;
                    float currentSplitCost = GetSplitCost(parentNode, i, currentSplitPos);
                    if (currentSplitCost < bestSplitCost)
                    {
                        splitPos = currentSplitPos;
                        splitAxis = i;
                        bestSplitCost = currentSplitCost;
                    }
                }
            }

            return bestSplitCost;
        }

        private float GetSplitCost(in GpuBlasNode parentNode, int splitAxis, float splitPos)
        {
            Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint leftCount = 0;
            for (uint i = 0; i < parentNode.TriCount; i++)
            {
                ref readonly GpuTriangle tri = ref Triangles[(int)(parentNode.TriStartOrLeftChild + i)];

                float triSplitPos = (tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f;
                if (triSplitPos < splitPos)
                {
                    leftCount++;
                    leftBox.GrowToFit(tri);
                }
                else
                {
                    rightBox.GrowToFit(tri);
                }
            }
            uint rightCount = parentNode.TriCount - leftCount;

            float sah = CostOfTracingRayAgainstNode(
                MyMath.HalfArea(parentNode.Max - parentNode.Min),
                MyMath.HalfArea(leftBox.Max - leftBox.Min),
                MyMath.HalfArea(rightBox.Max - rightBox.Min),
                leftCount,
                rightCount
            );
            //float sah = CalculateSAH(MyMath.HalfArea(leftBox.Max - leftBox.Min), leftCount, MyMath.HalfArea(rightBox.Max - rightBox.Min), rightCount);
            return sah;
        }

        private void UpdateNodeBounds(ref GpuBlasNode node)
        {
            Box bounds = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            for (uint i = node.TriStartOrLeftChild; i < node.TriStartOrLeftChild + node.TriCount; i++)
            {
                ref readonly GpuTriangle tri = ref Triangles[(int)i];
                bounds.GrowToFit(tri);
            }

            node.Min = bounds.Min;
            node.Max = bounds.Max;
        }

        // Desribed in https://www.nvidia.in/docs/IO/77714/sbvh.pdf, 2.1 BVH Construction
        private static float CostOfTracingRayAgainstNode(float areaParent, float areaLeftChild, float areaRightChild, uint numPrimitivesLeftChild, uint numPrimitivesRightChild)
        {
            float probabilityHitLeftChild = areaLeftChild / areaParent;
            float primitiveCostLeftChild = numPrimitivesLeftChild * TriangleIntersectCost;

            float probabilityHitRightChild = areaRightChild / areaParent;
            float primitiveCostRightChild = numPrimitivesRightChild * TriangleIntersectCost;

            return 1.0f + // This is the traversal cost. Keep it 1 so we can expose only one ratio as a paramater
                    probabilityHitLeftChild * primitiveCostLeftChild +
                    probabilityHitRightChild * primitiveCostRightChild;
        }
    }
}