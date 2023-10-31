using System;
using OpenTK.Mathematics;
using IDKEngine.Shapes;

namespace IDKEngine
{
    class BLAS
    {
        public const float TRIANGLE_INTERSECT_COST = 1.0f;
        public const int SAH_SAMPLES = 8;

        public struct RayHitInfo
        {
            public GpuBlasTriangle Triangle;
            public Vector3 Bary;
            public float T;
        }


        public GpuBlasNode Root => Nodes[0];
        public int TreeDepth { get; private set; }

        public readonly GpuBlasTriangle[] Triangles;
        public GpuBlasNode[] Nodes;

        private int nodesUsed;
        public BLAS(GpuBlasTriangle[] triangles)
        {
            Triangles = triangles;
            Nodes = Array.Empty<GpuBlasNode>();
        }

        public void Build()
        {
            Nodes = new GpuBlasNode[Math.Max(2 * Triangles.Length - 1, 3)];

            ref GpuBlasNode root = ref Nodes[nodesUsed++];
            root.TriStartOrLeftChild = 0;
            root.TriCount = (uint)Triangles.Length;
            UpdateNodeBounds(ref root);
            SplitNode(ref root);

            // Handle edge case of the root node being leaf, by creating a child node as a copy of the root node
            if (nodesUsed == 1)
            {
                Nodes[nodesUsed++] = root;

                // Add dummy invisible node because of the specifics of the traversal algorithm which tests always two nodes at once
                Nodes[nodesUsed++] = new GpuBlasNode()
                {
                    Min = new Vector3(float.MinValue),
                    Max = new Vector3(float.MinValue),
                    TriCount = 0,
                    TriStartOrLeftChild = 0,
                };
            }

            Array.Resize(ref Nodes, nodesUsed);

            TreeDepth = (int)MathF.Ceiling(MathF.Log2(nodesUsed));
        }

        private void SplitNode(ref GpuBlasNode parentNode)
        {
            if (!ShouldSplitNode(parentNode, out int splitAxis, out float splitPos, out float splitCost))
            {
                return;
            }

            Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint rightChildTriStart = parentNode.TriStartOrLeftChild;
            uint end = rightChildTriStart + parentNode.TriCount;
            while (rightChildTriStart < end)
            {
                ref GpuBlasTriangle tri = ref Triangles[(int)rightChildTriStart];
                float posOnSplitAxis = (tri.Position0[splitAxis] + tri.Position1[splitAxis] + tri.Position2[splitAxis]) / 3.0f;
                if (posOnSplitAxis < splitPos)
                {
                    leftBox.GrowToFit(tri);
                    rightChildTriStart++;
                }
                else
                {
                    rightBox.GrowToFit(tri);
                    MathHelper.Swap(ref tri, ref Triangles[--end]);
                }
            }

            ref GpuBlasNode leftChild = ref Nodes[nodesUsed++];
            leftChild.TriStartOrLeftChild = parentNode.TriStartOrLeftChild;
            leftChild.TriCount = rightChildTriStart - leftChild.TriStartOrLeftChild;
            leftChild.Min = leftBox.Min;
            leftChild.Max = leftBox.Max;

            ref GpuBlasNode rightChild = ref Nodes[nodesUsed++];
            rightChild.TriStartOrLeftChild = rightChildTriStart;
            rightChild.TriCount = parentNode.TriCount - leftChild.TriCount;
            rightChild.Min = rightBox.Min;
            rightChild.Max = rightBox.Max;

            parentNode.TriStartOrLeftChild = (uint)nodesUsed - 2;
            parentNode.TriCount = 0;

            SplitNode(ref leftChild);
            SplitNode(ref rightChild);
        }

        private bool ShouldSplitNode(in GpuBlasNode parentNode, out int splitAxis, out float splitPos, out float splitCost)
        {
            Box areaForSplits = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                ref readonly GpuBlasTriangle tri = ref Triangles[parentNode.TriStartOrLeftChild + i];
                Vector3 centroid = (tri.Position0 + tri.Position1 + tri.Position2) / 3.0f;
                areaForSplits.GrowToFit(centroid);
            }

            splitAxis = 0;
            splitPos = 0;
            splitCost = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (areaForSplits.Min[i] == areaForSplits.Max[i])
                {
                    continue;
                }

                float scale = (areaForSplits.Max[i] - areaForSplits.Min[i]) / (SAH_SAMPLES + 1);
                for (int j = 0; j < SAH_SAMPLES; j++)
                {
                    float currentSplitPos = areaForSplits.Min[i] + (j + 1) * scale;
                    float currentSplitCost = GetCostOfSplittingNodeAt(parentNode, i, currentSplitPos);
                    if (currentSplitCost < splitCost)
                    {
                        splitPos = currentSplitPos;
                        splitAxis = i;
                        splitCost = currentSplitCost;
                    }
                }
            }

            float parentNodeCost = CostOfHittingLeafNode(parentNode.TriCount);
            bool splittingIsWorthIt = splitCost < parentNodeCost;

            return splittingIsWorthIt;
        }

        private float GetCostOfSplittingNodeAt(in GpuBlasNode parentNode, int splitAxis, float splitPos)
        {
            Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint leftBoxCount = 0;
            for (uint i = 0; i < parentNode.TriCount; i++)
            {
                ref readonly GpuBlasTriangle tri = ref Triangles[parentNode.TriStartOrLeftChild + i];

                float triSplitPos = (tri.Position0[splitAxis] + tri.Position1[splitAxis] + tri.Position2[splitAxis]) / 3.0f;
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
                MyMath.Area(parentNode.Max - parentNode.Min),
                leftBox.Area(),
                rightBox.Area(),
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
                ref readonly GpuBlasTriangle tri = ref Triangles[i];
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
                ref readonly GpuBlasNode rightNode = ref Nodes[stackTop + 1];
                bool leftChildHit = Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(rightNode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                    for (uint i = first; i < first + triCount; i++)
                    {
                        ref readonly GpuBlasTriangle triangle = ref Triangles[i];
                        if (Intersections.RayVsTriangle(ray, GpuTypes.Conversions.ToTriangle(triangle), out Vector3 bary, out float t) && t < hitInfo.T)
                        {
                            hitInfo.Triangle = triangle;
                            hitInfo.Bary = bary;
                            hitInfo.T = t;
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
                        stack[stackPtr++] = leftCloser ? rightNode.TriStartOrLeftChild : leftNode.TriStartOrLeftChild;
                    }
                    else
                    {
                        stackTop = leftChildHit ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
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

        public delegate void BoxIntersectFunc(in GpuBlasTriangle triangle);
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
                        ref readonly GpuBlasTriangle triangle = ref Triangles[i];
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

        public void InternalNodeGetTriStartAndCount(in GpuBlasNode node, out uint triStart, out uint triCount)
        {
            GpuBlasNode nextNode = node;
            uint veryLeftLeafTriStart;
            while (nextNode.TriCount == 0)
            {
                nextNode = Nodes[nextNode.TriStartOrLeftChild];
            }
            veryLeftLeafTriStart = nextNode.TriStartOrLeftChild;


            nextNode = node;
            uint veryRightLeafTriEnd;
            while (nextNode.TriCount == 0)
            {
                nextNode = Nodes[nextNode.TriStartOrLeftChild + 1];
            }
            veryRightLeafTriEnd = nextNode.TriStartOrLeftChild + nextNode.TriCount;

            triStart = veryLeftLeafTriStart;
            triCount = veryRightLeafTriEnd - veryLeftLeafTriStart;
        }

        // Desribed in https://www.nvidia.in/docs/IO/77714/sbvh.pdf, 2.1 BVH Construction
        private static float SurfaceAreaHeuristic(float areaParent, float areaLeftChild, float areaRightChild, uint numTrianglesLeftChild, uint numTrianglesRightChild)
        {
            float probabilityHitLeftChild = areaLeftChild / areaParent;
            float probabilityHitRightChild = areaRightChild / areaParent;

            return 1.0f + // This is the traversal cost. Keep it 1 so we can expose only one ratio as a paramater
                   (CostOfHittingInternalNode(probabilityHitLeftChild, numTrianglesLeftChild) +
                   CostOfHittingInternalNode(probabilityHitRightChild, numTrianglesRightChild));
        }

        private static float CostOfHittingInternalNode(float probabilityOfHitting, uint numTriangles)
        {
            return probabilityOfHitting * CostOfHittingLeafNode(numTriangles);
        }

        private static float CostOfHittingLeafNode(uint numTriangles)
        {
            return numTriangles * TRIANGLE_INTERSECT_COST;
        }
    }
}