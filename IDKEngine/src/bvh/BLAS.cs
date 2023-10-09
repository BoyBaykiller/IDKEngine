using System;
using OpenTK.Mathematics;
using IDKEngine.Shapes;

namespace IDKEngine
{
    class BLAS
    {
        public const float TRIANGLE_INTERSECT_COST = 1.18f; // Ray-Triangle test is empirically 1.1834x computationally slower than Ray-Box test, excluding memory fetch
        public const int SAH_SAMPLES = 8;

        public struct RayHitInfo
        {
            public GpuTriangle Triangle;
            public Vector3 Bary;
            public float T;
        }

        public GpuBlasNode Root => Nodes[0];
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
            Nodes = new GpuBlasNode[2 * Triangles.Length + 1];

            ref GpuBlasNode root = ref Nodes[nodesUsed++];
            root.TriStartOrLeftChild = 0;
            root.TriCount = (uint)Triangles.Length;
            UpdateNodeBounds(ref root);
            SplitNode(ref root);

            // Handle edge case of the root node being leaf, by creating a child node as a copy of the root node
            if (nodesUsed == 1)
            {
                Nodes[nodesUsed++] = root;
            }

            // Add dummy invisible node because of the specifics of the traversal algorithm which tests always two nodes at once
            Nodes[nodesUsed].Min = new Vector3(float.MinValue);
            Nodes[nodesUsed++].Max = new Vector3(float.MinValue);
            Array.Resize(ref Nodes, nodesUsed);

            TreeDepth = (int)MathF.Ceiling(MathF.Log2(nodesUsed));
        }

        private void SplitNode(ref GpuBlasNode parentNode)
        {
            if (!ShouldSplitNode(parentNode, out int splitAxis, out float splitPos, out float splitCost))
            {
                return;
            }

            uint currentIndex = parentNode.TriStartOrLeftChild;
            uint end = currentIndex + parentNode.TriCount;
            while (currentIndex < end)
            {
                ref GpuTriangle tri = ref Triangles[(int)currentIndex];
                float posOnSplitAxis = (tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f;
                if (posOnSplitAxis < splitPos)
                {
                    currentIndex++;
                }
                else
                {
                    MathHelper.Swap(ref tri, ref Triangles[--end]);
                }
            }

            ref GpuBlasNode leftChild = ref Nodes[nodesUsed++];
            leftChild.TriStartOrLeftChild = parentNode.TriStartOrLeftChild;
            leftChild.TriCount = currentIndex - leftChild.TriStartOrLeftChild;

            ref GpuBlasNode rightChild = ref Nodes[nodesUsed++];
            rightChild.TriStartOrLeftChild = currentIndex;
            rightChild.TriCount = parentNode.TriCount - leftChild.TriCount;

            parentNode.TriStartOrLeftChild = (uint)nodesUsed - 2;
            parentNode.TriCount = 0;

            UpdateNodeBounds(ref leftChild);
            UpdateNodeBounds(ref rightChild);

            SplitNode(ref leftChild);
            SplitNode(ref rightChild);
        }

        private bool ShouldSplitNode(in GpuBlasNode parentNode, out int splitAxis, out float splitPos, out float splitCost)
        {
            Box uniformDivideArea = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                ref readonly GpuTriangle tri = ref Triangles[parentNode.TriStartOrLeftChild + i];
                Vector3 centroid = (tri.Vertex0.Position + tri.Vertex1.Position + tri.Vertex2.Position) / 3.0f;
                uniformDivideArea.GrowToFit(centroid);
            }

            splitAxis = 0;
            splitPos = 0;
            splitCost = float.MaxValue;
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
                    float currentSplitCost = GetCostOfSplittingNodeAt(parentNode, i, currentSplitPos);
                    if (currentSplitCost < splitCost)
                    {
                        splitPos = currentSplitPos;
                        splitAxis = i;
                        splitCost = currentSplitCost;
                    }
                }
            }

            float parentNodeCost = TrianglesIntersectCost(parentNode.TriCount);
            return splitCost < parentNodeCost;
        }

        private float GetCostOfSplittingNodeAt(in GpuBlasNode parentNode, int splitAxis, float splitPos)
        {
            Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint leftBoxCount = 0;
            for (uint i = 0; i < parentNode.TriCount; i++)
            {
                ref readonly GpuTriangle tri = ref Triangles[parentNode.TriStartOrLeftChild + i];

                float triSplitPos = (tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f;
                if (triSplitPos < splitPos)
                {
                    leftBoxCount++;
                    leftBox.GrowToFit(tri);
                }
                else
                {
                    rightBox.GrowToFit(tri);
                }
            }
            uint rightBoxCount = parentNode.TriCount - leftBoxCount;

            float sah = SurfaceAreaHeuristic(
                MyMath.HalfArea(parentNode.Max - parentNode.Min),
                MyMath.HalfArea(leftBox.Max - leftBox.Min),
                MyMath.HalfArea(rightBox.Max - rightBox.Min),
                leftBoxCount,
                rightBoxCount
            );

            return sah;
        }

        private void UpdateNodeBounds(ref GpuBlasNode node)
        {
            Box bounds = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            for (uint i = node.TriStartOrLeftChild; i < node.TriStartOrLeftChild + node.TriCount; i++)
            {
                ref readonly GpuTriangle tri = ref Triangles[i];
                bounds.GrowToFit(tri);
            }

            node.Min = bounds.Min;
            node.Max = bounds.Max;
        }

        public unsafe bool Intersect(in Ray ray, out RayHitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = tMaxDist;

            if (!BVH.CPU_USE_TLAS)
            {
                ref readonly GpuBlasNode rootNode = ref Nodes[0];
                if (!(Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(rootNode), out float tMinRoot, out float tMaxRoot) && tMinRoot < hitInfo.T))
                {
                    return false;
                }
            }

            uint stackPtr = 0;
            uint stackTop = 1;
            uint* stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref Nodes[stackTop];
                ref readonly GpuBlasNode rightode = ref Nodes[stackTop + 1];
                bool leftChildHit = Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(rightode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightode.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightode.TriStartOrLeftChild;
                    for (uint i = first; i < first + triCount; i++)
                    {
                        ref readonly GpuTriangle triangle = ref Triangles[i];
                        if (Intersections.RayVsTriangle(ray, GpuTypes.Conversions.ToTriangle(triangle), out Vector3 bary, out float t) && t < hitInfo.T)
                        {
                            hitInfo.Triangle = triangle;
                            hitInfo.Bary = bary;
                            hitInfo.T = t;
                        }
                    }

                    leftChildHit = leftChildHit && (leftNode.TriCount == 0);
                    rightChildHit = rightChildHit && (rightode.TriCount == 0);
                }

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? leftNode.TriStartOrLeftChild : rightode.TriStartOrLeftChild;
                        stack[stackPtr++] = leftCloser ? rightode.TriStartOrLeftChild : leftNode.TriStartOrLeftChild;
                    }
                    else
                    {
                        stackTop = leftChildHit ? leftNode.TriStartOrLeftChild : rightode.TriStartOrLeftChild;
                    }
                }
                else
                {
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                }
            }

            return hitInfo.T != tMaxDist;
        }

        public delegate void BoxIntersectFunc(in GpuTriangle triangle);
        public unsafe void Intersect(in Box box, BoxIntersectFunc intersectFunc)
        {
            uint stackPtr = 0;
            uint stackTop = 1;
            uint* stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref Nodes[stackTop];
                ref readonly GpuBlasNode rightNode = ref Nodes[stackTop + 1];
                bool leftChildHit = Intersections.BoxVsBox(GpuTypes.Conversions.ToBox(leftNode), box);
                bool rightChildHit = Intersections.BoxVsBox(GpuTypes.Conversions.ToBox(rightNode), box);

                uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                    for (uint i = first; i < first + triCount; i++)
                    {
                        ref readonly GpuTriangle triangle = ref Triangles[i];
                        if (Intersections.BoxVsTriangle(box, GpuTypes.Conversions.ToTriangle(triangle)))
                        {
                            intersectFunc(triangle);
                        }
                    }

                    leftChildHit = leftChildHit && (leftNode.TriCount == 0);
                    rightChildHit = rightChildHit && (rightNode.TriCount == 0);
                }

                if (leftChildHit || rightChildHit)
                {
                    stackTop = leftChildHit ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                    if (leftChildHit && rightChildHit)
                    {
                        stack[stackPtr++] = rightNode.TriStartOrLeftChild;
                    }
                }
                else
                {
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                }
            }
        }


        // Desribed in https://www.nvidia.in/docs/IO/77714/sbvh.pdf, 2.1 BVH Construction
        private static float SurfaceAreaHeuristic(float areaParent, float areaLeftChild, float areaRightChild, uint numTrianglesLeftChild, uint numTrianglesRightChild)
        {
            float probabilityHitLeftChild = areaLeftChild / areaParent;
            float probabilityHitRightChild = areaRightChild / areaParent;

            return 1.0f + // This is the traversal cost. Keep it 1 so we can expose only one ratio as a paramater
                    CostOfHittingNode(probabilityHitLeftChild, numTrianglesLeftChild) +
                    CostOfHittingNode(probabilityHitRightChild, numTrianglesRightChild);
        }

        private static float CostOfHittingNode(float probabilityOfHitting, uint numTriangles)
        {
            return probabilityOfHitting * TrianglesIntersectCost(numTriangles);
        }

        private static float TrianglesIntersectCost(uint numTriangles)
        {
            return numTriangles * TRIANGLE_INTERSECT_COST;
        }
    }
}