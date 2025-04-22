using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    /// <summary>
    /// Implementation of "Sweep SAH" in "Bonsai: Rapid Bounding Volume Hierarchy Generation using Mini Trees"
    /// https://jcgt.org/published/0004/03/02/paper-lowres.pdf
    /// </summary>
    public static unsafe class BLAS
    {
        public record struct BuildSettings
        {
            public int MinLeafTriangleCount = 1;
            public int MaxLeafTriangleCount = 8;
            public float TriangleCost = 1.1f;

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
            public readonly int TriangleCount => Triangles.Length;

            public Span<Vector3> VertexPositions;
            public Span<GpuIndicesTriplet> Triangles;

            public Geometry(Span<GpuIndicesTriplet> triangles, Span<Vector3> vertexPositions)
            {
                Triangles = triangles;
                VertexPositions = vertexPositions;
            }

            public readonly Triangle GetTriangle(int index)
            {
                return GetTriangle(Triangles[index]);
            }

            public readonly Triangle GetTriangle(in GpuIndicesTriplet indices)
            {
                ref readonly Vector3 p0 = ref VertexPositions[indices.X];
                ref readonly Vector3 p1 = ref VertexPositions[indices.Y];
                ref readonly Vector3 p2 = ref VertexPositions[indices.Z];

                return new Triangle(p0, p1, p2);
            }
        }

        public record struct RayHitInfo
        {
            public GpuIndicesTriplet TriangleIndices;
            public Vector3 Bary;
            public float T;
        }

        public record struct BuildData
        {
            public readonly int TriangleCount => TriBounds.Length;

            /// <summary>
            /// There are 3 arrays which store triangle indices sorted by position on each axis respectively.
            /// For the purpose of accessing triangle indices from a leaf-node range we can just return any axis
            /// </summary>
            public readonly Span<int> TriangleIndices => TrisAxesSorted[0];

            public float[] RightCostsAccum;
            public BitArray TriMarks;
            public Box[] TriBounds;
            public TriAxesSorted TrisAxesSorted;
        }

        public static BuildData GetBuildData(Box[] bounds)
        {
            int triangleCount = bounds.Length;

            BuildData buildData = new BuildData();
            buildData.TriMarks = new BitArray(triangleCount, false);
            buildData.TriBounds = bounds;
            buildData.RightCostsAccum = new float[triangleCount];
            buildData.TrisAxesSorted = new TriAxesSorted();
            buildData.TrisAxesSorted[0] = new int[triangleCount];
            buildData.TrisAxesSorted[1] = new int[triangleCount];
            buildData.TrisAxesSorted[2] = new int[triangleCount];

            for (int axis = 0; axis < 3; axis++)
            {
                Span<int> input = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum);
                Span<int> output = buildData.TrisAxesSorted[axis];

                Helper.FillIncreasing(input);
                Algorithms.RadixSort(input, output, (int index) =>
                {
                    float centerAxis = (bounds[index].Min[axis] + bounds[index].Max[axis]) * 0.5f;
                    return Algorithms.FloatToKey(centerAxis);
                });
            }

            return buildData;
        }

        public static int Build(ref BuildResult blas, in BuildData buildData, in BuildSettings settings)
        {
            int nodesUsed = 0;

            ref GpuBlasNode rootNode = ref blas.Nodes[nodesUsed++];
            rootNode.TriStartOrChild = 0;
            rootNode.TriCount = buildData.TriangleCount;
            rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount, buildData));

            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 0;
            while (stackPtr > 0)
            {
                ref GpuBlasNode parentNode = ref blas.Nodes[stack[--stackPtr]];

                if (TrySplit(parentNode, buildData, settings) is int partitionPivot)
                {
                    GpuBlasNode newLeftNode = new GpuBlasNode();
                    newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                    newLeftNode.TriCount = partitionPivot - newLeftNode.TriStartOrChild;
                    newLeftNode.SetBounds(ComputeBoundingBox(newLeftNode.TriStartOrChild, newLeftNode.TriCount, buildData));

                    GpuBlasNode newRightNode = new GpuBlasNode();
                    newRightNode.TriStartOrChild = partitionPivot;
                    newRightNode.TriCount = parentNode.TriCount - newLeftNode.TriCount;
                    newRightNode.SetBounds(ComputeBoundingBox(newRightNode.TriStartOrChild, newRightNode.TriCount, buildData));

                    int leftNodeId = nodesUsed + 0;
                    int rightNodeId = nodesUsed + 1;

                    blas.Nodes[leftNodeId] = newLeftNode;
                    blas.Nodes[rightNodeId] = newRightNode;

                    parentNode.TriStartOrChild = leftNodeId;
                    parentNode.TriCount = 0;
                    nodesUsed += 2;

                    stack[stackPtr++] = rightNodeId;
                    stack[stackPtr++] = leftNodeId;
                }
            }

            blas.UnpaddedNodesCount = nodesUsed;
            if (nodesUsed == 1)
            {
                blas.UnpaddedNodesCount++;

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

        public static void Refit(in BuildResult blas, in Geometry geometry)
        {
            for (int i = blas.UnpaddedNodesCount - 1; i >= 0; i--)
            {
                ref GpuBlasNode parent = ref blas.Nodes[i];
                if (parent.IsLeaf)
                {
                    parent.SetBounds(ComputeBoundingBox(parent.TriStartOrChild, parent.TriCount, geometry));
                    continue;
                }

                ref readonly GpuBlasNode leftChild = ref blas.Nodes[parent.TriStartOrChild];
                ref readonly GpuBlasNode rightChild = ref blas.Nodes[parent.TriStartOrChild + 1];

                Box mergedBox = Conversions.ToBox(leftChild);
                mergedBox.GrowToFit(Conversions.ToBox(rightChild));

                parent.SetBounds(mergedBox);
            }
        }

        public static void RefitFromNode(int nodeId, Span<GpuBlasNode> nodes, ReadOnlySpan<int> parentIds)
        {
            do
            {
                ref GpuBlasNode node = ref nodes[nodeId];
                if (!node.IsLeaf)
                {
                    ref readonly GpuBlasNode leftChild = ref nodes[node.TriStartOrChild];
                    ref readonly GpuBlasNode rightChild = ref nodes[node.TriStartOrChild + 1];

                    Box mergedBox = Conversions.ToBox(leftChild);
                    mergedBox.GrowToFit(Conversions.ToBox(rightChild));

                    node.SetBounds(mergedBox);
                }

                nodeId = parentIds[nodeId];
            } while (nodeId != -1);
        }

        public static float ComputeGlobalCost(in GpuBlasNode parentNode, ReadOnlySpan<GpuBlasNode> nodes, in BuildSettings settings)
        {
            if (parentNode.IsLeaf)
            {
                return CostLeafNode(parentNode.TriCount, settings.TriangleCost);
            }

            ref readonly GpuBlasNode leftChild = ref nodes[parentNode.TriStartOrChild];
            ref readonly GpuBlasNode rightChild = ref nodes[parentNode.TriStartOrChild + 1];

            float areaParent = parentNode.HalfArea();
            float probHitLeftChild = leftChild.HalfArea() / areaParent;
            float probHitRightChild = rightChild.HalfArea() / areaParent;

            float cost = CostInternalNode(probHitLeftChild, probHitRightChild, ComputeGlobalCost(leftChild, nodes, settings), ComputeGlobalCost(rightChild, nodes, settings));

            //if (parentNode.HalfArea() == 0.0f)
            //{
            //    // todo: this should not happen
            //    Console.WriteLine("dsad");
            //}

            return cost;
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
                        ref readonly GpuIndicesTriplet indicesTriplet = ref geometry.Triangles[i];
                        Triangle triangle = geometry.GetTriangle(indicesTriplet);

                        if (Intersections.RayVsTriangle(ray, triangle, out Vector3 bary, out float t) && t < hitInfo.T)
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

        public delegate bool FuncIntersectLeafNode(in GpuIndicesTriplet leafNodeTriangle);
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
                        ref readonly GpuIndicesTriplet indicesTriplet = ref geometry.Triangles[i];
                        if (intersectFunc(indicesTriplet))
                        {
                            return;
                        }
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

        public static GpuIndicesTriplet[] GetUnindexedTriangles(in BuildData buildData, in Geometry geometry)
        {
            GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[geometry.TriangleCount];
            for (int j = 0; j < triangles.Length; j++)
            {
                triangles[j] = geometry.Triangles[buildData.TriangleIndices[j]];
            }

            return triangles;
        }

        public static Box[] GetTriangleBounds(in Geometry geometry)
        {
            Box[] bounds = new Box[geometry.TriangleCount];

            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle tri = geometry.GetTriangle(i);
                bounds[i] = Box.From(tri);
            }

            return bounds;
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

        public static int GetUpperBoundNodes(int triangleCount)
        {
            return Math.Max(2 * triangleCount - 1, 3);
        }

        public static Box ComputeBoundingBox(int start, int count, in Geometry geometry)
        {
            Box box = Box.Empty();
            for (int i = start; i < start + count; i++)
            {
                Triangle tri = geometry.GetTriangle(i);
                box.GrowToFit(tri);
            }
            return box;
        }

        public static Box ComputeBoundingBox(int start, int count, in BuildData buildData)
        {
            Box box = Box.Empty();
            for (int i = start; i < start + count; i++)
            {
                box.GrowToFit(buildData.TriBounds[buildData.TriangleIndices[i]]);
            }
            return box;
        }

        private static int? TrySplit(in GpuBlasNode parentNode, in BuildData buildData, in BuildSettings settings)
        {
            if (parentNode.TriCount <= settings.MinLeafTriangleCount)
            {
                return null;
            }

            int triStart = parentNode.TriStartOrChild;
            int triEnd = triStart + parentNode.TriCount;
            float parentArea = parentNode.HalfArea();

            float notSplitCost = CostLeafNode(parentNode.TriCount, settings.TriangleCost);

            int triPivot = 0;
            int splitAxis = 0;
            float splitCost = notSplitCost;

            // Unfortunately we have to manually load the fields for best perf
            // as the JIT otherwise repeatedly loads them in the loop
            // https://github.com/dotnet/runtime/issues/113107
            Span<float> rightCostsAccum = buildData.RightCostsAccum;
            Span<Box> triBounds = buildData.TriBounds;
            BitArray triMarks = buildData.TriMarks;

            for (int axis = 0; axis < 3; axis++)
            {
                Box rightBoxAccum = Box.Empty();
                int firstRightTri = triStart + 1;

                Span<int> trisAxesSorted = buildData.TrisAxesSorted[axis];

                for (int i = triEnd - 1; i >= firstRightTri; i--)
                {
                    rightBoxAccum.GrowToFit(triBounds[trisAxesSorted[i]]);

                    int triCount = triEnd - i;
                    float probHitRightChild = rightBoxAccum.HalfArea() / parentArea;
                    float rightCost = probHitRightChild * CostLeafNode(triCount, settings.TriangleCost);

                    rightCostsAccum[i] = rightCost;

                    if (rightCost >= splitCost)
                    {
                        // Don't need to consider split positions beyond this point as cost is already greater and will only get more
                        firstRightTri = i + 1;
                        break;
                    }
                }

                Box leftBoxAccum = Box.Empty();
                for (int i = triStart; i < firstRightTri - 1; i++)
                {
                    leftBoxAccum.GrowToFit(triBounds[trisAxesSorted[i]]);
                }
                for (int i = firstRightTri - 1; i < triEnd - 1; i++)
                {
                    leftBoxAccum.GrowToFit(triBounds[trisAxesSorted[i]]);

                    // Implementation of "Surface Area Heuristic" described in "Spatial Splits in Bounding Volume Hierarchies"
                    // https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
                    int triIndex = i + 1;
                    int triCount = triIndex - triStart;
                    float probHitLeftChild = leftBoxAccum.HalfArea() / parentArea;

                    float leftCost = probHitLeftChild * CostLeafNode(triCount, settings.TriangleCost);
                    float rightCost = rightCostsAccum[i + 1];

                    // Estimates cost of hitting parentNode if it was split at the evaluated split position.
                    // The full "Surface Area Heuristic" is recursive, but in practice we assume
                    // the resulting child nodes are leafs. This the greedy SAH approach
                    float surfaceAreaHeuristic = CostInternalNode(leftCost, rightCost);

                    if (surfaceAreaHeuristic < splitCost)
                    {
                        triPivot = triIndex;
                        splitAxis = axis;
                        splitCost = surfaceAreaHeuristic;
                    }
                    else if (leftCost >= splitCost)
                    {
                        break;
                    }
                }
            }

            if (splitCost >= notSplitCost)
            {
                if (parentNode.TriCount > settings.MaxLeafTriangleCount)
                {
                    // Simply split the triangles equally in this case.
                    // Having a maximum triangle count regardless of what SAH says can be benefical in some scenes

                    Vector3 size = parentNode.Max - parentNode.Min;
                    int largestAxis = size.Y > size.X ? 1 : 0;
                    largestAxis = size.Z > size[largestAxis] ? 2 : largestAxis;

                    splitAxis = largestAxis;
                    triPivot = (triStart + triEnd + 1) / 2;
                }
                else
                {
                    return null;
                }
            }

            // We found a split axis where the triangles are partitioned into a left and right set.
            // Now, the other two axes also need to have the same triangles in their sets respectively.
            // To do that we mark every triangle on the left side of the split axis.
            // Then the other two axes have their triangles partitioned such that all marked triangles precede the others.
            // The partitioning is stable so the triangles stay sorted otherwise which is crucial

            Span<int> trisAxesSortedSplitAxis = buildData.TrisAxesSorted[splitAxis];
            for (int i = triStart; i < triPivot; i++)
            {
                triMarks[trisAxesSortedSplitAxis[i]] = true;
            }
            for (int i = triPivot; i < triEnd; i++)
            {
                triMarks[trisAxesSortedSplitAxis[i]] = false;
            }

            Span<int> partitionAux = Helper.ReUseMemory<float, int>(rightCostsAccum, triEnd - triPivot);
            Algorithms.StablePartition(buildData.TrisAxesSorted[(splitAxis + 1) % 3], triStart, triEnd, partitionAux, buildData.TriMarks);
            Algorithms.StablePartition(buildData.TrisAxesSorted[(splitAxis + 2) % 3], triStart, triEnd, partitionAux, buildData.TriMarks);

            return triPivot;
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild)
        {
            const float traversalCost = 1.0f;
            return traversalCost + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
        }

        private static float CostInternalNode(float costLeft, float costRight)
        {
            const float traversalCost = 1.0f;
            return traversalCost + costLeft + costRight;
        }

        private static float CostLeafNode(int numTriangles, float triangleCost)
        {
            return numTriangles * triangleCost;
        }

        [InlineArray(3)]
        public struct TriAxesSorted
        {
            private int[] _element;
        }
    }
}