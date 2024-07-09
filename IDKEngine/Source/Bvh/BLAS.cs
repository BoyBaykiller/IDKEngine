using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public partial class BLAS
    {
        public const int SAH_BINS = 16;
        public const int MAX_LEAF_TRIANGLE_COUNT = 8;
        public const float TRIANGLE_INTERSECT_COST = 1.1f;
        public const float TRAVERSAL_COST = 1.0f;

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
            public Box TriangleBounds = Box.Empty();
            public uint TriangleCount;

            public Bin()
            {
            }
        }

        public ref readonly GpuBlasNode Root => ref Nodes[0];
        public int TriangleCount => TriangleIndices.Length;

        public int MaxTreeDepth { get; private set; }
        public GpuBlasNode[] Nodes;
        public IndicesTriplet[] TriangleIndices;

        private readonly Vector3[] vertexPositions;
        private int unpaddedNodesCount;
        public BLAS(Vector3[] vertexPositions, ReadOnlySpan<uint> vertexIndices, in BBG.DrawElementsIndirectCommand geometryInfo)
        {
            this.vertexPositions = vertexPositions;

            TriangleIndices = new IndicesTriplet[geometryInfo.IndexCount / 3];
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
            rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount));

            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 0;
            while (stackPtr > 0)
            {
                ref GpuBlasNode parentNode = ref Nodes[stack[--stackPtr]];
                if (!TrySplit(parentNode, out int partitonPivot))
                {
                    continue;
                }

                GpuBlasNode newLeftNode = new GpuBlasNode();
                newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                newLeftNode.TriCount = (uint)(partitonPivot - newLeftNode.TriStartOrChild);
                newLeftNode.SetBounds(ComputeBoundingBox(newLeftNode.TriStartOrChild, newLeftNode.TriCount));

                GpuBlasNode newRightNode = new GpuBlasNode();
                newRightNode.TriStartOrChild = (uint)partitonPivot;
                newRightNode.TriCount = parentNode.TriCount - newLeftNode.TriCount;
                newRightNode.SetBounds(ComputeBoundingBox(newRightNode.TriStartOrChild, newRightNode.TriCount));

                int leftNodeId = nodesUsed + 0;
                int rightNodeId = nodesUsed + 1;

                Nodes[leftNodeId] = newLeftNode;
                Nodes[rightNodeId] = newRightNode;

                parentNode.TriStartOrChild = (uint)leftNodeId;
                parentNode.TriCount = 0;
                nodesUsed += 2;

                // Processing the child with smaller area first somehow makes reinsertion optimization
                // more effective and producer trees with smaller depth
                //if (newLeftNode.HalfArea() > newRightNode.HalfArea())
                //{
                //    MathHelper.Swap(ref leftNodeId, ref rightNodeId);
                //}

                stack[stackPtr++] = leftNodeId;
                stack[stackPtr++] = rightNodeId;
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
            }

            Array.Resize(ref Nodes, nodesUsed);
            MaxTreeDepth = ComputeTreeDepth(Nodes);
        }

        public void Optimize(in OptimizationSettings settings)
        {
            if (Nodes.Length <= 3)
            {
                return;
            }
            ReinsertionOptimizer.Optimize(this, settings);
        }

        public bool Intersect(in Ray ray, out RayHitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = tMaxDist;

            Span<uint> stack = stackalloc uint[MaxTreeDepth];
            int stackPtr = 0;
            uint stackTop = 1;

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
            Span<uint> stack = stackalloc uint[MaxTreeDepth];
            int stackPtr = 0;
            uint stackTop = 1;

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

        private bool TrySplit(in GpuBlasNode parentNode, out int pivot)
        {
            pivot = 0;
            if (parentNode.TriCount <= 1)
            {
                return false;
            }

            Box areaForSplits = Box.Empty();
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[parentNode.TriStartOrChild + i]);

                Vector3 centroid = (tri.Position0 + tri.Position1 + tri.Position2) / 3.0f;
                areaForSplits.GrowToFit(centroid);
            }

            if (areaForSplits.HalfArea() == 0.0f)
            {
                return false;
            }

            int splitAxis = 0;
            float splitPos = 0.0f;
            float costIfSplit = float.MaxValue;
            Span<Bin> bins = stackalloc Bin[SAH_BINS];
            Span<Box> rightSplitsBoxes = stackalloc Box[bins.Length - 1];
            for (int axis = 0; axis < 3; axis++)
            {
                // We already know splitting is not worth it in this case and it avoids edge cases
                float minMaxLength = MathF.Abs(areaForSplits.Max[axis] - areaForSplits.Min[axis]);
                if (minMaxLength == 0.0f)
                {
                    continue;
                }

                bins.Fill(new Bin());
                for (int i = 0; i < parentNode.TriCount; i++)
                {
                    Triangle tri = GetTriangle(TriangleIndices[parentNode.TriStartOrChild + i]);
                    float triSplitPos = (tri.Position0[axis] + tri.Position1[axis] + tri.Position2[axis]) / 3.0f;

                    float mapped = MyMath.MapToZeroOne(triSplitPos, areaForSplits.Min[axis], areaForSplits.Max[axis]);
                    int quantizePos = Math.Min((int)(mapped * bins.Length), bins.Length - 1);

                    bins[quantizePos].TriangleCount++;
                    bins[quantizePos].TriangleBounds.GrowToFit(tri);
                }

                rightSplitsBoxes[rightSplitsBoxes.Length - 1] = bins[bins.Length - 1].TriangleBounds;
                for (int i = rightSplitsBoxes.Length - 2; i >= 0; i--)
                {
                    rightSplitsBoxes[i] = bins[i + 1].TriangleBounds;
                    rightSplitsBoxes[i].GrowToFit(rightSplitsBoxes[i + 1]);
                }

                Bin leftSplit = new Bin();
                for (int i = 0; i < bins.Length - 1; i++)
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
                    float areaParent = parentNode.HalfArea();
                    float probHitLeftChild = leftSplit.TriangleBounds.HalfArea() / areaParent;
                    float probHitRightChild = rightSplit.TriangleBounds.HalfArea() / areaParent;

                    // Estimates cost of hitting parentNode if it was split at the evaluated split position
                    // The full "Surface Area Heuristic" is recurisve, but in practice we assume
                    // the resulting child nodes are leafs
                    float surfaceAreaHeuristic = CostInternalNode(
                        probHitLeftChild,
                        probHitRightChild,
                        CostLeafNode(leftSplit.TriangleCount),
                        CostLeafNode(rightSplit.TriangleCount)
                    );

                    if (surfaceAreaHeuristic < costIfSplit)
                    {
                        float scale = (areaForSplits.Max[axis] - areaForSplits.Min[axis]) / bins.Length;
                        float currentSplitPos = areaForSplits.Min[axis] + (i + 1) * scale;

                        splitPos = currentSplitPos;
                        splitAxis = axis;
                        costIfSplit = surfaceAreaHeuristic;
                    }
                }
            }


            float costIfNotSplit = CostLeafNode(parentNode.TriCount);
            if (costIfSplit >= costIfNotSplit)
            {
                if (parentNode.TriCount <= MAX_LEAF_TRIANGLE_COUNT)
                {
                    return false;
                }

                pivot = FallbackSplit(parentNode);
                return true;
            }
            
            int start = (int)parentNode.TriStartOrChild;
            int end = start + (int)parentNode.TriCount;
            pivot = Helper.Partition(TriangleIndices, start, end, (in IndicesTriplet triangleIndices) =>
            {
                Triangle tri = GetTriangle(triangleIndices);
                float posOnSplitAxis = (tri.Position0[splitAxis] + tri.Position1[splitAxis] + tri.Position2[splitAxis]) / 3.0f;
                return posOnSplitAxis < splitPos;
            });
            if (pivot == start || pivot == end)
            {
                pivot = FallbackSplit(parentNode);
                return true;
            }

            return true;
        }

        private int FallbackSplit(in GpuBlasNode parentNode)
        {
            // As a fallback we sort all triangles on the largest axis based on centroids and split in the middle
            Vector3 size = parentNode.Max - parentNode.Min;
            int largestAxis = size.Y > size.X ? 1 : 0;
            largestAxis = size.Z > size[largestAxis] ? 2 : largestAxis;

            int start = (int)parentNode.TriStartOrChild;
            int end = start + (int)parentNode.TriCount;
            MemoryExtensions.Sort(new Span<IndicesTriplet>(TriangleIndices, start, end - start), (IndicesTriplet a, IndicesTriplet b) =>
            {
                Triangle triA = GetTriangle(a);
                float posOnSplitAxisA = (triA.Position0[largestAxis] + triA.Position1[largestAxis] + triA.Position2[largestAxis]) / 3.0f;

                Triangle triB = GetTriangle(b);
                float posOnSplitAxisB = (triB.Position0[largestAxis] + triB.Position1[largestAxis] + triB.Position2[largestAxis]) / 3.0f;

                if (posOnSplitAxisA > posOnSplitAxisB) return 1;
                if (posOnSplitAxisA == posOnSplitAxisB) return 0;
                return -1;
            });

            int pivot = (start + end + 1) / 2;
            return pivot;
        }

        public Box ComputeBoundingBox(uint start, uint count)
        {
            Box box = Box.Empty();
            for (uint i = start; i < start + count; i++)
            {
                Triangle tri = GetTriangle(TriangleIndices[i]);
                box.GrowToFit(tri);
            }
            return box;
        }

        public float ComputeGlobalCost(in GpuBlasNode parentNode)
        {
            if (parentNode.IsLeaf)
            {
                return CostLeafNode(parentNode.TriCount);
            }

            ref readonly GpuBlasNode leftChild = ref Nodes[parentNode.TriStartOrChild];
            ref readonly GpuBlasNode rightChild = ref Nodes[parentNode.TriStartOrChild + 1];

            float areaParent = parentNode.HalfArea();
            float probHitLeftChild = leftChild.HalfArea() / areaParent;
            float probHitRightChild = rightChild.HalfArea() / areaParent;

            float cost = CostInternalNode(probHitLeftChild, probHitRightChild, ComputeGlobalCost(leftChild), ComputeGlobalCost(rightChild));

            if (areaParent == 0.0f)
            {
                // TODO: How?
                //System.Diagnostics.Debugger.Break();
            }

            return cost;
        }

        public Triangle GetTriangle(in IndicesTriplet indices)
        {
            ref readonly Vector3 p0 = ref vertexPositions[indices.X];
            ref readonly Vector3 p1 = ref vertexPositions[indices.Y];
            ref readonly Vector3 p2 = ref vertexPositions[indices.Z];

            return new Triangle() { Position0 = p0, Position1 = p1, Position2 = p2 };
        }

        public void Refit()
        {
            for (int i = unpaddedNodesCount - 1; i >= 0; i--)
            {
                ref GpuBlasNode parent = ref Nodes[i];
                if (parent.IsLeaf)
                {
                    parent.SetBounds(ComputeBoundingBox(parent.TriStartOrChild, parent.TriCount));
                    continue;
                }

                ref readonly GpuBlasNode leftChild = ref Nodes[parent.TriStartOrChild];
                ref readonly GpuBlasNode rightChild = ref Nodes[parent.TriStartOrChild + 1];

                Box mergedBox = Conversions.ToBox(leftChild);
                mergedBox.GrowToFit(rightChild.Min);
                mergedBox.GrowToFit(rightChild.Max);

                parent.SetBounds(mergedBox);
            }
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild)
        {
            return TRAVERSAL_COST + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
        }
        
        private static float CostLeafNode(uint numTriangles)
        {
            return numTriangles * TRIANGLE_INTERSECT_COST;
        }

        public static bool IsLeftSibling(int nodeId)
        {
            return nodeId % 2 == 1;
        }
        
        public static int GetSiblingId(int nodeId)
        {
            return IsLeftSibling(nodeId) ? nodeId + 1 : nodeId - 1;
        }

        public static int GetLeftSiblingId(int nodeId)
        {
            return IsLeftSibling(nodeId) ? nodeId : nodeId - 1;
        }

        private static int ComputeTreeDepth(ReadOnlySpan<GpuBlasNode> nodes)
        {
            int treeDepth = 0;
            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 1;

            while (stackPtr > 0)
            {
                treeDepth = Math.Max(stackPtr, treeDepth);

                int stackTop = stack[--stackPtr];
                ref readonly GpuBlasNode leftChild = ref nodes[stackTop];
                ref readonly GpuBlasNode rightChild = ref nodes[stackTop + 1];

                if (!leftChild.IsLeaf)
                {
                    stack[stackPtr++] = (int)leftChild.TriStartOrChild;
                }
                if (!rightChild.IsLeaf)
                {
                    stack[stackPtr++] = (int)rightChild.TriStartOrChild;
                }
            }

            return treeDepth;
        }
    }
}