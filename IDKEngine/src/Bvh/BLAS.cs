using System;
using OpenTK.Mathematics;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class BLAS
    {
        public const float TRIANGLE_INTERSECT_COST = 1.1f;
        public const float NODE_INTERSECT_COST = 1.0f; // Keep it 1 so we effectively only have TRIANGLE_INTERSECT_COST as a paramater

        public const int SAH_SAMPLES = 8;

        public struct RayHitInfo
        {
            public IndicesTriplet TriangleIndices;
            public Vector3 Bary;
            public float T;
        }

        public struct Triangle
        {
            public Vector3 Position0;
            public Vector3 Position1;
            public Vector3 Position2;
        }

        public struct IndicesTriplet
        {
            public uint X;
            public uint Y;
            public uint Z;
        }

        public GpuBlasNode Root => Nodes[0];
        public int TriangleCount => GeometryInfo.IndexCount / 3;
        public int MaxTreeDepth { get; private set; }

        public readonly GpuDrawElementsCmd GeometryInfo;
        public readonly Vector3[] VertexPositions;
        public readonly IndicesTriplet[] TriangleIndices;

        private int unpaddedNodesUsed;
        public GpuBlasNode[] Nodes;
        public BLAS(Vector3[] vertexPositions, ReadOnlySpan<uint> vertexIndices, GpuDrawElementsCmd geometryInfo)
        {
            GeometryInfo = geometryInfo;
            VertexPositions = vertexPositions;
            TriangleIndices = new IndicesTriplet[TriangleCount];
            for (int i = 0; i < TriangleIndices.Length; i++)
            {
                TriangleIndices[i].X = (uint)geometryInfo.BaseVertex + vertexIndices[geometryInfo.FirstIndex + (i * 3) + 0];
                TriangleIndices[i].Y = (uint)geometryInfo.BaseVertex + vertexIndices[geometryInfo.FirstIndex + (i * 3) + 1];
                TriangleIndices[i].Z = (uint)geometryInfo.BaseVertex + vertexIndices[geometryInfo.FirstIndex + (i * 3) + 2];
            }
        }

        public void Build()
        {
            Nodes = new GpuBlasNode[Math.Max(2 * TriangleCount - 1, 3)];
            int nodesUsed = 0;

            ref GpuBlasNode rootNode = ref Nodes[nodesUsed++];
            rootNode.TriStartOrLeftChild = 0;
            rootNode.TriCount = (uint)TriangleCount;
            UpdateNodeBounds(ref rootNode);

            int maxTraversalDepth = 0;
            int stackTop = 0;
            int stackPtr = 0;
            Span<int> stack = stackalloc int[128];
            while (true)
            {
                ref GpuBlasNode parentNode = ref Nodes[stackTop];
                if (!ShouldSplitNode(parentNode, out int splitAxis, out float splitPos, out float splitCost))
                {
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                GpuBlasNode newLeftNode;
                GpuBlasNode newRightNode;
                {
                    Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                    Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

                    uint middle = parentNode.TriStartOrLeftChild;
                    uint end = middle + parentNode.TriCount;
                    while (middle < end)
                    {
                        ref IndicesTriplet indices = ref TriangleIndices[middle];
                        Triangle tri = GetTriangle(indices);
                        float posOnSplitAxis = (tri.Position0[splitAxis] + tri.Position1[splitAxis] + tri.Position2[splitAxis]) / 3.0f;
                        if (posOnSplitAxis < splitPos)
                        {
                            leftBox.GrowToFit(tri);
                            middle++;
                        }
                        else
                        {
                            rightBox.GrowToFit(tri);
                            MathHelper.Swap(ref indices, ref TriangleIndices[--end]);
                        }
                    }

                    newLeftNode.TriStartOrLeftChild = parentNode.TriStartOrLeftChild;
                    newLeftNode.TriCount = middle - newLeftNode.TriStartOrLeftChild;
                    newLeftNode.Min = leftBox.Min;
                    newLeftNode.Max = leftBox.Max;

                    newRightNode.TriStartOrLeftChild = middle;
                    newRightNode.TriCount = parentNode.TriCount - newLeftNode.TriCount;
                    newRightNode.Min = rightBox.Min;
                    newRightNode.Max = rightBox.Max;
                }

                int leftNodeId = nodesUsed + 0;
                int rightNodeId = nodesUsed + 1;
                Nodes[leftNodeId] = newLeftNode;
                Nodes[rightNodeId] = newRightNode;

                stackTop = leftNodeId;
                stack[stackPtr++] = rightNodeId;

                parentNode.TriStartOrLeftChild = (uint)leftNodeId;
                parentNode.TriCount = 0;
                nodesUsed += 2;
                
                maxTraversalDepth = Math.Max(stackPtr, maxTraversalDepth);
            }

            unpaddedNodesUsed = nodesUsed;
            if (nodesUsed == 1)
            {
                // Handle edge case of the root node being a leaf by creating an artificial child node
                ref GpuBlasNode root = ref Nodes[0];
                Nodes[1] = root;
                root.TriCount = 0;
                root.TriStartOrLeftChild = 1;

                // Add an other dummy invisible node because the traversal algorithm always tests two nodes at once
                Nodes[2] = new GpuBlasNode()
                {
                    Min = new Vector3(float.MinValue),
                    Max = new Vector3(float.MinValue),
                    TriCount = 1, // mark as leaf
                    TriStartOrLeftChild = 0,
                };

                nodesUsed = 3;
            }

            Array.Resize(ref Nodes, nodesUsed);
            MaxTreeDepth = maxTraversalDepth;
        }

        public void Refit()
        {
            for (int i = unpaddedNodesUsed - 1; i >= 0; i--)
            {
                ref GpuBlasNode parent = ref Nodes[i];
                if (parent.IsLeaf())
                {
                    UpdateNodeBounds(ref parent);
                    continue;
                }

                ref readonly GpuBlasNode leftChild = ref Nodes[parent.TriStartOrLeftChild];
                ref readonly GpuBlasNode rightChild = ref Nodes[parent.TriStartOrLeftChild + 1];

                Box boundsFittingChildren = Conversions.ToBox(leftChild);
                boundsFittingChildren.GrowToFit(rightChild.Min);
                boundsFittingChildren.GrowToFit(rightChild.Max);

                parent.Min = boundsFittingChildren.Min;
                parent.Max = boundsFittingChildren.Max;
            }
        }

        public bool Intersect(in Ray ray, out RayHitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = tMaxDist;

            if (!BVH.CPU_USE_TLAS)
            {
                ref readonly GpuBlasNode rootNode = ref Nodes[0];
                if (!(Intersections.RayVsBox(ray, Conversions.ToBox(rootNode), out float tMinRoot, out float tMaxRoot) && tMinRoot < hitInfo.T))
                {
                    return false;
                }
            }

            int stackPtr = 0;
            uint stackTop = 1;
            Span<uint> stack = stackalloc uint[MaxTreeDepth];
            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref Nodes[stackTop];
                ref readonly GpuBlasNode rightNode = ref Nodes[stackTop + 1];
                bool leftChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && leftNode.IsLeaf()) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                    for (uint i = first; i < first + triCount; i++)
                    {
                        ref readonly IndicesTriplet indicesTriplet = ref TriangleIndices[i];
                        Triangle triangle = GetTriangle(indicesTriplet);

                        if (Intersections.RayVsTriangle(ray, Conversions.ToTriangle(triangle), out Vector3 bary, out float t) && t < hitInfo.T)
                        {
                            hitInfo.TriangleIndices = indicesTriplet;
                            hitInfo.Bary = bary;
                            hitInfo.T = t;
                        }
                    }

                    leftChildHit = leftChildHit && !leftNode.IsLeaf();
                    rightChildHit = rightChildHit && !rightNode.IsLeaf();
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

        public delegate void BoxIntersectFunc(in IndicesTriplet indicesTriplet);
        public void Intersect(in Box box, BoxIntersectFunc intersectFunc)
        {
            int stackPtr = 0;
            uint stackTop = 1;
            Span<uint> stack = stackalloc uint[MaxTreeDepth];
            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref Nodes[stackTop];
                ref readonly GpuBlasNode rightNode = ref Nodes[stackTop + 1];
                bool leftChildHit = Intersections.BoxVsBox(Conversions.ToBox(leftNode), box);
                bool rightChildHit = Intersections.BoxVsBox(Conversions.ToBox(rightNode), box);

                uint triCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrLeftChild : rightNode.TriStartOrLeftChild;
                    for (uint i = first; i < first + triCount; i++)
                    {
                        ref readonly IndicesTriplet indicesTriplet = ref TriangleIndices[i];
                        Triangle triangle = GetTriangle(indicesTriplet);

                        if (Intersections.BoxVsTriangle(box, Conversions.ToTriangle(triangle)))
                        {
                            intersectFunc(indicesTriplet);
                        }
                    }

                    leftChildHit = leftChildHit && !leftNode.IsLeaf();
                    rightChildHit = rightChildHit && !rightNode.IsLeaf();
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

        private bool ShouldSplitNode(in GpuBlasNode parentNode, out int splitAxis, out float splitPos, out float splittedCost)
        {
            Box areaForSplits = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[parentNode.TriStartOrLeftChild + i]);

                Vector3 centroid = (tri.Position0 + tri.Position1 + tri.Position2) / 3.0f;
                areaForSplits.GrowToFit(centroid);
            }

            splitAxis = 0;
            splitPos = 0;
            splittedCost = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float minMaxLength = MathF.Abs(areaForSplits.Max[i] - areaForSplits.Min[i]);
                if (minMaxLength == 0.0f)
                {
                    continue;
                }

                float scale = (areaForSplits.Max[i] - areaForSplits.Min[i]) / (SAH_SAMPLES + 1);
                for (int j = 0; j < SAH_SAMPLES; j++)
                {
                    float currentSplitPos = areaForSplits.Min[i] + (j + 1) * scale;
                    float currentSplitCost = GetCostOfSplittingNodeAt(parentNode, i, currentSplitPos);
                    if (currentSplitCost < splittedCost)
                    {
                        splitPos = currentSplitPos;
                        splitAxis = i;
                        splittedCost = currentSplitCost;
                    }
                }
            }

            float parentNodeCost = CostOfHittingLeafNode(parentNode.TriCount);
            bool splittingIsWorthIt = splittedCost < parentNodeCost;

            return splittingIsWorthIt;
        }

        private float GetCostOfSplittingNodeAt(in GpuBlasNode parentNode, int splitAxis, float splitPos)
        {
            Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint leftBoxCount = 0;
            for (uint i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[parentNode.TriStartOrLeftChild + i]);

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

            float areaParent = MyMath.Area(parentNode.Max - parentNode.Min);
            float probHitLeftChild = leftBox.Area() / areaParent;
            float probHitRightChild = rightBox.Area() / areaParent;

            float cost = EstimatedIntersectionCost(
                probHitLeftChild,
                probHitRightChild,
                leftBoxCount,
                rightBoxCount
            );

            return cost;
        }

        public Triangle GetTriangle(in IndicesTriplet indices)
        {
            ref readonly Vector3 v0 = ref VertexPositions[indices.X];
            ref readonly Vector3 v1 = ref VertexPositions[indices.Y];
            ref readonly Vector3 v2 = ref VertexPositions[indices.Z];

            return new Triangle() { Position0 = v0, Position1 = v1, Position2 = v2 };
        }
        
        public void InternalNodeGetTriStartAndCount(in GpuBlasNode node, out uint triStart, out uint triCount)
        {
            GpuBlasNode nextNode = node;
            uint veryLeftLeafTriStart;
            while (!nextNode.IsLeaf())
            {
                nextNode = Nodes[nextNode.TriStartOrLeftChild];
            }
            veryLeftLeafTriStart = nextNode.TriStartOrLeftChild;


            nextNode = node;
            uint veryRightLeafTriEnd;
            while (!nextNode.IsLeaf())
            {
                nextNode = Nodes[nextNode.TriStartOrLeftChild + 1];
            }
            veryRightLeafTriEnd = nextNode.TriStartOrLeftChild + nextNode.TriCount;

            triStart = veryLeftLeafTriStart;
            triCount = veryRightLeafTriEnd - veryLeftLeafTriStart;
        }
        
        public void UpdateNodeBounds(ref GpuBlasNode node)
        {
            Box box = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (uint i = node.TriStartOrLeftChild; i < node.TriStartOrLeftChild + node.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[i]);
                box.GrowToFit(tri);
            }
            node.Min = box.Min;
            node.Max = box.Max;
        }

        // Implementation of "Surface Area Heuristic" described in https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
        private static float EstimatedIntersectionCost(float probabilityHitLeftChild, float probabilityHitRightChild, uint numTrianglesLeftChild, uint numTrianglesRightChild)
        {
            return NODE_INTERSECT_COST +
                   (probabilityHitLeftChild * CostOfHittingLeafNode(numTrianglesLeftChild) +
                   probabilityHitRightChild * CostOfHittingLeafNode(numTrianglesRightChild));
        }

        private static float CostOfHittingLeafNode(uint numTriangles)
        {
            return numTriangles * TRIANGLE_INTERSECT_COST;
        }
    }
}