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
    public static class BLAS
    {
        public record struct BuildSettings
        {
            public int MinLeafTriangleCount = 1;
            public int MaxLeafTriangleCount = 8;
            public float TriangleCost = 1.1f;
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

        private record struct BuildData
        {
            public float[] RightCostsAccum;
            public BitArray TriMarks;
            public Box[] TriBounds;
            public TriAxesSorted TrisAxesSorted;
        }

        public static int Build(ref BuildResult blas, in Geometry geometry, in BuildSettings settings)
        {
            BuildData buildData = GetBuildData(geometry);

            int nodesUsed = 0;

            ref GpuBlasNode rootNode = ref blas.Nodes[nodesUsed++];
            rootNode.TriStartOrChild = 0;
            rootNode.TriCount = geometry.TriangleCount;
            rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount, buildData));

            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 0;
            while (stackPtr > 0)
            {
                ref GpuBlasNode parentNode = ref blas.Nodes[stack[--stackPtr]];

                if (TrySplit(parentNode, buildData, settings) is int partitonPivot)
                {
                    GpuBlasNode newLeftNode = new GpuBlasNode();
                    newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                    newLeftNode.TriCount = partitonPivot - newLeftNode.TriStartOrChild;
                    newLeftNode.SetBounds(ComputeBoundingBox(newLeftNode.TriStartOrChild, newLeftNode.TriCount, buildData));

                    GpuBlasNode newRightNode = new GpuBlasNode();
                    newRightNode.TriStartOrChild = partitonPivot;
                    newRightNode.TriCount = parentNode.TriCount - newLeftNode.TriCount;
                    newRightNode.SetBounds(ComputeBoundingBox(newRightNode.TriStartOrChild, newRightNode.TriCount, buildData));

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

            IndicesTriplet[] triangles = new IndicesTriplet[geometry.TriangleCount];
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                triangles[i] = geometry.Triangles[buildData.TrisAxesSorted[0][i]];
            }
            triangles.CopyTo(geometry.Triangles);

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

        public delegate bool FuncIntersectLeafNode(in IndicesTriplet leafNodeTriangle);
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
                return CostLeafNode(parentNode.TriCount, settings.TriangleCost);
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

        private static int? TrySplit(in GpuBlasNode parentNode, BuildData buildData, in BuildSettings settings)
        {
            if (parentNode.TriCount <= settings.MinLeafTriangleCount)
            {
                return null;
            }

            int triStart = parentNode.TriStartOrChild;
            int triEnd = triStart + parentNode.TriCount;
            int triPivot = 0;
            int splitAxis = 0;

            float notSplitCost = CostLeafNode(parentNode.TriCount, settings.TriangleCost);
            float splitCost = notSplitCost;

            for (int axis = 0; axis < 3; axis++)
            {
                Box rightBoxAccum = Box.Empty();
                int firstRightTri = triStart + 1;
                for (int i = triEnd - 1; i >= firstRightTri; i--)
                {
                    rightBoxAccum.GrowToFit(buildData.TriBounds[buildData.TrisAxesSorted[axis][i]]);

                    int triCount = triEnd - i;
                    float areaParent = parentNode.HalfArea();
                    float probHitRightChild = rightBoxAccum.HalfArea() / areaParent;
                    float rightCost = probHitRightChild * CostLeafNode(triCount, settings.TriangleCost);

                    buildData.RightCostsAccum[i] = rightCost;

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
                    leftBoxAccum.GrowToFit(buildData.TriBounds[buildData.TrisAxesSorted[axis][i]]);
                }
                for (int i = firstRightTri - 1; i < triEnd - 1; i++)
                {
                    leftBoxAccum.GrowToFit(buildData.TriBounds[buildData.TrisAxesSorted[axis][i]]);

                    // Implementation of "Surface Area Heuristic" described in "Spatial Splits in Bounding Volume Hierarchies"
                    // https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
                    int triIndex = i + 1;
                    int triCount = triIndex - triStart;
                    float areaParent = parentNode.HalfArea();
                    float probHitLeftChild = leftBoxAccum.HalfArea() / areaParent;

                    float leftCost = probHitLeftChild * CostLeafNode(triCount, settings.TriangleCost);
                    float rightCost = buildData.RightCostsAccum[i + 1];

                    // Estimates cost of hitting parentNode if it was split at the evaluated split position.
                    // The full "Surface Area Heuristic" is recursive, but in practice we assume
                    // the resulting child nodes are leafs. This the greedy SAH approach
                    float surfaceAreaHeuristic = CostInternalNode(leftCost, rightCost, settings.TraversalCost);

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
            // Then the other two axes have their triangles partioned such that all marked triangles precede the others.
            // The partitioning is stable so the triangles stay sorted otherwise which is crucial

            for (int i = triStart; i < triPivot; i++)
            {
                buildData.TriMarks[buildData.TrisAxesSorted[splitAxis][i]] = true;
            }
            for (int i = triPivot; i < triEnd; i++)
            {
                buildData.TriMarks[buildData.TrisAxesSorted[splitAxis][i]] = false;
            }

            int[] partitionAux = new int[triEnd - triStart];
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == splitAxis)
                {
                    continue;
                }

                Algorithms.StablePartition(buildData.TrisAxesSorted[axis].AsSpan(triStart, triEnd - triStart), partitionAux, (in int triId) =>
                {
                    return buildData.TriMarks[triId];
                });
            }

            return triPivot;
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

        private static Box ComputeBoundingBox(int start, int count, in BuildData buildData)
        {
            Box box = Box.Empty();
            for (int i = start; i < start + count; i++)
            {
                box.GrowToFit(buildData.TriBounds[buildData.TrisAxesSorted[0][i]]);
            }
            return box;
        }

        private static BuildData GetBuildData(in Geometry geometry)
        {
            BuildData buildData = new BuildData();
            buildData.TriMarks = new BitArray(geometry.TriangleCount, false);
            buildData.TriBounds = new Box[geometry.TriangleCount];
            buildData.RightCostsAccum = new float[geometry.TriangleCount];
            buildData.TrisAxesSorted = new TriAxesSorted();

            TriCentroids triCentroids = new TriCentroids();

            for (int axis = 0; axis < 3; axis++)
            {
                triCentroids[axis] = new float[geometry.TriangleCount];
                buildData.TrisAxesSorted[axis] = new int[geometry.TriangleCount];
            }

            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle tri = geometry.GetTriangle(i);

                Vector3 centroid = tri.Centroid;
                triCentroids[0][i] = centroid.X;
                triCentroids[1][i] = centroid.Y;
                triCentroids[2][i] = centroid.Z;

                buildData.TriBounds[i] = Box.From(tri);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                Span<int> tris = buildData.TrisAxesSorted[axis];

                Helper.FillIncreasing(tris);

                tris.Sort((int a, int b) =>
                {
                    float centroidA = triCentroids[axis][a];
                    float centroidB = triCentroids[axis][b];

                    if (centroidA > centroidB) return 1;
                    if (centroidA == centroidB) return 0;
                    return -1;
                });
            }

            return buildData;
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild, float traversalCost)
        {
            return traversalCost + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
        }

        private static float CostInternalNode(float costLeft, float costRight, float traversalCost)
        {
            return traversalCost + costLeft + costRight;
        }

        private static float CostLeafNode(int numTriangles, float triangleCost)
        {
            return numTriangles * triangleCost;
        }

        [InlineArray(3)]
        private struct TriAxesSorted
        {
            private int[] _element;
        }

        [InlineArray(3)]
        private struct TriCentroids
        {
            private float[] _element;
        }
    }
}