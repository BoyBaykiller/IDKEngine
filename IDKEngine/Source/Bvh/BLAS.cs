using System;
using System.Threading;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    /// <summary>
    /// Implementation of "SBVH" from "Spatial Splits in Bounding Volume Hierarchies"
    /// with memory managment from "Parallel Spatial Splits in Bounding Volume Hierarchies".
    /// Objects splits are using "Sweep SAH" from "Bonsai: Rapid Bounding Volume Hierarchy Generation using Mini Trees"
    /// https://www.nvidia.in/docs/IO/77714/sbvh.pdf
    /// https://diglib.eg.org/bitstream/handle/10.2312/pgv20161179/021-030.pdf
    /// https://jcgt.org/published/0004/03/02/paper-lowres.pdf
    /// </summary>
    public static unsafe class BLAS
    {
        // Do not change, instead modify TriangleCost
        public const float TRAVERSAL_COST = 1.0f;

        public record struct BuildSettings
        {
            public int MinLeafTriangleCount = 1;
            public int MaxLeafTriangleCount = 8;
            public float TriangleCost = 1.1f;

            public SBVHSettings SBVH = new SBVHSettings();
            public PreSplitting.Settings PreSplitting = new PreSplitting.Settings();

            public BuildSettings()
            {
            }
        }

        public record struct SBVHSettings
        {
            public readonly bool Enabled => SplitFactor > 0.0f;

            public float SplitFactor = 1.0f;
            public float OverlapTreshold = 0.0001f;

            public SBVHSettings()
            {
            }
        }

        public record struct Fragments
        {
            public readonly int ReservedSpace => Bounds.Length;

            public int Count;

            public Box[] Bounds;
            public int[] OriginalTriIds;

            public void Add(Box box, int origTriId)
            {
                Bounds[Count] = box;
                OriginalTriIds[Count] = origTriId;
                Count++;
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
            /// <summary>
            /// True if Pre-Splitting and/or SBVH is applied
            /// </summary>
            public readonly bool HasSpatialSplits => Fragments.OriginalTriIds != null;

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
            public Box LeftBox;
            public Box RightBox;
        }

        private record struct SpatialSplit
        {
            public readonly int Count => LeftCount + RightCount;

            public float NewCost;
            public int Axis;
            public float Position;
            public Box LeftBox;
            public Box RightBox;
            public int LeftCount;
            public int RightCount;
        }

        private record struct BuildStackItem
        {
            public int ParentNodeId;
            public int ReservedSpaceBoundary;
            public bool FragmentsAreSorted;
        }

        public static Fragments GetFragments(in Geometry geometry, in BuildSettings settings)
        {
            Fragments fragments = new Fragments();

            if (settings.PreSplitting.Enabled)
            {
                PreSplitting.PrepareData info = PreSplitting.Prepare(geometry, settings.PreSplitting);

                int maxFragmentCount = info.FragmentCount + (int)(info.FragmentCount * settings.SBVH.SplitFactor);
                fragments.Bounds = new Box[maxFragmentCount];
                fragments.OriginalTriIds = new int[maxFragmentCount];
                fragments.Count = info.FragmentCount;

                PreSplitting.PreSplit(info, fragments.Bounds, fragments.OriginalTriIds, settings.PreSplitting, geometry);
            }
            else
            {
                int maxFragmentCount = geometry.TriangleCount + (int)(geometry.TriangleCount * settings.SBVH.SplitFactor) + 1000;
                fragments.Bounds = new Box[maxFragmentCount];

                if (settings.SBVH.Enabled)
                {
                    fragments.OriginalTriIds = new int[maxFragmentCount];
                    Helper.FillIncreasing(fragments.OriginalTriIds);
                }
                fragments.Count = geometry.TriangleCount;

                GetTriangleBounds(fragments.Bounds, geometry);
            }

            return fragments;
        }

        public static BuildData GetBuildData(Fragments fragments)
        {
            BuildData buildData = new BuildData();
            buildData.Fragments = fragments;
            buildData.PartitionLeft = new BitArray(fragments.ReservedSpace, false);
            buildData.RightCostsAccum = new float[fragments.ReservedSpace];
            buildData.FragmentIdsSortedOnAxis[0] = new int[fragments.ReservedSpace];
            buildData.FragmentIdsSortedOnAxis[1] = new int[fragments.ReservedSpace];
            buildData.FragmentIdsSortedOnAxis[2] = new int[fragments.ReservedSpace];

            for (int axis = 0; axis < 3; axis++)
            {
                Span<int> input = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, fragments.Count);
                Span<int> output = buildData.FragmentIdsSortedOnAxis[axis].AsSpan(0, fragments.Count);

                Helper.FillIncreasing(input);
                Algorithms.RadixSort(input, output, (int index) =>
                {
                    float centerAxis = (fragments.Bounds[index].Min[axis] + fragments.Bounds[index].Max[axis]) * 0.5f;
                    return Algorithms.FloatToKey(centerAxis);
                });
            }

            return buildData;
        }

        public static int Build(ref BuildResult blas, in Geometry geometry, ref BuildData buildData, BuildSettings settings)
        {
            if (true)
            {
                int nodesUsed = 0;

                ref GpuBlasNode rootNode = ref blas.Nodes[nodesUsed++];
                rootNode.TriStartOrChild = 0;
                rootNode.TriCount = buildData.Fragments.Count;
                rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount, buildData));

                float globalArea = rootNode.HalfArea();

                int stackPtr = 0;
                Span<BuildStackItem> stack = stackalloc BuildStackItem[64];
                stack[stackPtr++] = new BuildStackItem() { ParentNodeId = 0, ReservedSpaceBoundary = buildData.Fragments.ReservedSpace, FragmentsAreSorted = true };
                while (stackPtr > 0)
                {
                    ref readonly BuildStackItem parentData = ref stack[--stackPtr];
                    ref GpuBlasNode parentNode = ref blas.Nodes[parentData.ParentNodeId];

                    Box parentBox = Conversions.ToBox(parentNode);
                    int start = parentNode.TriStartOrChild;
                    int end = parentNode.TriEnd;

                    if (parentNode.TriCount <= settings.MinLeafTriangleCount || parentBox.HalfArea() == 0.0f)
                    {
                        continue;
                    }

                    if (!parentData.FragmentsAreSorted)
                    {
                        for (int axis = 0; axis < 3; axis++)
                        {
                            Span<int> input = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, parentNode.TriCount);
                            Span<int> output = buildData.FragmentIdsSortedOnAxis[axis].AsSpan(parentNode.TriStartOrChild, parentNode.TriCount);

                            Box[] bounds = buildData.Fragments.Bounds;
                            uint SortFunc(int index)
                            {
                                float centerAxis = (bounds[index].Min[axis] + bounds[index].Max[axis]) * 0.5f;
                                return Algorithms.FloatToKey(centerAxis);
                            };

                            if (output.Length > 32)
                            {
                                output.CopyTo(input);
                                Algorithms.RadixSort(input, output, SortFunc);
                            }
                            else
                            {
                                MemoryExtensions.Sort(output, (int a, int b) => MyComparer.LessThan(SortFunc(a), SortFunc(b)));
                            }
                        }
                    }

                    ObjectSplit objectSplit = FindObjectSplit(parentBox, start, end, buildData, settings);
                    SpatialSplit spatialSplit = new SpatialSplit() { NewCost = float.MaxValue };
                    
                    if (settings.SBVH.Enabled)
                    {
                        float overlap = Box.GetOverlappingHalfArea(objectSplit.LeftBox, objectSplit.RightBox);
                        float percentOverlap = overlap / globalArea;

                        bool trySpatialSplit = percentOverlap > settings.SBVH.OverlapTreshold;
                        if (trySpatialSplit)
                        {
                            spatialSplit = FindSpatialSplit(parentBox, start, end, geometry, buildData, settings);
                        }
                    }

                    bool useSpatialSplit = spatialSplit.NewCost < objectSplit.NewCost;
                    float notSplitCost = CostLeafNode(parentNode.TriCount, settings.TriangleCost);
                    float splitCost = useSpatialSplit ? spatialSplit.NewCost : objectSplit.NewCost;
                    if (splitCost >= notSplitCost)
                    {
                        if (parentNode.TriCount > settings.MaxLeafTriangleCount)
                        {
                            useSpatialSplit = false;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    bool parentIsLeft = end <= parentData.ReservedSpaceBoundary;
                    int reservedStart = parentIsLeft ? start : parentData.ReservedSpaceBoundary;
                    int reservedEnd = parentIsLeft ? parentData.ReservedSpaceBoundary : end;
                    int reservedCount = reservedEnd - reservedStart;
                    int remainingSpaceAfterSplit = reservedCount - (useSpatialSplit ? spatialSplit.Count : parentNode.TriCount);

                    if (useSpatialSplit && (remainingSpaceAfterSplit < 0 || spatialSplit.LeftCount == 0 || spatialSplit.RightCount == 0))
                    {
                        useSpatialSplit = false;
                        remainingSpaceAfterSplit = reservedCount - parentNode.TriCount;
                    }

                    GpuBlasNode newLeftNode = new GpuBlasNode();
                    GpuBlasNode newRightNode = new GpuBlasNode();

                    if (!useSpatialSplit)
                    {
                        for (int i = start; i < objectSplit.Pivot; i++)
                        {
                            buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]] = true;
                        }
                        for (int i = objectSplit.Pivot; i < end; i++)
                        {
                            buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]] = false;
                        }

                        Span<int> partitionAux = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, end - objectSplit.Pivot);
                        Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3].AsSpan(start, parentNode.TriCount), partitionAux, buildData.PartitionLeft);
                        Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3].AsSpan(start, parentNode.TriCount), partitionAux, buildData.PartitionLeft);

                        newLeftNode.TriStartOrChild = start;
                        newLeftNode.TriCount = objectSplit.Pivot - start;
                        newLeftNode.SetBounds(objectSplit.LeftBox);

                        newRightNode.TriStartOrChild = objectSplit.Pivot;
                        newRightNode.TriCount = end - objectSplit.Pivot;
                        newRightNode.SetBounds(objectSplit.RightBox);

                        if (parentIsLeft)
                        {
                            int newStart = reservedEnd - newRightNode.TriCount;
                            Array.Copy(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 0) % 3], newRightNode.TriStartOrChild, buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 0) % 3], newStart, newRightNode.TriCount);
                            Array.Copy(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3], newRightNode.TriStartOrChild, buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3], newStart, newRightNode.TriCount);
                            Array.Copy(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3], newRightNode.TriStartOrChild, buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3], newStart, newRightNode.TriCount);
                            newRightNode.TriStartOrChild = newStart;
                        }
                        else
                        {
                            Array.Copy(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 0) % 3], newLeftNode.TriStartOrChild, buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 0) % 3], reservedStart, newLeftNode.TriCount);
                            Array.Copy(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3], newLeftNode.TriStartOrChild, buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3], reservedStart, newLeftNode.TriCount);
                            Array.Copy(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3], newLeftNode.TriStartOrChild, buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3], reservedStart, newLeftNode.TriCount);
                            newLeftNode.TriStartOrChild = reservedStart;
                        }
                    }
                    else
                    {
                        int leftDest = reservedStart;
                        int rightDest = reservedEnd - spatialSplit.RightCount;

                        int leftCounter = 0;
                        int rightCounter = 0;
                        for (int i = start; i < end; i++)
                        {
                            int fragmentId = buildData.FragmentIdsSortedOnAxis[spatialSplit.Axis][i];

                            Box fragmentBounds = buildData.Fragments.Bounds[fragmentId];
                            int triangleId = buildData.Fragments.OriginalTriIds[fragmentId];

                            bool completlyLeft = fragmentBounds.Max[spatialSplit.Axis] <= spatialSplit.Position;
                            bool completlyRight = fragmentBounds.Min[spatialSplit.Axis] >= spatialSplit.Position;
                            bool isStraddling = !completlyLeft && !completlyRight;
                            if (completlyLeft)
                            {
                                buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][leftDest + leftCounter] = fragmentId;
                                leftCounter++;
                            }
                            else if (completlyRight)
                            {
                                buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][rightDest + rightCounter] = fragmentId;
                                rightCounter++;
                            }
                            else
                            {
                                Box putInLeftBox = spatialSplit.LeftBox;
                                putInLeftBox.GrowToFit(fragmentBounds);

                                Box putInRightBox = spatialSplit.RightBox;
                                putInRightBox.GrowToFit(fragmentBounds);

                                float straddlingCost = spatialSplit.NewCost;
                                {
                                    float areaParent = parentBox.HalfArea();
                                    float probHitLeftChild = spatialSplit.LeftBox.HalfArea() / areaParent;
                                    float probHitRightChild = spatialSplit.RightBox.HalfArea() / areaParent;
                                    straddlingCost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(spatialSplit.LeftCount, settings.TriangleCost), CostLeafNode(spatialSplit.RightCount, settings.TriangleCost));
                                }

                                float putInLeftCost;
                                {
                                    float areaParent = parentBox.HalfArea();
                                    float probHitLeftChild = putInLeftBox.HalfArea() / areaParent;
                                    float probHitRightChild = spatialSplit.RightBox.HalfArea() / areaParent;
                                    putInLeftCost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(spatialSplit.LeftCount, settings.TriangleCost), CostLeafNode(spatialSplit.RightCount - 1, settings.TriangleCost));
                                }
                                float putInRightCost;
                                {
                                    float areaParent = parentBox.HalfArea();
                                    float probHitLeftChild = spatialSplit.LeftBox.HalfArea() / areaParent;
                                    float probHitRightChild = putInRightBox.HalfArea() / areaParent;
                                    putInRightCost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(spatialSplit.LeftCount - 1, settings.TriangleCost), CostLeafNode(spatialSplit.RightCount, settings.TriangleCost));
                                }

                                if (straddlingCost < putInLeftCost && straddlingCost < putInRightCost)
                                {
                                    Triangle triangle = geometry.GetTriangle(triangleId);
                                    (Box lSplittedBox, Box rSplittedBox) = triangle.Split(spatialSplit.Axis, spatialSplit.Position);
                                    lSplittedBox.ShrinkToFit(fragmentBounds);
                                    rSplittedBox.ShrinkToFit(fragmentBounds);

                                    buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][leftDest + leftCounter] = buildData.Fragments.Count;
                                    buildData.Fragments.Add(lSplittedBox, triangleId);
                                    leftCounter++;
                                    
                                    buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][rightDest + rightCounter] = fragmentId;
                                    buildData.Fragments.Bounds[fragmentId] = rSplittedBox;
                                    rightCounter++;
                                }
                                else
                                {
                                    if (putInLeftCost < putInRightCost)
                                    {
                                        buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][leftDest + leftCounter] = fragmentId;
                                        buildData.Fragments.Bounds[fragmentId] = fragmentBounds;
                                        leftCounter++;

                                        spatialSplit.LeftBox.GrowToFit(fragmentBounds);
                                        spatialSplit.RightCount--;
                                    }
                                    else
                                    {
                                        buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][rightDest + rightCounter] = fragmentId;
                                        buildData.Fragments.Bounds[fragmentId] = fragmentBounds;
                                        rightCounter++;

                                        spatialSplit.RightBox.GrowToFit(fragmentBounds);
                                        spatialSplit.LeftCount--;
                                    }
                                }
                            }
                        }

                        if (spatialSplit.LeftCount == 0 || spatialSplit.RightCount == 0)
                        {
                            throw new Exception("uh no handle this");
                        }

                        Array.Copy(buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3], leftDest, buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 2) % 3], leftDest, leftCounter);
                        Array.Copy(buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3], leftDest, buildData.FragmentIdsSortedOnAxis[spatialSplit.Axis], leftDest, leftCounter);

                        Array.Copy(buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3], rightDest, buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 2) % 3], rightDest, rightCounter);
                        Array.Copy(buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3], rightDest, buildData.FragmentIdsSortedOnAxis[spatialSplit.Axis], rightDest, rightCounter);

                        newLeftNode.TriStartOrChild = leftDest;
                        newLeftNode.TriCount = leftCounter;
                        newLeftNode.SetBounds(spatialSplit.LeftBox);

                        newRightNode.TriStartOrChild = rightDest;
                        newRightNode.TriCount = rightCounter;
                        newRightNode.SetBounds(spatialSplit.RightBox);
                    }

                    float leftCost = newLeftNode.HalfArea() * newLeftNode.TriCount;
                    float rightCost = newRightNode.HalfArea() * newRightNode.TriCount;

                    float shareOfReservedSpace = leftCost / (leftCost + rightCost);
                    int newReservedSpaceBoundary = newLeftNode.TriEnd + (int)(remainingSpaceAfterSplit * shareOfReservedSpace);

                    int leftNodeId = nodesUsed + 0;
                    int rightNodeId = nodesUsed + 1;

                    blas.Nodes[leftNodeId] = newLeftNode;
                    blas.Nodes[rightNodeId] = newRightNode;

                    parentNode.TriStartOrChild = leftNodeId;
                    parentNode.TriCount = 0;
                    nodesUsed += 2;

                    stack[stackPtr++] = new BuildStackItem() { ParentNodeId = rightNodeId, ReservedSpaceBoundary = newReservedSpaceBoundary, FragmentsAreSorted = !useSpatialSplit };
                    stack[stackPtr++] = new BuildStackItem() { ParentNodeId = leftNodeId, ReservedSpaceBoundary = newReservedSpaceBoundary, FragmentsAreSorted = !useSpatialSplit };

                    Debug.Assert(newRightNode.TriStartOrChild >= newLeftNode.TriEnd);
                    Debug.Assert(newReservedSpaceBoundary >= newLeftNode.TriEnd);
                    Debug.Assert(newReservedSpaceBoundary <= newRightNode.TriStartOrChild);
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
            else
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
                    Box parentBox = Conversions.ToBox(parentNode);

                    if (parentNode.TriCount <= settings.MinLeafTriangleCount || parentBox.HalfArea() == 0.0f)
                    {
                        continue;
                    }

                    ObjectSplit objectSplit = FindObjectSplit(parentBox, parentNode.TriStartOrChild, parentNode.TriEnd, buildData, settings);

                    float notSplitCost = CostLeafNode(parentNode.TriCount, settings.TriangleCost);
                    if (objectSplit.NewCost >= notSplitCost && parentNode.TriCount <= settings.MaxLeafTriangleCount)
                    {
                        continue;
                    }

                    (GpuBlasNode newLeftNode, GpuBlasNode newRightNode) = ApplyObjectSplit(buildData, parentNode.TriStartOrChild, parentNode.TriEnd, objectSplit);

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

        public static float ComputeGlobalCost(in GpuBlasNode rootNode, ReadOnlySpan<GpuBlasNode> nodes, in BuildSettings settings)
        {
            float cost = 0.0f;

            float rootArea = rootNode.HalfArea();
            for (int i = 0; i < nodes.Length; i++)
            {
                ref readonly GpuBlasNode node = ref nodes[i];
                float probHitNode = nodes[i].HalfArea() / rootArea;

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

                bool traverseLeft = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool traverseRight = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                Interlocked.Add(ref BVH.DebugStatistics.BoxIntersections, 2ul);

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
                            hitInfo.TriangleIndices = indicesTriplet;
                            hitInfo.Bary = bary;
                            hitInfo.T = t;
                        }
                    }

                    if (leftNode.IsLeaf) traverseLeft = false;
                    if (rightNode.IsLeaf) traverseRight = false;

                    if (hitInfo.T < tMinLeft && traverseLeft)
                    {
                        traverseLeft = false;
                    }
                    if (hitInfo.T < tMinRight && traverseRight)
                    {
                        traverseRight = false;
                    }

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
               
                bool traverseLeft = Intersections.BoxVsBox(Conversions.ToBox(leftNode), box);
                bool traverseRight = Intersections.BoxVsBox(Conversions.ToBox(rightNode), box);

                bool intersectLeft = traverseLeft && leftNode.IsLeaf;
                bool intersectRight = traverseRight && rightNode.IsLeaf;
                if (intersectLeft || intersectRight)
                {
                    int first = intersectLeft ? leftNode.TriStartOrChild : rightNode.TriStartOrChild;
                    int end = !intersectRight ? (first + leftNode.TriCount) : (rightNode.TriStartOrChild + rightNode.TriCount);

                    for (int i = first; i < end; i++)
                    {
                        ref readonly GpuIndicesTriplet indicesTriplet = ref geometry.Triangles[i];
                        intersectFunc(indicesTriplet);
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

        public static GpuIndicesTriplet[] GetUnindexedTriangles(in BuildResult blas, in BuildData buildData, in Geometry geometry, in BuildSettings buildSettings)
        {
            GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[buildData.Fragments.Count];
            int globalTriCounter = 0;

            if (buildData.HasSpatialSplits)
            {
                // When spatial splits are used duplicate triangle references in a leaf(-pair) may happen.
                // Here we deduplicate them when possible possibly resulting in leaf-pair triangles like:
                // [lStart, lEnd), [rStart, rEnd), where the "straddling triangles" are shared.
                // In addition the SBVH builder doesn't guarantee continous triangle ranges which is also fixed here.

                int stackPtr = 0;
                Span<int> stack = stackalloc int[64];
                stack[stackPtr++] = 1;
                while (stackPtr > 0)
                {
                    int stackTop = stack[--stackPtr];

                    ref GpuBlasNode leftChild = ref blas.Nodes[stackTop];
                    ref GpuBlasNode rightChild = ref blas.Nodes[stackTop + 1];

                    if (leftChild.IsLeaf && rightChild.IsLeaf)
                    {
                        Span<int> leftUniqueTriIds = GetUniqueTriIds(leftChild, buildData);
                        Span<int> rightUniqueTriIds = GetUniqueTriIds(rightChild, buildData);
                        Span<int> straddlingTriIds = GetStraddlingTriIds(leftUniqueTriIds, rightUniqueTriIds);

                        int onlyLeftTriCount = 0;
                        int backwardsCounter = 0;
                        for (int i = 0; i < leftUniqueTriIds.Length; i++)
                        {
                            int triId = leftUniqueTriIds[i];
                            if (straddlingTriIds.Contains(triId))
                            {
                                triangles[globalTriCounter + (leftUniqueTriIds.Length - backwardsCounter++ - 1)] = geometry.Triangles[triId];
                            }
                            else
                            {
                                triangles[globalTriCounter + onlyLeftTriCount++] = geometry.Triangles[triId];
                            }
                        }

                        int onlyRightTriCount = 0;
                        for (int i = 0; i < rightUniqueTriIds.Length; i++)
                        {
                            int triId = rightUniqueTriIds[i];
                            if (!straddlingTriIds.Contains(triId))
                            {
                                triangles[globalTriCounter + leftUniqueTriIds.Length + onlyRightTriCount++] = geometry.Triangles[triId];
                            }
                        }

                        leftChild.TriStartOrChild = globalTriCounter;
                        leftChild.TriCount = leftUniqueTriIds.Length;

                        rightChild.TriStartOrChild = globalTriCounter + onlyLeftTriCount;
                        rightChild.TriCount = rightUniqueTriIds.Length;

                        int leafsTriCount = rightChild.TriEnd - leftChild.TriStartOrChild;
                        globalTriCounter += leafsTriCount;
                    }
                    else if (leftChild.IsLeaf || rightChild.IsLeaf)
                    {
                        ref GpuBlasNode theLeafNode = ref (leftChild.IsLeaf ? ref leftChild : ref rightChild);

                        Span<int> uniqueTriIds = GetUniqueTriIds(theLeafNode, buildData);
                        for (int i = 0; i < uniqueTriIds.Length; i++)
                        {
                            triangles[globalTriCounter + i] = geometry.Triangles[uniqueTriIds[i]];
                        }

                        theLeafNode.TriStartOrChild = globalTriCounter;
                        theLeafNode.TriCount = uniqueTriIds.Length;
                        globalTriCounter += uniqueTriIds.Length;
                    }

                    if (!rightChild.IsLeaf)
                    {
                        stack[stackPtr++] = rightChild.TriStartOrChild;
                    }
                    if (!leftChild.IsLeaf)
                    {
                        stack[stackPtr++] = leftChild.TriStartOrChild;
                    }
                }

                static Span<int> GetUniqueTriIds(in GpuBlasNode leafNode, in BLAS.BuildData buildData)
                {
                    Span<int> triIds = new int[leafNode.TriCount];
                    for (int i = 0; i < leafNode.TriCount; i++)
                    {
                        triIds[i] = buildData.Fragments.OriginalTriIds[buildData.PermutatedFragmentIds[leafNode.TriStartOrChild + i]];
                    }

                    MemoryExtensions.Sort(triIds);

                    int uniqueTriCount = Algorithms.SortedFilterDuplicates(triIds);
                    Span<int> uniqueTriIds = triIds.Slice(0, uniqueTriCount);

                    return uniqueTriIds;
                }

                static Span<int> GetStraddlingTriIds(Span<int> leftTriIds, Span<int> rightTriIds)
                {
                    List<int> straddlingTris = new List<int>((leftTriIds.Length + rightTriIds.Length) / 4);
                    for (int i = 0; i < leftTriIds.Length; i++)
                    {
                        int triId = leftTriIds[i];

                        if (rightTriIds.Contains(triId))
                        {
                            straddlingTris.Add(triId);
                        }
                    }

                    return straddlingTris;
                }
            }
            else
            {
                for (int i = 0; i < blas.UnpaddedNodesCount; i++)
                {
                    ref GpuBlasNode node = ref blas.Nodes[i];
                    if (node.IsLeaf)
                    {
                        for (int j = 0; j < node.TriCount; j++)
                        {
                            int triId = buildData.PermutatedFragmentIds[node.TriStartOrChild + j];
                            triangles[globalTriCounter + j] = geometry.Triangles[triId];
                        }
                        node.TriStartOrChild = globalTriCounter;
                        globalTriCounter += node.TriCount;
                    }
                }
            }

            Array.Resize(ref triangles, globalTriCounter);
            return triangles;
        }

        public static void GetTriangleBounds(Span<Box> bounds, in Geometry geometry)
        {
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle tri = geometry.GetTriangle(i);
                bounds[i] = Box.From(tri);
            }
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
                box.GrowToFit(buildData.Fragments.Bounds[buildData.PermutatedFragmentIds[i]]);
            }
            return box;
        }

        private static ObjectSplit FindObjectSplit(in Box parentBox, int start, int end, BuildData buildData, in BuildSettings settings)
        {
            ObjectSplit objectSplit = new ObjectSplit();

            float parentArea = parentBox.HalfArea();
            
            objectSplit.Axis = 0;
            objectSplit.Pivot = 0;
            objectSplit.NewCost = float.MaxValue;

            // Unfortunately we have to manually load the fields for best perf
            // as the JIT otherwise repeatedly loads them in the loop
            // https://github.com/dotnet/runtime/issues/113107
            Span<float> rightCostsAccum = buildData.RightCostsAccum;
            Span<Box> triBounds = buildData.Fragments.Bounds;

            for (int axis = 0; axis < 3; axis++)
            {
                Span<int> trisAxesSorted = buildData.FragmentIdsSortedOnAxis[axis];

                Box rightBoxAccum = Box.Empty();
                int firstRightTri = start + 1;

                for (int i = end - 1; i >= firstRightTri; i--)
                {
                    rightBoxAccum.GrowToFit(triBounds[trisAxesSorted[i]]);

                    int count = end - i;
                    float probHitRightChild = rightBoxAccum.HalfArea() / parentArea;
                    float rightCost = probHitRightChild * CostLeafNode(count, settings.TriangleCost);

                    rightCostsAccum[i] = rightCost;

                    if (rightCost >= objectSplit.NewCost)
                    {
                        // Don't need to consider split positions beyond this point as cost is already greater and will only get more
                        firstRightTri = i + 1;
                        break;
                    }
                }

                Box leftBoxAccum = Box.Empty();
                for (int i = start; i < firstRightTri - 1; i++)
                {
                    leftBoxAccum.GrowToFit(triBounds[trisAxesSorted[i]]);
                }
                for (int i = firstRightTri - 1; i < end - 1; i++)
                {
                    leftBoxAccum.GrowToFit(triBounds[trisAxesSorted[i]]);

                    // Implementation of "Surface Area Heuristic" described in "Spatial Splits in Bounding Volume Hierarchies"
                    // https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
                    int triIndex = i + 1;
                    int count = triIndex - start;
                    float probHitLeftChild = leftBoxAccum.HalfArea() / parentArea;

                    float leftCost = probHitLeftChild * CostLeafNode(count, settings.TriangleCost);
                    float rightCost = rightCostsAccum[i + 1];

                    // Estimates cost of hitting parentNode if it was split at the evaluated split position.
                    // The full "Surface Area Heuristic" is recursive, but in practice we assume
                    // the resulting child nodes are leafs. This the greedy SAH approach
                    float surfaceAreaHeuristic = CostInternalNode(leftCost, rightCost);

                    if (surfaceAreaHeuristic < objectSplit.NewCost)
                    {
                        objectSplit.Pivot = triIndex;
                        objectSplit.Axis = axis;
                        objectSplit.NewCost = surfaceAreaHeuristic;
                        objectSplit.LeftBox = leftBoxAccum;
                        
                    }
                    else if (leftCost >= objectSplit.NewCost)
                    {
                        break;
                    }
                }
            }

            objectSplit.RightBox = Box.Empty();
            for (int i = objectSplit.Pivot; i < end; i++)
            {
                objectSplit.RightBox.GrowToFit(buildData.Fragments.Bounds[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]]);
            }

            return objectSplit;
        }

        private static SpatialSplit FindSpatialSplit(in Box parentBox, int start, int end, in Geometry geometry, in BuildData buildData, in BuildSettings settings)
        {
            SpatialSplit split = new SpatialSplit();
            split.NewCost = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float size = parentBox.Max[axis] - parentBox.Min[axis];
                if (size < 0.0001f)
                {
                    continue;
                }

                const int samples = 16;

                for (int i = 0; i < samples; i++)
                {
                    float position = parentBox.Min[axis] + size * ((i + 1.0f) / (samples + 1.0f));

                    Box leftBox = Box.Empty();
                    Box rightBox = Box.Empty();

                    int leftCounter = 0;
                    int rightCounter = 0;
                    for (int j = start; j < end; j++)
                    {
                        int fragmentId = buildData.FragmentIdsSortedOnAxis[axis][j];

                        Box fragmentBounds = buildData.Fragments.Bounds[fragmentId];
                        int triangleId = buildData.Fragments.OriginalTriIds[fragmentId];

                        bool completlyLeft = fragmentBounds.Max[axis] <= position;
                        bool completlyRight = fragmentBounds.Min[axis] >= position;
                        if (completlyLeft)
                        {
                            leftBox.GrowToFit(fragmentBounds);
                            leftCounter++;
                        }
                        else if (completlyRight)
                        {
                            rightBox.GrowToFit(fragmentBounds);
                            rightCounter++;
                        }
                        else
                        {
                            Triangle triangle = geometry.GetTriangle(triangleId);
                            (Box lSplittedBox, Box rSplittedBox) = triangle.Split(axis, position);
                            lSplittedBox.ShrinkToFit(fragmentBounds);
                            rSplittedBox.ShrinkToFit(fragmentBounds);

                            leftBox.GrowToFit(lSplittedBox);
                            rightBox.GrowToFit(rSplittedBox);

                            leftCounter++;
                            rightCounter++;
                        }
                    }

                    float areaParent = parentBox.HalfArea();
                    float probHitLeftChild = leftBox.HalfArea() / areaParent;
                    float probHitRightChild = rightBox.HalfArea() / areaParent;
                    float cost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(leftCounter, settings.TriangleCost), CostLeafNode(rightCounter, settings.TriangleCost));

                    if (cost < split.NewCost)
                    {
                        split.LeftBox = leftBox;
                        split.RightBox = rightBox;
                        split.LeftCount = leftCounter;
                        split.RightCount = rightCounter;

                        split.Axis = axis;
                        split.NewCost = cost;
                        split.Position = position;
                    }
                }
            }

            return split;
        }

        private static ValueTuple<GpuBlasNode, GpuBlasNode> ApplyObjectSplit(in BuildData buildData, int start, int end, in ObjectSplit objectSplit)
        {
            // We found a split axis where the triangles are partitioned into a left and right set.
            // Now, the other two axes also need to have the same triangles in their sets respectively.
            // To do that we mark every triangle on the left side of the split axis.
            // Then the other two axes have their triangles partitioned such that all marked triangles precede the others.
            // The partitioning is stable so the triangles stay sorted otherwise which is crucial

            int count = end - start;

            for (int i = start; i < objectSplit.Pivot; i++)
            {
                buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]] = true;
            }
            for (int i = objectSplit.Pivot; i < end; i++)
            {
                buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]] = false;
            }

            Span<int> partitionAux = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, end - objectSplit.Pivot);
            Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3].AsSpan(start, count), partitionAux, buildData.PartitionLeft);
            Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3].AsSpan(start, count), partitionAux, buildData.PartitionLeft);

            GpuBlasNode newLeftNode = new GpuBlasNode();
            newLeftNode.TriStartOrChild = start;
            newLeftNode.TriCount = objectSplit.Pivot - newLeftNode.TriStartOrChild;
            newLeftNode.SetBounds(objectSplit.LeftBox);

            GpuBlasNode newRightNode = new GpuBlasNode();
            newRightNode.TriStartOrChild = objectSplit.Pivot;
            newRightNode.TriCount = end - newLeftNode.TriEnd;
            newRightNode.SetBounds(objectSplit.RightBox);

            return (newLeftNode, newRightNode);
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild)
        {
            return TRAVERSAL_COST + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
        }

        private static float CostInternalNode(float costLeft, float costRight)
        {
            return TRAVERSAL_COST + costLeft + costRight;
        }

        private static float CostLeafNode(int numTriangles, float triangleCost)
        {
            return numTriangles * triangleCost;
        }

        [InlineArray(3)]
        public struct FragmentsAxesSorted
        {
            private int[] _element;
        }
    }
}