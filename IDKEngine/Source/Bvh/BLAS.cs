using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class BLAS
    {
        public const float TRIANGLE_INTERSECT_COST = 1.1f;
        public const float TRAVERSAL_INTERSECT_COST = 1.0f;

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

        private struct Bin
        {
            public Box TriangleBounds = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            public uint TriangleCount;

            public Bin()
            {
            }
        }

        public ref readonly GpuBlasNode Root => ref Nodes[0];
        public int TriangleCount => GeometryInfo.IndexCount / 3;
        public int MaxTreeDepth { get; private set; }

        public GpuBlasNode[] Nodes;
        public readonly BBG.DrawElementsIndirectCommand GeometryInfo;
        public readonly Vector3[] VertexPositions;
        public readonly IndicesTriplet[] Triangles;

        private int unpaddedNodesCount;
        public BLAS(Vector3[] vertexPositions, ReadOnlySpan<uint> vertexIndices, in BBG.DrawElementsIndirectCommand geometryInfo)
        {
            GeometryInfo = geometryInfo;
            VertexPositions = vertexPositions;
            Triangles = new IndicesTriplet[TriangleCount];
            for (int i = 0; i < Triangles.Length; i++)
            {
                Triangles[i].X = (uint)geometryInfo.BaseVertex + vertexIndices[geometryInfo.FirstIndex + (i * 3) + 0];
                Triangles[i].Y = (uint)geometryInfo.BaseVertex + vertexIndices[geometryInfo.FirstIndex + (i * 3) + 1];
                Triangles[i].Z = (uint)geometryInfo.BaseVertex + vertexIndices[geometryInfo.FirstIndex + (i * 3) + 2];
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
                        ref IndicesTriplet indices = ref Triangles[middle];
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
                            MathHelper.Swap(ref indices, ref Triangles[--end]);
                        }
                    }

                    if (middle == parentNode.TriStartOrChild || middle == parentNode.TriStartOrChild + parentNode.TriCount)
                    {
                        // Here all triangles ended up in one node. Happens in binned-builder only. Do median split?
                        if (stackPtr == 0) break;
                        stackTop = stack[--stackPtr];
                        continue;
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
            if (nodesUsed == 1)
            {
                // Handle edge case of the root node being a leaf by creating an artificial child node
                Nodes[1] = Nodes[0];
                Nodes[0].TriCount = 0;
                Nodes[0].TriStartOrChild = 1;

                // Add an other dummy invisible node because the traversal algorithm always tests two nodes at once
                Nodes[2] = new GpuBlasNode();
                Nodes[2].Min = new Vector3(float.MinValue);
                Nodes[2].Max = new Vector3(float.MinValue);
                Nodes[2].TriCount = 1;  // mark as leaf

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
                        ref readonly IndicesTriplet indicesTriplet = ref Triangles[i];
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
                        ref readonly IndicesTriplet indicesTriplet = ref Triangles[i];
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

        private bool ShouldSplitNode(in GpuBlasNode parentNode, out int splitAxis, out float splitPos, out float costIfSplit)
        {
            splitAxis = 0;
            splitPos = 0.0f;
            costIfSplit = float.MaxValue;
            if (parentNode.TriCount <= 1)
            {
                return false;
            }

            Box areaForSplits = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = GetTriangle(Triangles[parentNode.TriStartOrChild + i]);

                Vector3 centroid = (tri.Position0 + tri.Position1 + tri.Position2) / 3.0f;
                areaForSplits.GrowToFit(centroid);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                // We already know splitting is not worth it in this case and it avoids edge cases
                float minMaxLength = MathF.Abs(areaForSplits.Max[axis] - areaForSplits.Min[axis]);
                if (minMaxLength < 0.0001f)
                {
                    continue;
                }
                
                Span<Bin> bins = stackalloc Bin[SAH_SAMPLES + 1];
                bins.Fill(new Bin());
                for (int i = 0; i < parentNode.TriCount; i++)
                {
                    Triangle tri = GetTriangle(Triangles[parentNode.TriStartOrChild + i]);
                    float triSplitPos = (tri.Position0[axis] + tri.Position1[axis] + tri.Position2[axis]) / 3.0f;

                    float mapped = MyMath.MapToZeroOne(triSplitPos, areaForSplits.Min[axis], areaForSplits.Max[axis]);
                    int quantizePos = Math.Min((int)(mapped * bins.Length), bins.Length - 1);

                    bins[quantizePos].TriangleCount++;
                    bins[quantizePos].TriangleBounds.GrowToFit(tri);
                }

                Span<Box> rightSplitsBoxes = stackalloc Box[bins.Length - 1];
                rightSplitsBoxes[rightSplitsBoxes.Length - 1] = bins[bins.Length - 1].TriangleBounds;
                for (int i = rightSplitsBoxes.Length - 2; i >= 0; i--)
                {
                    rightSplitsBoxes[i] = bins[i + 1].TriangleBounds;
                    rightSplitsBoxes[i].GrowToFit(rightSplitsBoxes[i + 1]);
                }

                Bin leftSplit = new Bin();
                for (int i = 0; i < SAH_SAMPLES; i++)
                {
                    if (bins[i].TriangleCount > 0)
                    {
                        leftSplit.TriangleCount += bins[i].TriangleCount;
                        leftSplit.TriangleBounds.GrowToFit(bins[i].TriangleBounds);
                    }

                    Bin rightSplit = new Bin();
                    rightSplit.TriangleCount = parentNode.TriCount - leftSplit.TriangleCount;
                    rightSplit.TriangleBounds = rightSplitsBoxes[i];

                    // Implementation of "Surface Area Heuristic" described in https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
                    float areaParent = MyMath.Area(parentNode.Max - parentNode.Min);
                    float probHitLeftChild = leftSplit.TriangleBounds.Area() / areaParent;
                    float probHitRightChild = rightSplit.TriangleBounds.Area() / areaParent;

                    // Estimates cost of hitting parentNode if it was split at the evaluated split position
                    // The full "Surface Area Heuristic" is recurisve, but in practive we assume
                    // the resulting child nodes are leafs
                    float surfaceAreaHeuristic = CostInternalNode(
                        probHitLeftChild,
                        probHitRightChild,
                        CostLeafNode(leftSplit.TriangleCount),
                        CostLeafNode(rightSplit.TriangleCount)
                    );

                    if (surfaceAreaHeuristic < costIfSplit)
                    {
                        float scale = (areaForSplits.Max[axis] - areaForSplits.Min[axis]) / (SAH_SAMPLES + 1);
                        float currentSplitPos = areaForSplits.Min[axis] + (i + 1) * scale;

                        splitPos = currentSplitPos;
                        splitAxis = axis;
                        costIfSplit = surfaceAreaHeuristic;
                    }
                }
            }

            float costIfNotSplit = CostLeafNode(parentNode.TriCount);
            bool splittingIsWorthIt = costIfSplit < costIfNotSplit;

            return splittingIsWorthIt;
        }
        
        public void UpdateNodeBounds(ref GpuBlasNode node)
        {
            Box box = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (uint i = node.TriStartOrChild; i < node.TriStartOrChild + node.TriCount; i++)
            {
                Triangle tri = GetTriangle(Triangles[i]);
                box.GrowToFit(tri);
            }
            node.Min = box.Min;
            node.Max = box.Max;
        }

        public float ComputeGlobalCost(in GpuBlasNode parentNode)
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

            float cost = CostInternalNode(probHitLeftChild, probHitRightChild, ComputeGlobalCost(leftChild), ComputeGlobalCost(rightChild));

            return cost;
        }

        public Triangle GetTriangle(in IndicesTriplet indices)
        {
            ref readonly Vector3 p0 = ref VertexPositions[indices.X];
            ref readonly Vector3 p1 = ref VertexPositions[indices.Y];
            ref readonly Vector3 p2 = ref VertexPositions[indices.Z];

            return new Triangle() { Position0 = p0, Position1 = p1, Position2 = p2 };
        }
        
        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild)
        {
            return TRAVERSAL_INTERSECT_COST + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
        }
        
        private static float CostLeafNode(uint numTriangles)
        {
            return numTriangles * TRIANGLE_INTERSECT_COST;
        }
    }
}