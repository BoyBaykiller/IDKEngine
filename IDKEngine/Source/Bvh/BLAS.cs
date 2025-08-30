using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh;

/// <summary>
/// Implementation of "Sweep SAH" from "Bonsai: Rapid Bounding Volume Hierarchy Generation using Mini Trees"
/// https://jcgt.org/published/0004/03/02/paper-lowres.pdf
/// 
/// There are a few properties we should be aware of and maintain:
/// * The root must never be a leaf
/// * The left child of the root must be at index 1
/// * A leaf-pair always forms a continous range of geometry beginning left
/// * Straddling geometry of a leaf-pair can be shared (when Pre-Splitting is enabled)
/// </summary>
public static class BLAS
{
    // Do not change, instead modify TriangleCost
    public const float TRAVERSAL_COST = 1.0f;

    public record struct BuildSettings
    {
        public int StopSplittingThreshold = 1;
        public int MaxLeafTriangleCount = 8;
        public float TriangleCost = 1.1f;

        public BuildSettings()
        {
        }
    }

    public record struct Fragments
    {
        public readonly int Count => Bounds.Length;

        public Box[] Bounds;
        public int[] OriginalTriIds;
    }

    public ref struct BuildResult
    {
        public readonly ref GpuBlasNode Root => ref Nodes[0];

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
        public int TriangleId;
        public Vector3 Bary;
        public float T;
    }

    public record struct BuildData
    {
        /// <summary>
        /// There are 3 permutated arrays which store triangle indices sorted by position on each axis respectively.
        /// For the purpose of accessing triangle indices from a leaf-node range we can just return any axis
        /// </summary>
        public readonly Span<int> PermutatedFragmentIds => FragmentIdsSortedOnAxis[0];

        public float[] RightCostsAccum;
        public BitArray PartitionLeft;
        public Fragments Fragments;

        public FragmentsAxesSorted FragmentIdsSortedOnAxis;
    }

    private record struct ObjectSplit
    {
        public float NewCost;
        public int Axis;
        public int Pivot;
    }

    public static BuildData GetBuildData(Fragments fragments)
    {
        BuildData buildData = new BuildData();
        buildData.Fragments = fragments;
        buildData.PartitionLeft = new BitArray(fragments.Count, false);
        buildData.RightCostsAccum = new float[fragments.Count];
        buildData.FragmentIdsSortedOnAxis[0] = new int[fragments.Count];
        buildData.FragmentIdsSortedOnAxis[1] = new int[fragments.Count];
        buildData.FragmentIdsSortedOnAxis[2] = new int[fragments.Count];

        for (int axis = 0; axis < 3; axis++)
        {
            Span<int> input = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, fragments.Count);
            Span<int> output = buildData.FragmentIdsSortedOnAxis[axis];

            Helper.FillIncreasing(input);

            // We're loosing perf here compared to C++ because of indirect call on lambda func
            Algorithms.RadixSort(input, output, (int index) =>
            {
                float centerAxis = (fragments.Bounds[index].SimdMin[axis] + fragments.Bounds[index].SimdMax[axis]) * 0.5f;

                return Algorithms.FloatToKey(centerAxis);
            });
        }

        return buildData;
    }

    public static int Build(ref BuildResult blas, in BuildData buildData, BuildSettings settings)
    {
        int nodesUsed = 0;

        ref GpuBlasNode rootNode = ref blas.Nodes[nodesUsed++];
        rootNode.TriStartOrChild = 0;
        rootNode.TriCount = buildData.Fragments.Count;
        rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount, buildData));

        int stackPtr = 0;
        Span<int> stack = stackalloc int[64];
        stack[stackPtr++] = 0;
        while (stackPtr > 0)
        {
            ref GpuBlasNode parentNode = ref blas.Nodes[stack[--stackPtr]];

            if (TrySplit(parentNode, buildData, settings) is ObjectSplit objectSplit)
            {
                GpuBlasNode newLeftNode = new GpuBlasNode();
                newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                newLeftNode.TriCount = objectSplit.Pivot - newLeftNode.TriStartOrChild;
                newLeftNode.SetBounds(ComputeBoundingBox(newLeftNode.TriStartOrChild, newLeftNode.TriCount, buildData));

                GpuBlasNode newRightNode = new GpuBlasNode();
                newRightNode.TriStartOrChild = objectSplit.Pivot;
                newRightNode.TriCount = parentNode.TriEnd - newRightNode.TriStartOrChild;
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

            Box mergedBox = Box.From(Conversions.ToBox(leftChild), Conversions.ToBox(rightChild));
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

                Box mergedBox = Box.From(Conversions.ToBox(leftChild), Conversions.ToBox(rightChild));
                node.SetBounds(mergedBox);
            }

            nodeId = parentIds[nodeId];
        } while (nodeId != -1);
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

        if (!Intersections.RayVsBox(ray, Conversions.ToBox(blas.Root), out _, out _))
        {
            return false;
        }

        while (true)
        {
            ref readonly GpuBlasNode leftNode = ref blas.Nodes[stackTop];
            ref readonly GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];

            bool traverseLeft = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
            bool traverseRight = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

            System.Threading.Interlocked.Add(ref BVH.DebugStatistics.BoxIntersections, 2ul);

            bool intersectLeft = traverseLeft && leftNode.IsLeaf;
            bool intersectRight = traverseRight && rightNode.IsLeaf;
            if (intersectLeft || intersectRight)
            {
                int first = intersectLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                int end = !intersectRight ? (first + leftNode.TriCount) : (rightNode.TriStartOrChild + rightNode.TriCount);

                for (int i = first; i < end; i++)
                {
                    ref readonly GpuIndicesTriplet indicesTriplet = ref geometry.Triangles[i];
                    Triangle triangle = geometry.GetTriangle(indicesTriplet);

                    if (Intersections.RayVsTriangle(ray, triangle, out Vector3 bary, out float t) && t < hitInfo.T)
                    {
                        hitInfo.TriangleId = i;
                        hitInfo.Bary = bary;
                        hitInfo.T = t;
                    }
                }

                if (leftNode.IsLeaf) traverseLeft = false;
                if (rightNode.IsLeaf) traverseRight = false;

                System.Threading.Interlocked.Add(ref BVH.DebugStatistics.TriIntersections, (ulong)(end - first));
            }

            if (traverseLeft || traverseRight)
            {
                if (traverseLeft && traverseRight)
                {
                    bool leftCloser = tMinLeft < tMinRight;
                    stackTop = leftCloser ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    stack[stackPtr++] = leftCloser ? rightNode.TriStartOrChild : leftNode.TriStartOrChild;
                }
                else
                {
                    stackTop = traverseLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
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

    public static void Intersect(
        in BuildResult blas,
        in Geometry geometry,
        in Box box, Func<int, bool> intersectFunc)
    {
        Span<int> stack = stackalloc int[32];
        int stackPtr = 0;
        int stackTop = 1;

        if (!Intersections.BoxVsBox(box, Conversions.ToBox(blas.Root)))
        {
            return;
        }

        while (true)
        {
            ref readonly GpuBlasNode leftNode = ref blas.Nodes[stackTop];
            ref readonly GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];

            bool traverseLeft = Intersections.BoxVsBox(box, Conversions.ToBox(leftNode));
            bool traverseRight = Intersections.BoxVsBox(box, Conversions.ToBox(rightNode));

            bool intersectLeft = traverseLeft && leftNode.IsLeaf;
            bool intersectRight = traverseRight && rightNode.IsLeaf;
            if (intersectLeft || intersectRight)
            {
                int first = intersectLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                int end = !intersectRight ? (first + leftNode.TriCount) : (rightNode.TriStartOrChild + rightNode.TriCount);

                for (int i = first; i < end; i++)
                {
                    intersectFunc(i);
                }

                if (leftNode.IsLeaf) traverseLeft = false;
                if (rightNode.IsLeaf) traverseRight = false;
            }

            if (traverseLeft || traverseRight)
            {
                stackTop = traverseLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                if (traverseLeft && traverseRight)
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

    public static GpuIndicesTriplet[] GetUnindexedTriangles(in BuildResult blas, in BuildData buildData, in Geometry geometry)
    {
        GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[buildData.Fragments.Count];
        int triCounter = 0;

        for (int i = 0; i < blas.UnpaddedNodesCount; i++)
        {
            ref GpuBlasNode node = ref blas.Nodes[i];
            if (node.IsLeaf)
            {
                // It is important that we put the geometry of the left child first
                // and then immeditaly afer the right childs geometry. Sweep-SAH builder
                // already guarantees this layout but Reinserton opt can destroy it.

                for (int j = 0; j < node.TriCount; j++)
                {
                    triangles[triCounter + j] = geometry.Triangles[buildData.PermutatedFragmentIds[node.TriStartOrChild + j]];
                }

                node.TriStartOrChild = triCounter;
                triCounter += node.TriCount;
            }
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

    public static float ComputeGlobalSAH(in BuildResult blas, BuildSettings settings)
    {
        float cost = 0.0f;

        float rootArea = blas.Root.HalfArea();
        for (int i = 0; i < blas.Nodes.Length; i++)
        {
            ref readonly GpuBlasNode node = ref blas.Nodes[i];
            float probHitNode = node.HalfArea() / rootArea;

            if (node.IsLeaf)
            {
                cost += settings.TriangleCost * node.TriCount * probHitNode;
            }
            else
            {
                cost += TRAVERSAL_COST * probHitNode;
            }
        }
        return cost;
    }

    public static float ComputeOverlap(in BuildResult blas)
    {
        float overlap = 0.0f;

        int stackPtr = 0;
        Span<int> stack = stackalloc int[64];
        stack[stackPtr++] = 1;

        while (stackPtr > 0)
        {
            int stackTop = stack[--stackPtr];

            ref readonly GpuBlasNode leftNode = ref blas.Nodes[stackTop];
            ref readonly GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];

            overlap += Box.GetOverlappingHalfArea(Conversions.ToBox(leftNode), Conversions.ToBox(rightNode));

            if (!rightNode.IsLeaf)
            {
                stack[stackPtr++] = rightNode.TriStartOrChild;
            }
            if (!leftNode.IsLeaf)
            {
                stack[stackPtr++] = leftNode.TriStartOrChild;
            }
        }

        return overlap;
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

    public static Box ComputeBoundingBox(int start, int count, in BuildData buildData, int axis = 0)
    {
        Box box = Box.Empty();
        for (int i = start; i < start + count; i++)
        {
            box.GrowToFit(buildData.Fragments.Bounds[buildData.FragmentIdsSortedOnAxis[axis][i]]);
        }
        return box;
    }

    private static ObjectSplit? TrySplit(in GpuBlasNode parentNode, in BuildData buildData, BuildSettings settings)
    {
        Box parentBox = Conversions.ToBox(parentNode);

        if (parentNode.TriCount <= settings.StopSplittingThreshold || parentBox.HalfArea() == 0.0f)
        {
            return null;
        }

        int start = parentNode.TriStartOrChild;
        int end = parentNode.TriEnd;
        float invParentArea = 1.0f / parentBox.HalfArea();

        ObjectSplit split = new ObjectSplit();
        split.NewCost = parentNode.TriCount;

        // Unfortunately we have to manually load the fields for best perf
        // as the JIT otherwise repeatedly loads them in the loop
        // https://github.com/dotnet/runtime/issues/113107
        BitArray partitionLeft = buildData.PartitionLeft;
        Span<float> rightCostsAccum = buildData.RightCostsAccum;
        Span<Box> fragBounds = buildData.Fragments.Bounds;
        Span<int> fragIdsSorted;

        for (int axis = 0; axis < 3; axis++)
        {
            fragIdsSorted = buildData.FragmentIdsSortedOnAxis[axis];
            Box rightBoxAccum = Box.Empty();
            int firstRight = start + 1;

            for (int i = end - 1; i >= firstRight; i--)
            {
                rightBoxAccum.GrowToFit(fragBounds[fragIdsSorted[i]]);

                int fragCount = end - i;
                float probHitRightChild = rightBoxAccum.HalfArea() * invParentArea;
                float rightCost = probHitRightChild * fragCount;

                rightCostsAccum[i] = rightCost;

                if (rightCost >= split.NewCost)
                {
                    // Don't need to consider split positions beyond this point as cost is already greater and will only get more
                    firstRight = i + 1;
                    break;
                }
            }

            Box leftBoxAccum = Box.Empty();
            for (int i = start; i < firstRight - 1; i++)
            {
                leftBoxAccum.GrowToFit(fragBounds[fragIdsSorted[i]]);
            }
            for (int i = firstRight - 1; i < end - 1; i++)
            {
                leftBoxAccum.GrowToFit(fragBounds[fragIdsSorted[i]]);

                // Implementation of "Surface Area Heuristic" described in "Spatial Splits in Bounding Volume Hierarchies"
                // https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
                int fragIndex = i + 1;
                int fragCount = fragIndex - start;
                float probHitLeftChild = leftBoxAccum.HalfArea() * invParentArea;

                float leftCost = probHitLeftChild * fragCount;
                float rightCost = rightCostsAccum[fragIndex];

                // Estimates cost of hitting parentNode if it was split at the evaluated split position.
                // The full "Surface Area Heuristic" is recursive, but in practice we assume
                // the resulting child nodes are leafs. This the greedy SAH approach
                float cost = leftCost + rightCost;

                if (cost < split.NewCost)
                {
                    split.Pivot = fragIndex;
                    split.Axis = axis;
                    split.NewCost = cost;
                }
                else if (leftCost >= split.NewCost)
                {
                    break;
                }
            }
        }

        float notSplitCost = parentNode.TriCount * settings.TriangleCost;
        split.NewCost = TRAVERSAL_COST + (settings.TriangleCost * split.NewCost);
        if (split.NewCost >= notSplitCost && parentNode.TriCount <= settings.MaxLeafTriangleCount)
        {
            return null;
        }

        fragIdsSorted = buildData.FragmentIdsSortedOnAxis[split.Axis];
        for (int i = start; i < split.Pivot; i++)
        {
            partitionLeft[fragIdsSorted[i]] = true;
        }
        for (int i = split.Pivot; i < end; i++)
        {
            partitionLeft[fragIdsSorted[i]] = false;
        }

        Span<int> partitionAux = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, parentNode.TriCount);

        if (false)
        {
            Box leftBox = ComputeBoundingBox(start, split.Pivot - start, buildData, split.Axis);
            Box rightBox = ComputeBoundingBox(split.Pivot, end - split.Pivot, buildData, split.Axis);

            bool leftSmaller = leftBox.HalfArea() < rightBox.HalfArea();
            Box smallerBox = leftSmaller ? leftBox : rightBox;
            int sideStart = leftSmaller ? split.Pivot : start;
            int sideEnd = leftSmaller ? end : split.Pivot;

            for (int i = sideStart; i < sideEnd; i++)
            {
                Box mergedBox = Box.From(smallerBox, buildData.Fragments.Bounds[buildData.FragmentIdsSortedOnAxis[split.Axis][i]]);

                // Does moving the primitive to the smaller box leave it's area unchanged?
                if (mergedBox == smallerBox)
                {
                    // If yes move to the other side
                    buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[split.Axis][i]] = leftSmaller;
                }
            }
            split.Pivot = start + Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[split.Axis].AsSpan(start, parentNode.TriCount), partitionAux, buildData.PartitionLeft);

            if (split.Pivot == start || split.Pivot == end)
            {
                return null;
            }
        }

        Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(split.Axis + 1) % 3].AsSpan(start, parentNode.TriCount), partitionAux, partitionLeft);
        Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(split.Axis + 2) % 3].AsSpan(start, parentNode.TriCount), partitionAux, partitionLeft);

        return split;
    }

    [InlineArray(3)]
    public struct FragmentsAxesSorted
    {
        private int[] _element;
    }
}