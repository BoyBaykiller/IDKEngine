using System;
using System.Threading;
using System.Threading.Tasks;
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
/// * There are 32 bytes padding at the start to have child nodes be 64-byte aligned
/// * The root starts at index 1 and is never a leaf
/// * The left child of the root is at index 2
/// * A leaf-pair always forms a continous range of geometry beginning left
/// * Straddling geometry of a leaf-pair can be shared (when Pre-Splitting is enabled)
/// </summary>
public static class BLAS
{
    // Do not change, instead modify TriangleCost
    public const float TRAVERSAL_COST = 1.0f;

    public const int THREADED_RECURSION_THRESHOLD = 1 << 13;
    public const int THREADED_SORTING_THRESHOLD = 1 << 16;

    public record struct BuildSettings
    {
        public int StopSplittingThreshold = 1;
        public int MaxLeafTriangleCount = 8;
        public float TriangleCost = 1.1f;
        public float StackSizeOptSAHIncreaseAcceptance = 0.0006f; // 0.06%

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
        public readonly ref GpuBlasNode Root => ref Nodes[1];

        public Span<GpuBlasNode> Nodes;
        public int RequiredStackSize;

        public BuildResult(Span<GpuBlasNode> nodes)
        {
            Nodes = nodes;
        }
    }

    public ref struct Geometry
    {
        public readonly int TriangleCount => TriIndices.Length;

        public Span<Vector3> VertexPositions;
        public Span<GpuIndicesTriplet> TriIndices;

        public Geometry(Span<GpuIndicesTriplet> triangles, Span<Vector3> vertexPositions)
        {
            TriIndices = triangles;
            VertexPositions = vertexPositions;
        }

        public readonly Triangle GetTriangle(int index)
        {
            return GetTriangle(TriIndices[index]);
        }

        public readonly Triangle GetTriangle(in GpuIndicesTriplet indices)
        {
            Vector3 p0 = VertexPositions[indices.X];
            Vector3 p1 = VertexPositions[indices.Y];
            Vector3 p2 = VertexPositions[indices.Z];

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
        public bool[] PartitionLeft; // For single threaded builds BitArray can be used to save memory
        public Fragments Fragments;

        public FragmentsAxesSorted FragmentIdsSortedOnAxis;
    }

    private record struct ObjectSplit
    {
        public int Axis;
        public int SplitIndex;
        public float NewCost;
    }

    public static BuildData GetBuildData(Fragments fragments)
    {
        BuildData buildData = new BuildData();
        buildData.Fragments = fragments;
        buildData.PartitionLeft = new bool[fragments.Count];
        buildData.RightCostsAccum = new float[fragments.Count];
        buildData.FragmentIdsSortedOnAxis[0] = new int[fragments.Count];
        buildData.FragmentIdsSortedOnAxis[1] = new int[fragments.Count];
        buildData.FragmentIdsSortedOnAxis[2] = new int[fragments.Count];

        Task[] tasks = new Task[3];
        for (int axis = 0; axis < 3; axis++)
        {
            int copyAxis = axis;

            bool threaded = fragments.Count >= THREADED_SORTING_THRESHOLD;
            tasks[copyAxis] = Helper.ExecuteMaybeThreaded(threaded, () =>
            {
                Span<int> input = new int[fragments.Count];
                Span<int> output = buildData.FragmentIdsSortedOnAxis[copyAxis];

                Helper.FillIncreasing(input);
                
                Algorithms.RadixSort(input, output, new LambdaSortFragments(fragments, copyAxis));
            });
        }
        Task.WaitAll(tasks);

        return buildData;
    }

    public static unsafe int Build(ref BuildResult blas, BuildData buildData, BuildSettings settings)
    {
        // Adding padding to make child pairs 64 byte aligned can noticable increase perf
        blas.Nodes[0] = new GpuBlasNode();

        ref GpuBlasNode rootNode = ref blas.Nodes[1];
        rootNode.TriStartOrChild = 0;
        rootNode.TriCount = buildData.Fragments.Count;

        fixed (BuildResult* blasPtr = &blas)
        {
            ProcessBuildTask(blasPtr, 1, 2);
        }

        // It's typical to end up with a few deep paths that can be collapsed for a very small SAH increase.
        // Meanwhile the reduced stack size unlocks occupancy making the shader run faster overall.
        OptimizeStackSize(ref blas, settings);

        // Multithreaded building logic assumes 1PPL to get node offsets.
        // An atomic counter could be used to add nodes, but that gives uncoherent and undeterministic ordering.
        // Plus OptimizeStackSize does node collapse also causing empty subtrees. Let's compact.
        int nodeCounter = RemoveEmptySubtrees(blas);

        return nodeCounter;

        void ProcessBuildTask(BuildResult* blas, int parentNodeId, int newNodesId)
        {
            ref GpuBlasNode parentNode = ref blas->Nodes[parentNodeId];
            parentNode.SetBounds(ComputeBoundingBox(parentNode.TriStartOrChild, parentNode.TriCount, buildData));

            bool forceSplit = parentNodeId == 1;
            if (TrySplit(parentNode, buildData, settings, forceSplit) is ObjectSplit objectSplit)
            {
                GpuBlasNode leftNode = new GpuBlasNode();
                leftNode.TriStartOrChild = parentNode.TriStartOrChild;
                leftNode.TriCount = objectSplit.SplitIndex - leftNode.TriStartOrChild;

                GpuBlasNode rightNode = new GpuBlasNode();
                rightNode.TriStartOrChild = objectSplit.SplitIndex;
                rightNode.TriCount = parentNode.TriCount - leftNode.TriCount;

                int leftNodeId = newNodesId;
                int rightNodeId = leftNodeId + 1;

                blas->Nodes[leftNodeId] = leftNode;
                blas->Nodes[rightNodeId] = rightNode;

                parentNode.TriStartOrChild = leftNodeId;
                parentNode.TriCount = 0;

                if (Math.Min(leftNode.TriCount, rightNode.TriCount) >= THREADED_RECURSION_THRESHOLD)
                {
                    // Using a thread pool (Task.Run) is slightly faster if we don't do other BLAS builds in other threads.
                    // But when there are enough BLASes to build and we can saturate the cores that way,
                    // then using a thread pool obliterates performance (far worse than no MT at all) for some reason
                    Thread t = new Thread(() => { ProcessBuildTask(blas, leftNodeId, rightNodeId + 1); });
                    t.Start();
                    ProcessBuildTask(blas, rightNodeId, rightNodeId + MaxNodeCountFromTriCount(leftNode.TriCount));

                    t.Join();
                }
                else
                {
                    ProcessBuildTask(blas, leftNodeId, rightNodeId + 1);
                    ProcessBuildTask(blas, rightNodeId, rightNodeId + MaxNodeCountFromTriCount(leftNode.TriCount));
                }

                static int MaxNodeCountFromTriCount(int triCount)
                {
                    return 2 * triCount - 1;
                }
            }
        }

        static int RemoveEmptySubtrees(BuildResult blas)
        {
            int nodeCounter = 2;

            Span<int> stack = stackalloc int[128];
            int stackPtr = 0;
            stack[stackPtr++] = 1;
            while (stackPtr > 0)
            {
                ref GpuBlasNode parentNode = ref blas.Nodes[stack[--stackPtr]];

                ref readonly GpuBlasNode leftNode = ref blas.Nodes[parentNode.TriStartOrChild];
                ref readonly GpuBlasNode rightNode = ref blas.Nodes[parentNode.TriStartOrChild + 1];

                int leftNodeId = nodeCounter + 0;
                int rightNodeId = nodeCounter + 1;

                blas.Nodes[leftNodeId] = leftNode;
                blas.Nodes[rightNodeId] = rightNode;

                parentNode.TriStartOrChild = leftNodeId;
                nodeCounter += 2;

                if (!rightNode.IsLeaf) stack[stackPtr++] = rightNodeId;
                if (!leftNode.IsLeaf) stack[stackPtr++] = leftNodeId;
            }

            return nodeCounter;
        }
    }

    public static void Refit(BuildResult blas, Geometry geometry)
    {
        for (int i = blas.Nodes.Length - 1; i >= 1; i--)
        {
            ref GpuBlasNode parent = ref blas.Nodes[i];
            if (parent.IsLeaf)
            {
                parent.SetBounds(ComputeBoundingBox(parent.TriStartOrChild, parent.TriCount, geometry));
                continue;
            }

            ref readonly GpuBlasNode leftNode = ref blas.Nodes[parent.TriStartOrChild];
            ref readonly GpuBlasNode rightNode = ref blas.Nodes[parent.TriStartOrChild + 1];

            Box mergedBox = Box.From(Conversions.ToBox(leftNode), Conversions.ToBox(rightNode));
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
                ref readonly GpuBlasNode leftNode = ref nodes[node.TriStartOrChild];
                ref readonly GpuBlasNode rightNode = ref nodes[node.TriStartOrChild + 1];

                Box mergedBox = Box.From(Conversions.ToBox(leftNode), Conversions.ToBox(rightNode));
                node.SetBounds(mergedBox);
            }

            nodeId = parentIds[nodeId];
        } while (nodeId != -1);
    }

    public static bool Intersect(
        BuildResult blas,
        Geometry geometry,
        Ray ray, out RayHitInfo hitInfo, float tMaxDist = float.MaxValue)
    {
        hitInfo = new RayHitInfo();
        hitInfo.T = tMaxDist;

        Span<int> stack = stackalloc int[128];
        int stackPtr = 0;
        int stackTop = 2;

        if (!Intersections.RayVsBox(ray, Conversions.ToBox(blas.Root), out _, out _))
        {
            return false;
        }

        while (true)
        {
            ref readonly GpuBlasNode leftNode = ref blas.Nodes[stackTop];
            ref readonly GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];

            bool traverseLeft = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float _) && tMinLeft <= hitInfo.T;
            bool traverseRight = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out float _) && tMinRight <= hitInfo.T;

            Interlocked.Add(ref BVH.DebugStatistics.BoxIntersections, 2ul);

            bool intersectLeft = traverseLeft && leftNode.IsLeaf;
            bool intersectRight = traverseRight && rightNode.IsLeaf;
            if (intersectLeft || intersectRight)
            {
                int first = intersectLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                int end = !intersectRight ? (first + leftNode.TriCount) : (rightNode.TriStartOrChild + rightNode.TriCount);

                for (int i = first; i < end; i++)
                {
                    Triangle triangle = geometry.GetTriangle(i);

                    if (Intersections.RayVsTriangle(ray, triangle, out Vector3 bary, out float t) && t < hitInfo.T)
                    {
                        hitInfo.TriangleId = i;
                        hitInfo.Bary = bary;
                        hitInfo.T = t;
                    }
                }

                if (leftNode.IsLeaf) traverseLeft = false;
                if (rightNode.IsLeaf) traverseRight = false;

                Interlocked.Add(ref BVH.DebugStatistics.TriIntersections, (ulong)(end - first));
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
        BuildResult blas,
        Geometry geometry,
        Box box, Func<int, bool> intersectFunc)
    {
        Span<int> stack = stackalloc int[128];
        int stackPtr = 0;
        int stackTop = 2;

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

    public static GpuIndicesTriplet[] GetUnindexedTriangles(BuildResult blas, BuildData buildData, Geometry geometry)
    {
        GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[buildData.Fragments.Count];
        int triCounter = 0;

        for (int i = 2; i < blas.Nodes.Length; i++)
        {
            ref GpuBlasNode node = ref blas.Nodes[i];
            if (node.IsLeaf)
            {
                // It is important that we put the geometry of the left leaf first
                // and then immeditaly afer the right leafs geometry. SweepSAH builder
                // already guarantees this layout but Reinsertion opt can destroy it.

                for (int j = 0; j < node.TriCount; j++)
                {
                    triangles[triCounter + j] = geometry.TriIndices[buildData.PermutatedFragmentIds[node.TriStartOrChild + j]];
                }

                node.TriStartOrChild = triCounter;
                triCounter += node.TriCount;
            }
        }

        return triangles;
    }

    public static Box[] GetTriangleBounds(Geometry geometry)
    {
        Box[] bounds = new Box[geometry.TriangleCount];

        for (int i = 0; i < geometry.TriangleCount; i++)
        {
            Triangle tri = geometry.GetTriangle(i);
            bounds[i] = Box.From(tri);
        }

        return bounds;
    }

    public static int[] GetParentIndices(BuildResult blas)
    {
        int[] parents = new int[blas.Nodes.Length];
        parents[0] = -1;
        parents[1] = -1;

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

    public static int[] GetLeafIndices(BuildResult blas)
    {
        int[] indices = new int[blas.Nodes.Length / 2 + 1];
        int counter = 0;
        for (int i = 2; i < blas.Nodes.Length; i++)
        {
            if (blas.Nodes[i].IsLeaf)
            {
                indices[counter++] = i;
            }
        }
        Array.Resize(ref indices, counter);

        return indices;
    }

    public static bool IsLeftSibling(int nodeId)
    {
        return nodeId % 2 == 0;
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
        return Math.Max(2 * triangleCount, 4);
    }

    public static float ComputeEPOArea(BuildResult blas, Geometry geometry, int subtreeRootId)
    {
        if (subtreeRootId == 1)
        {
            return 0.0f;
        }

        Box subtreeBox = Conversions.ToBox(blas.Nodes[subtreeRootId]);

        float area = 0.0f;

        Span<int> stack = stackalloc int[128];
        int stackPtr = 0;
        stack[stackPtr++] = 2;
        stack[stackPtr++] = 3;
        while (stackPtr > 0)
        {
            int nodeId = stack[--stackPtr];

            // EPO measures the triangles which overlap with this subtree but don't belong to it.
            // We ignore the triangles belonging to this subtree by not traversing it
            if (nodeId == subtreeRootId)
            {
                continue;
            }

            ref readonly GpuBlasNode node = ref blas.Nodes[nodeId];

            bool doIntersect = Intersections.BoxVsBox(subtreeBox, Conversions.ToBox(node));
            if (!doIntersect)
            {
                continue;
            }

            if (!node.IsLeaf)
            {
                stack[stackPtr++] = node.TriStartOrChild + 1;
                stack[stackPtr++] = node.TriStartOrChild;
            }
            else
            {
                for (int j = node.TriStartOrChild; j < node.TriEnd; j++)
                {
                    Triangle tri = geometry.GetTriangle(j);
                    area += MyMath.GetTriangleAreaInBox(tri, subtreeBox);
                }
            }
        }

        return area;
    }

    public static float ComputeGlobalEPO(BuildResult blas, Geometry geometry, BuildSettings settings, int subtreeRootId = 1)
    {
        // https://users.aalto.fi/~ailat1/publications/aila2013hpg_paper.pdf
        // https://research.nvidia.com/sites/default/files/pubs/2013-09_On-Quality-Metrics/aila2013hpg_slides.pdf

        float totalArea = 0.0f;

        float cost = 0.0f;

        Span<int> stack = stackalloc int[128];
        int stackPtr = 0;
        stack[stackPtr++] = subtreeRootId;
        while (stackPtr > 0)
        {
            int nodeId = stack[--stackPtr];

            ref readonly GpuBlasNode subtreeRoot = ref blas.Nodes[nodeId];
            float area = ComputeEPOArea(blas, geometry, nodeId);

            if (subtreeRoot.IsLeaf)
            {
                cost += subtreeRoot.TriCount * settings.TriangleCost * area;

                for (int j = subtreeRoot.TriStartOrChild; j < subtreeRoot.TriEnd; j++)
                {
                    Triangle tri = geometry.GetTriangle(j);
                    totalArea += tri.Area;
                }
            }
            else
            {
                cost += TRAVERSAL_COST * area;

                stack[stackPtr++] = subtreeRoot.TriStartOrChild + 1;
                stack[stackPtr++] = subtreeRoot.TriStartOrChild;
            }
        }

        return cost / totalArea;
    }

    public static double ComputeGlobalSAH(BuildResult blas, BuildSettings settings)
    {
        double cost = 0.0f;

        double rootArea = 1.0f / blas.Root.HalfArea();

        Span<int> stack = stackalloc int[128];
        int stackPtr = 0;
        stack[stackPtr++] = 1;
        while (stackPtr > 0)
        {
            ref readonly GpuBlasNode node = ref blas.Nodes[stack[--stackPtr]];
            double probHitNode = node.HalfArea() * rootArea;
            
            if (node.IsLeaf)
            {
                cost += settings.TriangleCost * node.TriCount * probHitNode;
            }
            else
            {
                cost += TRAVERSAL_COST * probHitNode;

                stack[stackPtr++] = node.TriStartOrChild + 1;
                stack[stackPtr++] = node.TriStartOrChild;
            }
        }

        return cost;
    }

    public static int ComputeTreeDepth(BuildResult blas, int nodeId = 1)
    {
        ref readonly GpuBlasNode parent = ref blas.Nodes[nodeId];
        if (parent.IsLeaf)
        {
            return 1;
        }

        int left = ComputeTreeDepth(blas, parent.TriStartOrChild);
        int right = ComputeTreeDepth(blas, parent.TriStartOrChild + 1);
        return Math.Max(left, right) + 1;
    }

    public static int ComputeRequiredStackSize(BuildResult blas, int nodeId = 2)
    {
        // Computes the maximum required stack size for an efficient traversal that:
        // 1. Stores the top in a register
        // 2. Skips the root
        // 3. Never puts leaf nodes on the stack

        ref readonly GpuBlasNode leftNode = ref blas.Nodes[nodeId + 0];
        ref readonly GpuBlasNode rightNode = ref blas.Nodes[nodeId + 1];

        bool traverseLeft = !leftNode.IsLeaf;
        bool traverseRight = !rightNode.IsLeaf;

        if (traverseLeft || traverseRight)
        {
            if (traverseLeft && traverseRight)
            {
                int left = ComputeRequiredStackSize(blas, leftNode.TriStartOrChild);
                int right = ComputeRequiredStackSize(blas, rightNode.TriStartOrChild);
                return Math.Max(left, right) + 1;
            }
            else
            {
                return ComputeRequiredStackSize(blas, traverseLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild);
            }
        }
        else
        {
            return 0;
        }
    }

    public static Box ComputeBoundingBox(int start, int count, Geometry geometry)
    {
        Box box = Box.Empty();

        Span<GpuIndicesTriplet> triIndices = geometry.TriIndices.Slice(start, count);
        for (int i = 0; i < triIndices.Length; i++)
        {
            Triangle tri = geometry.GetTriangle(triIndices[i]);
            box.GrowToFit(tri);
        }
        return box;
    }

    public static Box ComputeBoundingBox(int start, int count, BuildData buildData, int axis = 0)
    {
        Box box = Box.Empty();

        Span<int> fragIds = buildData.FragmentIdsSortedOnAxis[axis].AsSpan(start, count);
        Span<Box> fragBounds = buildData.Fragments.Bounds;
        for (int i = 0; i < fragIds.Length; i++)
        {
            box.GrowToFit(fragBounds[fragIds[i]]);
        }
        return box;
    }

    private static ObjectSplit? TrySplit(GpuBlasNode parentNode, BuildData buildData, BuildSettings settings, bool forceSplit = false)
    {
        Box parentBox = Conversions.ToBox(parentNode);

        if (parentNode.TriCount <= settings.StopSplittingThreshold || parentNode.HalfArea() == 0.0f)
        {
            return null;
        }

        int start = parentNode.TriStartOrChild;
        int end = parentNode.TriEnd;

        ObjectSplit split = new ObjectSplit();
        split.NewCost = float.MaxValue;

        // Unfortunately we have to manually load the fields for best perf
        // as the JIT otherwise repeatedly loads them in the loop
        // https://github.com/dotnet/runtime/issues/113107
        bool[] partitionLeft = buildData.PartitionLeft;
        Span<float> rightCostsAccum = buildData.RightCostsAccum;
        Box[] fragBounds = buildData.Fragments.Bounds;
        Span<int> fragIdsSorted;

        for (int axis = 0; axis < 3; axis++)
        {
            fragIdsSorted = buildData.FragmentIdsSortedOnAxis[axis];

            int firstRight = start + 1;

            Box rightBoxAccum = Box.Empty();
            int rightCounter = 0;
            for (int j = end - 1; j >= firstRight; j--)
            {
                rightCounter++;
                rightBoxAccum.GrowToFit(fragBounds[fragIdsSorted[j]]);

                float rightCost = rightBoxAccum.HalfArea() * rightCounter;

                rightCostsAccum[j] = rightCost;

                if (rightCost >= split.NewCost)
                {
                    // Don't need to consider split positions beyond this point as cost is already greater and will only get more
                    firstRight = j + 1;
                    break;
                }
            }

            Box leftBoxAccum = Box.Empty();
            int leftCounter = firstRight - start - 1;
            for (int j = start; j < firstRight - 1; j++)
            {
                leftBoxAccum.GrowToFit(fragBounds[fragIdsSorted[j]]);
            }
            for (int j = firstRight - 1; j < end - 1; j++)
            {
                int splitIndex = j + 1;

                leftCounter++;
                leftBoxAccum.GrowToFit(fragBounds[fragIdsSorted[j]]);

                float leftCost = leftBoxAccum.HalfArea() * leftCounter;
                float rightCost = rightCostsAccum[splitIndex];
                float cost = leftCost + rightCost;

                if (cost < split.NewCost)
                {
                    split.SplitIndex = splitIndex;
                    split.Axis = axis;
                    split.NewCost = cost;
                }
                else if (leftCost >= split.NewCost)
                {
                    break;
                }
            }
        }

        float notSplitCost = settings.TriangleCost * parentNode.TriCount;
        split.NewCost = TRAVERSAL_COST + (settings.TriangleCost * split.NewCost / parentBox.HalfArea());
        if (split.NewCost >= notSplitCost && parentNode.TriCount <= settings.MaxLeafTriangleCount && !forceSplit)
        {
            return null;
        }

        Box leftBox = ComputeBoundingBox(start, split.SplitIndex - start, buildData, split.Axis);
        Box rightBox = ComputeBoundingBox(split.SplitIndex, end - split.SplitIndex, buildData, split.Axis);
        bool leftSmaller = leftBox.HalfArea() < rightBox.HalfArea();

        // Make the larger child always be on the left as it's more likely
        // to be hit and traversing the left path is memory friendly
        bool swapSides = leftSmaller;

        fragIdsSorted = buildData.FragmentIdsSortedOnAxis[split.Axis];
        for (int i = start; i < split.SplitIndex; i++)
        {
            partitionLeft[fragIdsSorted[i]] = !swapSides;
        }
        for (int i = split.SplitIndex; i < end; i++)
        {
            partitionLeft[fragIdsSorted[i]] = swapSides;
        }

        Span<int> partitionAux = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, start, parentNode.TriCount);

        if (false)
        {
            Box smallerBox = leftSmaller ? leftBox : rightBox;
            int sideStart = leftSmaller ? split.SplitIndex : start;
            int sideEnd = leftSmaller ? end : split.SplitIndex;

            for (int i = sideStart; i < sideEnd; i++)
            {
                Box mergedBox = Box.From(smallerBox, fragBounds[fragIdsSorted[i]]);

                // Does moving the fragment to the smaller box leave it's area unchanged?
                if (mergedBox == smallerBox)
                {
                    // If yes move to the other side
                    buildData.PartitionLeft[fragIdsSorted[i]] = leftSmaller;
                }
            }
            split.SplitIndex = start + Algorithms.StablePartition(fragIdsSorted.Slice(start, parentNode.TriCount), partitionAux, buildData.PartitionLeft);

            if (split.SplitIndex == start || split.SplitIndex == end)
            {
                return null;
            }
        }
        else if (swapSides)
        {
            split.SplitIndex = start + Algorithms.StablePartition(fragIdsSorted.Slice(start, parentNode.TriCount), partitionAux, partitionLeft);
        }

        Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(split.Axis + 1) % 3].AsSpan(start, parentNode.TriCount), partitionAux, partitionLeft);
        Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(split.Axis + 2) % 3].AsSpan(start, parentNode.TriCount), partitionAux, partitionLeft);

        return split;
    }

    private static void OptimizeStackSize(ref BuildResult blas, BuildSettings settings)
    {
        double initalCost = ComputeGlobalSAH(blas, settings);
        int newStackSize = ComputeRequiredStackSize(blas);

        double costIncrease = 0.0f;

        // Before we collapse the level we need to gather the cost increase first
        CollapseDeepestLevel(blas, newStackSize - 1, firstPass: true, ref costIncrease);
        double increasePercent = costIncrease / initalCost;

        while (increasePercent <= settings.StackSizeOptSAHIncreaseAcceptance && newStackSize > 0)
        {
            // Collapse the deepest level and also add cost for collapsing the next level
            CollapseDeepestLevel(blas, --newStackSize, firstPass: false, ref costIncrease);
            increasePercent = costIncrease / initalCost;
        }

        blas.RequiredStackSize = newStackSize;

        void CollapseDeepestLevel(BuildResult blas, int newStackSize, bool firstPass, ref double nextCollapseCost, int parentId = 1, int stackSize = 0)
        {
            ref GpuBlasNode parentNode = ref blas.Nodes[parentId];

            ref readonly GpuBlasNode leftNode = ref blas.Nodes[parentNode.TriStartOrChild];
            ref readonly GpuBlasNode rightNode = ref blas.Nodes[parentNode.TriStartOrChild + 1];

            if (!leftNode.IsLeaf)
            {
                CollapseDeepestLevel(blas, newStackSize, firstPass, ref nextCollapseCost, parentNode.TriStartOrChild + 0, stackSize + 1);
            }

            if (!rightNode.IsLeaf)
            {
                CollapseDeepestLevel(blas, newStackSize, firstPass, ref nextCollapseCost, parentNode.TriStartOrChild + 1, stackSize + 1);
            }

            if (leftNode.IsLeaf && rightNode.IsLeaf)
            {
                if (stackSize > newStackSize && !firstPass)
                {
                    parentNode.TriStartOrChild = leftNode.TriStartOrChild;
                    parentNode.TriCount = leftNode.TriCount + rightNode.TriCount;
                }
                
                if ((stackSize == newStackSize && !firstPass) || (stackSize > newStackSize && firstPass))
                {
                    double leavesCost = settings.TriangleCost * ((double)leftNode.TriCount * leftNode.HalfArea() + (double)rightNode.TriCount * rightNode.HalfArea());
                    double newParentLeafCost = (double)settings.TriangleCost * (leftNode.TriCount + rightNode.TriCount);

                    nextCollapseCost += (parentNode.HalfArea() * (newParentLeafCost - TRAVERSAL_COST) - leavesCost) / blas.Root.HalfArea();
                }
            }
        }
    }

    private record struct LambdaSortFragments : Algorithms.IRadixSortable<int>
    {
        private readonly Fragments fragments;
        private readonly int axis;

        public LambdaSortFragments(Fragments fragments, int axis)
        {
            this.fragments = fragments;
            this.axis = axis;
        }

        public readonly uint GetKey(int index)
        {
            float p = fragments.Bounds[index].SimdMin[axis] + fragments.Bounds[index].SimdMax[axis];
            return Algorithms.FloatToKey(p);
        }
    }

    [InlineArray(3)]
    public struct FragmentsAxesSorted
    {
        private int[] _element;
    }
}