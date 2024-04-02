using System;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class BLAS
    {
        public const float TRIANGLE_INTERSECT_COST = 1.1f;
        public const float NODE_INTERSECT_COST = 1.0f;

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

        private int unpaddedNodesCount;
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
            rootNode.TriStartOrChild = 0;
            rootNode.TriCount = (uint)TriangleCount;
            UpdateNodeBounds(ref rootNode);

            int treeDepth = 0;

            int stackTop = 0;
            int stackPtr = 0;
            Span<int> stack = stackalloc int[128];
            while (true)
            {
                treeDepth = Math.Max(stackPtr + 1, treeDepth);

                ref GpuBlasNode parentNode = ref Nodes[stackTop];
                if (!ShouldSplitNode(parentNode, out int splitAxis, out float splitPos, out _))
                {
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                GpuBlasNode newLeftNode = new GpuBlasNode();
                GpuBlasNode newRightNode = new GpuBlasNode();
                {
                    Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                    Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

                    uint middle = parentNode.TriStartOrChild;
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

                    newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                    newLeftNode.TriCount = middle - newLeftNode.TriStartOrChild;
                    newLeftNode.Min = leftBox.Min;
                    newLeftNode.Max = leftBox.Max;

                    newRightNode.TriStartOrChild = middle;
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

                parentNode.TriStartOrChild = (uint)leftNodeId;
                parentNode.TriCount = 0;
                nodesUsed += 2;
            }

            unpaddedNodesCount = nodesUsed;
            if (nodesUsed < 3)
            {
                // Handle edge case of the root node being a leaf by creating an artificial child node
                ref GpuBlasNode root = ref Nodes[0];
                Nodes[1] = root;
                root.TriCount = 0;
                root.TriStartOrChild = 1;

                // Add an other dummy invisible node because the traversal algorithm always tests two nodes at once
                Nodes[2] = new GpuBlasNode()
                {
                    Min = new Vector3(float.MinValue),
                    Max = new Vector3(float.MinValue),
                    TriCount = 1, // mark as leaf
                    TriStartOrChild = 0,
                };

                nodesUsed = 3;
                treeDepth = 2;
            }

            Array.Resize(ref Nodes, nodesUsed);
            MaxTreeDepth = treeDepth;
        }

        public void Refit()
        {
            for (int i = unpaddedNodesCount - 1; i >= 0; i--)
            {
                ref GpuBlasNode parent = ref Nodes[i];
                if (parent.IsLeaf)
                {
                    UpdateNodeBounds(ref parent);
                    continue;
                }

                ref readonly GpuBlasNode leftChild = ref Nodes[parent.TriStartOrChild];
                ref readonly GpuBlasNode rightChild = ref Nodes[parent.TriStartOrChild + 1];

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

            uint stackTop = 1;
            int stackPtr = 0;
            Span<uint> stack = stackalloc uint[MaxTreeDepth];
            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref Nodes[stackTop];
                ref readonly GpuBlasNode rightNode = ref Nodes[stackTop + 1];
                bool leftChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                uint summedTriCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (summedTriCount > 0)
                {
                    uint first = (leftChildHit && leftNode.IsLeaf) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    for (uint i = first; i < first + summedTriCount; i++)
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

                    if (leftNode.IsLeaf) leftChildHit = false;
                    if (rightNode.IsLeaf) rightChildHit = false;
                }

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                        stack[stackPtr++] = leftCloser ? rightNode.TriStartOrChild : leftNode.TriStartOrChild;
                        if (stackPtr - 1 >= MaxTreeDepth)
                        {
                            Console.WriteLine("penis");
                        }
                    }
                    else
                    {
                        stackTop = leftChildHit ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
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

        public delegate void FuncIntersectLeafNode(in IndicesTriplet leafNodeTriangle);
        public void Intersect(in Box box, FuncIntersectLeafNode intersectFunc)
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

                uint summedTriCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (summedTriCount > 0)
                {
                    uint first = (leftChildHit && (leftNode.TriCount > 0)) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    for (uint i = first; i < first + summedTriCount; i++)
                    {
                        ref readonly IndicesTriplet indicesTriplet = ref TriangleIndices[i];
                        intersectFunc(indicesTriplet);
                    }

                    if (leftNode.IsLeaf) leftChildHit = false;
                    if (rightNode.IsLeaf) rightChildHit = false;
                }

                if (leftChildHit || rightChildHit)
                {
                    stackTop = leftChildHit ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    if (leftChildHit && rightChildHit)
                    {
                        stack[stackPtr++] = rightNode.TriStartOrChild;
                    }
                }
                else
                {
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                }
            }
        }

        public float ComputeCostOfNode(in GpuBlasNode parentNode)
        {
            if (parentNode.IsLeaf)
            {
                return CostLeafNode(parentNode.TriCount);
            }

            ref readonly GpuBlasNode leftChild = ref Nodes[parentNode.TriStartOrChild];
            ref readonly GpuBlasNode rightChild = ref Nodes[parentNode.TriStartOrChild + 1];

            float areaParent = MyMath.Area(parentNode.Max - parentNode.Min);
            float probHitLeftChild = Conversions.ToBox(leftChild).Area() / areaParent;
            float probHitRightChild = Conversions.ToBox(rightChild).Area() / areaParent;

            float cost = CostInternalNode(probHitLeftChild, probHitRightChild, ComputeCostOfNode(leftChild), ComputeCostOfNode(rightChild));

            return cost;
        }

        private bool ShouldSplitNode(in GpuBlasNode parentNode, out int splitAxis, out float splitPos, out float costIfSplit)
        {
            Box areaForSplits = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[parentNode.TriStartOrChild + i]);

                Vector3 centroid = (tri.Position0 + tri.Position1 + tri.Position2) / 3.0f;
                areaForSplits.GrowToFit(centroid);
            }

            splitAxis = 0;
            splitPos = 0.0f;
            costIfSplit = float.MaxValue;
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
                    float currentSplitCost = EstimateCostOfSplittingNode(parentNode, i, currentSplitPos);
                    if (currentSplitCost < costIfSplit)
                    {
                        splitPos = currentSplitPos;
                        splitAxis = i;
                        costIfSplit = currentSplitCost;
                    }
                }
            }

            float costIfNotSplit = CostLeafNode(parentNode.TriCount);
            bool splittingIsWorthIt = costIfSplit < costIfNotSplit;

            return splittingIsWorthIt;
        }
        private float EstimateCostOfSplittingNode(in GpuBlasNode parentNode, int splitAxis, float splitPos)
        {
            Box leftBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            Box rightBox = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint leftBoxCount = 0;
            for (uint i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[parentNode.TriStartOrChild + i]);

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

            // Implementation of "Surface Area Heuristic" described in https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
            float areaParent = MyMath.Area(parentNode.Max - parentNode.Min);
            float probHitLeftChild = leftBox.Area() / areaParent;
            float probHitRightChild = rightBox.Area() / areaParent;

            // Estimates cost of hitting parentNode if it was split at the evaluted split position
            // The full "Surface Area Heuristic" is recurisve, but in practive we assume
            // the resulting child nodes are leafs
            float surfaceAreaHeuristic = CostInternalNode(
                probHitLeftChild,
                probHitRightChild,
                CostLeafNode(leftBoxCount),
                CostLeafNode(rightBoxCount)
            );

            return surfaceAreaHeuristic;
        }

        public Triangle GetTriangle(in IndicesTriplet indices)
        {
            ref readonly Vector3 p0 = ref VertexPositions[indices.X];
            ref readonly Vector3 p1 = ref VertexPositions[indices.Y];
            ref readonly Vector3 v2 = ref VertexPositions[indices.Z];

            return new Triangle() { Position0 = p0, Position1 = p1, Position2 = v2 };
        }
        public void UpdateNodeBounds(ref GpuBlasNode node)
        {
            Box box = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (uint i = node.TriStartOrChild; i < node.TriStartOrChild + node.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[i]);
                box.GrowToFit(tri);
            }
            node.Min = box.Min;
            node.Max = box.Max;
        }
        public void InternalNodeGetTriStartAndCount(in GpuBlasNode node, out uint triStart, out uint triCount)
        {
            GpuBlasNode nextNode = node;
            uint veryLeftLeafTriStart;
            while (!nextNode.IsLeaf)
            {
                nextNode = Nodes[nextNode.TriStartOrChild];
            }
            veryLeftLeafTriStart = nextNode.TriStartOrChild;


            nextNode = node;
            uint veryRightLeafTriEnd;
            while (!nextNode.IsLeaf)
            {
                nextNode = Nodes[nextNode.TriStartOrChild + 1];
            }
            veryRightLeafTriEnd = nextNode.TriStartOrChild + nextNode.TriCount;

            triStart = veryLeftLeafTriStart;
            triCount = veryRightLeafTriEnd - veryLeftLeafTriStart;
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild)
        {
            return NODE_INTERSECT_COST +
                   (probabilityHitLeftChild * costLeftChild +
                   probabilityHitRightChild * costRightChild);
        }
        private static float CostLeafNode(uint numTriangles)
        {
            return numTriangles * TRIANGLE_INTERSECT_COST;
        }
    }
}