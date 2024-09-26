#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type. Workarround to C# Lambda-Functions skill issue
using System;
using System.Runtime.InteropServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public static partial class BLAS
    {
        public record struct BuildSettings
        {
            public int SahBins = 16;
            public int MaxLeafTriangleCount = 8;
            public float TriangleIntersectCost = 1.1f;
            public float TraversalCost = 1.0f;

            public BuildSettings()
            {
            }
        }

        public ref struct BuildResult
        {
            public ref GpuBlasNode Root => ref Nodes[0];

            public Span<GpuBlasNode> Nodes;
            public int MaxTreeDepth;
            public int UnpaddedNodesCount;

            public BuildResult(Span<GpuBlasNode> nodes)
            {
                Nodes = nodes;
            }
        }

        public ref struct Geometry
        {
            public int TriangleCount => Triangles.Length;

            public Span<Vector3> VertexPositions;
            public Span<IndicesTriplet> Triangles;

            public Geometry(Span<Vector3> vertexPositions, Span<IndicesTriplet> triangles)
            {
                VertexPositions = vertexPositions;
                Triangles = triangles;
            }

            public Triangle GetTriangle(int index)
            {
                return GetTriangle(Triangles[index]);
            }

            public Triangle GetTriangle(in IndicesTriplet triangles)
            {
                ref readonly Vector3 p0 = ref VertexPositions[triangles.X];
                ref readonly Vector3 p1 = ref VertexPositions[triangles.Y];
                ref readonly Vector3 p2 = ref VertexPositions[triangles.Z];

                return new Triangle() { Position0 = p0, Position1 = p1, Position2 = p2 };
            }

            public static Triangle GetTriangle(in IndicesTriplet triangles, ReadOnlySpan<Vector3> vertexPositions)
            {
                ref readonly Vector3 p0 = ref vertexPositions[triangles.X];
                ref readonly Vector3 p1 = ref vertexPositions[triangles.Y];
                ref readonly Vector3 p2 = ref vertexPositions[triangles.Z];

                return new Triangle() { Position0 = p0, Position1 = p1, Position2 = p2 };
            }
        }

        public record struct IndicesTriplet
        {
            public int X;
            public int Y;
            public int Z;
        }

        public record struct RayHitInfo
        {
            public IndicesTriplet TriangleIndices;
            public Vector3 Bary;
            public float T;
        }

        public record struct Triangle
        {
            public Vector3 Position0;
            public Vector3 Position1;
            public Vector3 Position2;
        }

        private record struct Bin
        {
            public Box TriangleBounds = Box.Empty();
            public int TriangleCount;

            public Bin()
            {
            }
        }

        public static int Build(ref BuildResult blas, in Geometry geometry, in BuildSettings settings)
        {
            int nodesUsed = 0;

            ref GpuBlasNode rootNode = ref blas.Nodes[nodesUsed++];
            rootNode.TriStartOrChild = 0;
            rootNode.TriCount = geometry.TriangleCount;
            rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount, geometry));

            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 0;
            while (stackPtr > 0)
            {
                ref GpuBlasNode parentNode = ref blas.Nodes[stack[--stackPtr]];

                if (TrySplit(parentNode, geometry, settings) is int partitonPivot)
                {
                    GpuBlasNode newLeftNode = new GpuBlasNode();
                    newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                    newLeftNode.TriCount = partitonPivot - newLeftNode.TriStartOrChild;
                    newLeftNode.SetBounds(ComputeBoundingBox(newLeftNode.TriStartOrChild, newLeftNode.TriCount, geometry));

                    GpuBlasNode newRightNode = new GpuBlasNode();
                    newRightNode.TriStartOrChild = partitonPivot;
                    newRightNode.TriCount = parentNode.TriCount - newLeftNode.TriCount;
                    newRightNode.SetBounds(ComputeBoundingBox(newRightNode.TriStartOrChild, newRightNode.TriCount, geometry));

                    int leftNodeId = nodesUsed + 0;
                    int rightNodeId = nodesUsed + 1;

                    blas.Nodes[leftNodeId] = newLeftNode;
                    blas.Nodes[rightNodeId] = newRightNode;

                    parentNode.TriStartOrChild = leftNodeId;
                    parentNode.TriCount = 0;
                    nodesUsed += 2;

                    stack[stackPtr++] = leftNodeId;
                    stack[stackPtr++] = rightNodeId;
                }
            }

            blas.UnpaddedNodesCount = nodesUsed;
            if (nodesUsed == 1)
            {
                // Handle edge case of the root node being a leaf by creating an artificial child node
                blas.Nodes[1] = blas.Nodes[0];
                blas.Nodes[0].TriCount = 0;
                blas.Nodes[0].TriStartOrChild = 1;

                // Add an other dummy invisible node because the traversal algorithm always tests two nodes at once
                blas.Nodes[2] = new GpuBlasNode();
                blas.Nodes[2].Min = new Vector3(float.MinValue);
                blas.Nodes[2].Max = new Vector3(float.MinValue);
                blas.Nodes[2].TriCount = 1;

                nodesUsed = 3;
            }

            blas.MaxTreeDepth = ComputeTreeDepth(blas);

            return nodesUsed;
        }

        public static bool Intersect(
            in BuildResult blas,
            in Geometry geometry,
            in Ray ray, out RayHitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = tMaxDist;

            Span<int> stack = stackalloc int[blas.MaxTreeDepth];
            int stackPtr = 0;
            int stackTop = 1;

            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref blas.Nodes[stackTop];
                ref readonly GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];

                bool leftChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                int summedTriCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (summedTriCount > 0)
                {
                    int first = (leftChildHit && leftNode.IsLeaf) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    for (int i = first; i < first + summedTriCount; i++)
                    {
                        ref readonly IndicesTriplet indicesTriplet = ref geometry.Triangles[i];
                        Triangle triangle = geometry.GetTriangle(indicesTriplet);

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
        public static void Intersect(
            in BuildResult blas,
            in Geometry geometry,
            in Box box, FuncIntersectLeafNode intersectFunc)
        {
            Span<int> stack = stackalloc int[32];
            int stackPtr = 0;
            int stackTop = 1;

            while (true)
            {
                ref readonly GpuBlasNode leftNode = ref blas.Nodes[stackTop];
                ref readonly GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];
                bool leftChildHit = Intersections.BoxVsBox(Conversions.ToBox(leftNode), box);
                bool rightChildHit = Intersections.BoxVsBox(Conversions.ToBox(rightNode), box);

                int summedTriCount = (leftChildHit ? leftNode.TriCount : 0) + (rightChildHit ? rightNode.TriCount : 0);
                if (summedTriCount > 0)
                {
                    int first = (leftChildHit && leftNode.IsLeaf) ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    for (int i = first; i < first + summedTriCount; i++)
                    {
                        ref readonly IndicesTriplet indicesTriplet = ref geometry.Triangles[i];
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

        public static void Refit(in BuildResult buildResult, in Geometry geometry)
        {
            for (int i = buildResult.UnpaddedNodesCount - 1; i >= 0; i--)
            {
                ref GpuBlasNode parent = ref buildResult.Nodes[i];
                if (parent.IsLeaf)
                {
                    parent.SetBounds(ComputeBoundingBox(parent.TriStartOrChild, parent.TriCount, geometry));
                    continue;
                }

                ref readonly GpuBlasNode leftChild = ref buildResult.Nodes[parent.TriStartOrChild];
                ref readonly GpuBlasNode rightChild = ref buildResult.Nodes[parent.TriStartOrChild + 1];

                Box mergedBox = Conversions.ToBox(leftChild);
                mergedBox.GrowToFit(Conversions.ToBox(rightChild));

                parent.SetBounds(mergedBox);
            }
        }

        public static void RefitFromNode(int parentId, Span<GpuBlasNode> nodes, ReadOnlySpan<int> parentIds)
        {
            do
            {
                ref GpuBlasNode node = ref nodes[parentId];
                if (!node.IsLeaf)
                {
                    ref readonly GpuBlasNode leftChild = ref nodes[node.TriStartOrChild];
                    ref readonly GpuBlasNode rightChild = ref nodes[node.TriStartOrChild + 1];

                    Box mergedBox = Conversions.ToBox(leftChild);
                    mergedBox.GrowToFit(Conversions.ToBox(rightChild));

                    node.SetBounds(mergedBox);
                }

                parentId = parentIds[parentId];
            } while (parentId != -1);
        }

        public static float ComputeGlobalCost(in GpuBlasNode parentNode, ReadOnlySpan<GpuBlasNode> nodes, in BuildSettings settings)
        {
            if (parentNode.IsLeaf)
            {
                return CostLeafNode(parentNode.TriCount, settings.TriangleIntersectCost);
            }

            ref readonly GpuBlasNode leftChild = ref nodes[parentNode.TriStartOrChild];
            ref readonly GpuBlasNode rightChild = ref nodes[parentNode.TriStartOrChild + 1];

            float areaParent = parentNode.HalfArea();
            float probHitLeftChild = leftChild.HalfArea() / areaParent;
            float probHitRightChild = rightChild.HalfArea() / areaParent;

            float cost = CostInternalNode(probHitLeftChild, probHitRightChild, ComputeGlobalCost(leftChild, nodes, settings), ComputeGlobalCost(rightChild, nodes, settings), settings.TraversalCost);

            if (areaParent == 0.0f)
            {
                // TODO: How?
                //System.Diagnostics.Debugger.Break();
            }

            return cost;
        }

        public static int ComputeTreeDepth(in BuildResult blas)
        {
            int treeDepth = 0;
            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 1;

            while (stackPtr > 0)
            {
                treeDepth = Math.Max(stackPtr + 1, treeDepth);

                int stackTop = stack[--stackPtr];
                ref readonly GpuBlasNode leftChild = ref blas.Nodes[stackTop];
                ref readonly GpuBlasNode rightChild = ref blas.Nodes[stackTop + 1];

                if (!leftChild.IsLeaf)
                {
                    stack[stackPtr++] = leftChild.TriStartOrChild;
                }
                if (!rightChild.IsLeaf)
                {
                    stack[stackPtr++] = rightChild.TriStartOrChild;
                }
            }

            return treeDepth;
        }

        public static int[] GetParentIndices(in BuildResult blas)
        {
            int[] parents = new int[blas.Nodes.Length];
            parents[0] = -1;

            for (int i = 1; i < blas.Nodes.Length; i++)
            {
                ref readonly GpuBlasNode node = ref blas.Nodes[i];
                if (!node.IsLeaf)
                {
                    parents[node.TriStartOrChild + 0] = i;
                    parents[node.TriStartOrChild + 1] = i;
                }
            }

            return parents;
        }

        public static int[] GetLeafIndices(in BuildResult blas)
        {
            ReadOnlySpan<GpuBlasNode> nodes = MemoryMarshal.CreateReadOnlySpan(in blas.Nodes[0], blas.UnpaddedNodesCount);

            int[] indices = new int[nodes.Length / 2 + 1];
            int counter = 0;
            for (int i = 1; i < nodes.Length; i++)
            {
                if (nodes[i].IsLeaf)
                {
                    indices[counter++] = i;
                }
            }
            Array.Resize(ref indices, counter);

            return indices;
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

        public static GpuBlasNode[] AllocateUpperBoundNodes(int triangleCount)
        {
            return new GpuBlasNode[Math.Max(2 * triangleCount - 1, 3)];
        }

        private static unsafe int? TrySplit(in GpuBlasNode parentNode, Geometry geometry, in BuildSettings settings)
        {
            if (parentNode.TriCount <= 1)
            {
                return null;
            }

            Box areaForSplits = Box.Empty();
            for (int i = 0; i < parentNode.TriCount; i++)
            {
                Triangle tri = geometry.GetTriangle(parentNode.TriStartOrChild + i);

                Vector3 centroid = (tri.Position0 + tri.Position1 + tri.Position2) / 3.0f;
                areaForSplits.GrowToFit(centroid);
            }

            if (areaForSplits.HalfArea() == 0.0f)
            {
                return null;
            }

            int splitAxis = 0;
            float splitPos = 0.0f;
            float costIfSplit = float.MaxValue;
            Span<Bin> bins = stackalloc Bin[settings.SahBins];
            Span<Box> rightSplitsBoxes = stackalloc Box[bins.Length - 1];
            for (int axis = 0; axis < 3; axis++)
            {
                float minMaxLength = MathF.Abs(areaForSplits.Max[axis] - areaForSplits.Min[axis]);
                if (minMaxLength == 0.0f)
                {
                    continue;
                }

                bins.Fill(new Bin());
                for (int i = 0; i < parentNode.TriCount; i++)
                {
                    Triangle tri = geometry.GetTriangle(parentNode.TriStartOrChild + i);
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
                        CostLeafNode(leftSplit.TriangleCount, settings.TriangleIntersectCost),
                        CostLeafNode(rightSplit.TriangleCount, settings.TriangleIntersectCost),
                        settings.TraversalCost
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

            float costIfNotSplit = CostLeafNode(parentNode.TriCount, settings.TriangleIntersectCost);
            if (costIfSplit >= costIfNotSplit)
            {
                if (parentNode.TriCount <= settings.MaxLeafTriangleCount)
                {
                    return null;
                }

                return MedianSplit(parentNode, geometry);
            }
            
            Geometry* geometryPtr = &geometry;
            int start = parentNode.TriStartOrChild;
            int end = start + parentNode.TriCount;
            int pivot = Algorithms.Partition(geometry.Triangles, start, end, (in IndicesTriplet triangleIndices) =>
            {
                Triangle tri = geometryPtr->GetTriangle(triangleIndices);
                float posOnSplitAxis = (tri.Position0[splitAxis] + tri.Position1[splitAxis] + tri.Position2[splitAxis]) / 3.0f;
                return posOnSplitAxis < splitPos;
            });

            if (pivot == start || pivot == end)
            {
                // All triangles ended up on the same side, just split at the median
                return MedianSplit(parentNode, geometry);
            }

            return pivot;
        }

        private static unsafe int MedianSplit(in GpuBlasNode parentNode, Geometry geometry)
        {
            // Sort all triangles on the largest axis based on centroids and split at the median (not the middle!)
            
            Vector3 size = parentNode.Max - parentNode.Min;
            int largestAxis = size.Y > size.X ? 1 : 0;
            largestAxis = size.Z > size[largestAxis] ? 2 : largestAxis;

            int start = parentNode.TriStartOrChild;
            int end = start + parentNode.TriCount;
            Geometry* geometryPtr = &geometry;
            MemoryExtensions.Sort(geometry.Triangles.GetSpan(start, end - start), (IndicesTriplet a, IndicesTriplet b) =>
            {
                Triangle triA = geometryPtr->GetTriangle(a);
                float posOnSplitAxisA = (triA.Position0[largestAxis] + triA.Position1[largestAxis] + triA.Position2[largestAxis]) / 3.0f;

                Triangle triB = geometryPtr->GetTriangle(b);
                float posOnSplitAxisB = (triB.Position0[largestAxis] + triB.Position1[largestAxis] + triB.Position2[largestAxis]) / 3.0f;

                if (posOnSplitAxisA > posOnSplitAxisB) return 1;
                if (posOnSplitAxisA == posOnSplitAxisB) return 0;
                return -1;
            });
            int pivot = (start + end + 1) / 2;

            return pivot;
        }

        private static Box ComputeBoundingBox(int start, int count, in Geometry geometry)
        {
            Box box = Box.Empty();
            for (int i = start; i < start + count; i++)
            {
                Triangle tri = geometry.GetTriangle(i);
                box.GrowToFit(tri);
            }
            return box;
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild, float traversalCost)
        {
            return traversalCost + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
        }
        
        private static float CostLeafNode(int numTriangles, float triangleCost)
        {
            return numTriangles * triangleCost;
        }
    }
}