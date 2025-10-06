using System;
using System.Threading;
using System.Diagnostics;
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
            public int StopSplittingThreshold = 1;
            public int MaxLeafTriangleCount = 1;
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
            public float OverlapThreshold = 0.00001f;

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
            public readonly ref GpuBlasNode Root => ref Nodes[1];

            public Span<GpuBlasNode> Nodes;
            public int MaxTreeDepth;

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

            // For Sweep Spatial-Split evaluation
            public int[] SortedMin;
            public int[] SortedMax;
            public Box[] RightBoxesAccum;
            public int[] StraddlingIds;

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
            public bool FragmentsNeedSorting;
            public int Depth;
        }

        private record struct Bin
        {
            public Box Box = Box.Empty();
            public int Entry;
            public int Exit;

            public Bin()
            {
            }
        }

        public static Fragments GetFragments(in Geometry geometry, BuildSettings settings)
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
                int maxFragmentCount = geometry.TriangleCount + (int)(geometry.TriangleCount * settings.SBVH.SplitFactor);
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
            buildData.SortedMax = new int[fragments.ReservedSpace];
            buildData.SortedMin = new int[fragments.ReservedSpace];
            buildData.RightBoxesAccum = new Box[fragments.ReservedSpace];
            buildData.StraddlingIds = new int[fragments.ReservedSpace];

            for (int axis = 0; axis < 3; axis++)
            {
                Span<int> output = buildData.FragmentIdsSortedOnAxis[axis].AsSpan(0, fragments.Count);
                Helper.FillIncreasing(output);
            }

            return buildData;
        }

        public static int Build(ref BuildResult blas, in Geometry geometry, ref BuildData buildData, BuildSettings settings)
        {
            int nodesUsed = 0;

            // Add padding to make left children 64 byte aligned.
            // This increases performance on SponzaMerged: 128fps->133fps.
            blas.Nodes[nodesUsed++] = new GpuBlasNode();

            ref GpuBlasNode rootNode = ref blas.Nodes[nodesUsed++];
            rootNode.TriStartOrChild = 0;
            rootNode.TriCount = buildData.Fragments.Count;
            rootNode.SetBounds(ComputeBoundingBox(rootNode.TriStartOrChild, rootNode.TriCount, buildData));

            float rootArea = rootNode.HalfArea();

            int stackPtr = 0;
            Span<BuildStackItem> stack = stackalloc BuildStackItem[64];
            stack[stackPtr++] = new BuildStackItem() { ParentNodeId = 1, ReservedSpaceBoundary = buildData.Fragments.ReservedSpace, FragmentsNeedSorting = true, Depth = 0 };
            while (stackPtr > 0)
            {
                ref readonly BuildStackItem buildStackItem = ref stack[--stackPtr];
                ref GpuBlasNode parentNode = ref blas.Nodes[buildStackItem.ParentNodeId];

                if (parentNode.TriCount <= settings.StopSplittingThreshold || parentNode.HalfArea() == 0.0f)
                {
                    continue;
                }

                if (buildStackItem.FragmentsNeedSorting)
                {
                    // Sweep-SAH object split search requires sorted fragments. If spatial split added new fragments we need to sort again
                    for (int axis = 0; axis < 3; axis++)
                    {
                        Span<int> input = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, parentNode.TriCount);
                        Span<int> output = buildData.FragmentIdsSortedOnAxis[axis].AsSpan(parentNode.TriStartOrChild, parentNode.TriCount);
                        Box[] bounds = buildData.Fragments.Bounds;

                        output.CopyTo(input);
                        Algorithms.RadixSort(input, output, i =>
                        {
                            float centerAxis = (bounds[i].Min[axis] + bounds[i].Max[axis]) * 0.5f;
                            return Algorithms.FloatToKey(centerAxis);
                        });
                    }
                }

                bool parentIsLeft = parentNode.TriEnd <= buildStackItem.ReservedSpaceBoundary;
                int reservedStart = parentIsLeft ? parentNode.TriStartOrChild : buildStackItem.ReservedSpaceBoundary;
                int reservedEnd = parentIsLeft ? buildStackItem.ReservedSpaceBoundary : parentNode.TriEnd;
                int reservedCount = reservedEnd - reservedStart;

                Box parentBox = Conversions.ToBox(parentNode);
                ObjectSplit objectSplit = FindObjectSplit(parentBox, parentNode.TriStartOrChild, parentNode.TriEnd, buildData, settings);

                SpatialSplit spatialSplit = new SpatialSplit() { NewCost = float.MaxValue };
                if (settings.SBVH.Enabled)
                {
                    float overlap = MyMath.HalfArea(objectSplit.LeftBox.Max - objectSplit.RightBox.Min);
                    float percentOverlap = overlap / rootArea;
                    bool aboveThreshold = percentOverlap > settings.SBVH.OverlapThreshold;

                    bool spaceForNew = reservedCount > parentNode.TriCount;

                    if (spaceForNew && aboveThreshold)
                    {
                        //spatialSplit = FindSpatialSplitSweep(1.0f / parentBox.HalfArea(), parentNode.TriStartOrChild, parentNode.TriEnd, geometry, buildData, settings);
                        spatialSplit = FindSpatialSplit(parentBox, parentNode.TriStartOrChild, parentNode.TriEnd, geometry, buildData, settings);
                    }
                }

                bool useSpatialSplit = spatialSplit.NewCost < objectSplit.NewCost;
                int remainingSpaceAfterSplit = reservedCount - (useSpatialSplit ? spatialSplit.Count : parentNode.TriCount);
                if (useSpatialSplit && (remainingSpaceAfterSplit < 0 || spatialSplit.LeftCount == 0 || spatialSplit.RightCount == 0))
                {
                    useSpatialSplit = false;
                    remainingSpaceAfterSplit = reservedCount - parentNode.TriCount;
                }

                float notSplitCost = parentNode.TriCount * settings.TriangleCost;
                float splitCost = useSpatialSplit ? spatialSplit.NewCost : objectSplit.NewCost;
                if (splitCost >= notSplitCost && parentNode.TriCount <= settings.MaxLeafTriangleCount)
                {
                    continue;
                }

                GpuBlasNode newLeftNode = new GpuBlasNode();
                GpuBlasNode newRightNode = new GpuBlasNode();

                if (!useSpatialSplit)
                {
                    for (int i = parentNode.TriStartOrChild; i < objectSplit.Pivot; i++)
                    {
                        buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]] = true;
                    }
                    for (int i = objectSplit.Pivot; i < parentNode.TriEnd; i++)
                    {
                        buildData.PartitionLeft[buildData.FragmentIdsSortedOnAxis[objectSplit.Axis][i]] = false;
                    }

                    Span<int> partitionAux = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, parentNode.TriEnd - objectSplit.Pivot);
                    Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 1) % 3].AsSpan(parentNode.TriStartOrChild, parentNode.TriCount), partitionAux, buildData.PartitionLeft);
                    Algorithms.StablePartition(buildData.FragmentIdsSortedOnAxis[(objectSplit.Axis + 2) % 3].AsSpan(parentNode.TriStartOrChild, parentNode.TriCount), partitionAux, buildData.PartitionLeft);

                    newLeftNode.TriStartOrChild = parentNode.TriStartOrChild;
                    newLeftNode.TriCount = objectSplit.Pivot - parentNode.TriStartOrChild;
                    newLeftNode.SetBounds(objectSplit.LeftBox);

                    newRightNode.TriStartOrChild = objectSplit.Pivot;
                    newRightNode.TriCount = parentNode.TriEnd - objectSplit.Pivot;
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
                    int leftCounter = 0;
                    int rightCounter = 0;
                    int outputAxis = (spatialSplit.Axis + 1) % 3;
                    for (int i = parentNode.TriStartOrChild; i < parentNode.TriEnd; i++)
                    {
                        int fragmentId = buildData.FragmentIdsSortedOnAxis[spatialSplit.Axis][i];

                        Box fragmentBounds = buildData.Fragments.Bounds[fragmentId];
                        int triangleId = buildData.Fragments.OriginalTriIds[fragmentId];

                        bool fullyLeft = fragmentBounds.Max[spatialSplit.Axis] <= spatialSplit.Position;
                        bool fullyRight = fragmentBounds.Min[spatialSplit.Axis] >= spatialSplit.Position;
                        if (fullyLeft)
                        {
                            buildData.FragmentIdsSortedOnAxis[outputAxis][reservedStart + leftCounter] = fragmentId;
                            leftCounter++;
                        }
                        else if (fullyRight)
                        {
                            buildData.FragmentIdsSortedOnAxis[outputAxis][reservedEnd - rightCounter - 1] = fragmentId;
                            rightCounter++;
                        }
                        else
                        {
                            Box putInLeftBox = spatialSplit.LeftBox;
                            Box putInRightBox = spatialSplit.RightBox;

                            putInLeftBox.GrowToFit(fragmentBounds);
                            putInRightBox.GrowToFit(fragmentBounds);

                            float straddlingCost = spatialSplit.LeftBox.HalfArea() * spatialSplit.LeftCount + spatialSplit.RightBox.HalfArea() * spatialSplit.RightCount;
                            float putInLeftCost = putInLeftBox.HalfArea() * spatialSplit.LeftCount + spatialSplit.RightBox.HalfArea() * (spatialSplit.RightCount - 1);
                            float putInRightCost = spatialSplit.LeftBox.HalfArea() * (spatialSplit.LeftCount - 1) + putInRightBox.HalfArea() * spatialSplit.RightCount;

                            if (straddlingCost < putInLeftCost && straddlingCost < putInRightCost)
                            {
                                Triangle triangle = geometry.GetTriangle(triangleId);
                                (Box lSplittedBox, Box rSplittedBox) = triangle.Split(spatialSplit.Axis, spatialSplit.Position);
                                lSplittedBox.ShrinkToFit(fragmentBounds);
                                rSplittedBox.ShrinkToFit(fragmentBounds);

                                buildData.FragmentIdsSortedOnAxis[outputAxis][reservedStart + leftCounter++] = buildData.Fragments.Count;
                                buildData.Fragments.Add(lSplittedBox, triangleId);
                                    
                                buildData.FragmentIdsSortedOnAxis[outputAxis][reservedEnd - rightCounter++ - 1] = fragmentId;
                                buildData.Fragments.Bounds[fragmentId] = rSplittedBox;
                            }
                            else if (putInLeftCost < putInRightCost)
                            {
                                buildData.FragmentIdsSortedOnAxis[outputAxis][reservedStart + leftCounter++] = fragmentId;
                                buildData.Fragments.Bounds[fragmentId] = fragmentBounds;

                                spatialSplit.LeftBox.GrowToFit(fragmentBounds);
                            }
                            else
                            {
                                buildData.FragmentIdsSortedOnAxis[outputAxis][reservedEnd - rightCounter++ - 1] = fragmentId;
                                buildData.Fragments.Bounds[fragmentId] = fragmentBounds;

                                spatialSplit.RightBox.GrowToFit(fragmentBounds);
                            }
                        }
                    }

                    spatialSplit.LeftCount = leftCounter;
                    spatialSplit.RightCount = rightCounter;

                    // Reference unsplitting can lower the number of split triangles, so recompute
                    remainingSpaceAfterSplit = reservedCount - spatialSplit.Count;

                    if (spatialSplit.LeftCount == 0 || spatialSplit.RightCount == 0)
                    {
                        continue;
                    }

                    Debug.Assert(reservedStart + leftCounter <= (reservedEnd - rightCounter));
                    Array.Copy(buildData.FragmentIdsSortedOnAxis[outputAxis], reservedStart, buildData.FragmentIdsSortedOnAxis[(outputAxis + 1) % 3], reservedStart, leftCounter);
                    Array.Copy(buildData.FragmentIdsSortedOnAxis[outputAxis], reservedStart, buildData.FragmentIdsSortedOnAxis[(outputAxis + 2) % 3], reservedStart, leftCounter);
                    Array.Copy(buildData.FragmentIdsSortedOnAxis[outputAxis], reservedEnd - rightCounter, buildData.FragmentIdsSortedOnAxis[(outputAxis + 1) % 3], reservedEnd - rightCounter, rightCounter);
                    Array.Copy(buildData.FragmentIdsSortedOnAxis[outputAxis], reservedEnd - rightCounter, buildData.FragmentIdsSortedOnAxis[(outputAxis + 2) % 3], reservedEnd - rightCounter, rightCounter);

                    newLeftNode.TriStartOrChild = reservedStart;
                    newLeftNode.TriCount = leftCounter;
                    newLeftNode.SetBounds(spatialSplit.LeftBox);

                    newRightNode.TriStartOrChild = reservedEnd - rightCounter;
                    newRightNode.TriCount = rightCounter;
                    newRightNode.SetBounds(spatialSplit.RightBox);
                }

                float leftCost = newLeftNode.HalfArea() * newLeftNode.TriCount;
                float rightCost = newRightNode.HalfArea() * newRightNode.TriCount;

                // Don't accept zero areas, to prevent NaN
                if (leftCost + rightCost == 0.0f)
                {
                    continue;
                }

                float shareOfReservedSpace = leftCost / (leftCost + rightCost);
                int newReservedSpaceBoundary = newLeftNode.TriEnd + (int)(remainingSpaceAfterSplit * shareOfReservedSpace);

                int leftNodeId = nodesUsed + 0;
                int rightNodeId = nodesUsed + 1;

                blas.Nodes[leftNodeId] = newLeftNode;
                blas.Nodes[rightNodeId] = newRightNode;

                parentNode.TriStartOrChild = leftNodeId;
                parentNode.TriCount = 0;
                nodesUsed += 2;

                stack[stackPtr++] = new BuildStackItem() { ParentNodeId = rightNodeId, ReservedSpaceBoundary = newReservedSpaceBoundary, FragmentsNeedSorting = useSpatialSplit, Depth = buildStackItem.Depth + 1 };
                stack[stackPtr++] = new BuildStackItem() { ParentNodeId = leftNodeId, ReservedSpaceBoundary = newReservedSpaceBoundary, FragmentsNeedSorting = useSpatialSplit, Depth = buildStackItem.Depth + 1 };

                Debug.Assert(newRightNode.TriStartOrChild >= newLeftNode.TriEnd);
                Debug.Assert(newReservedSpaceBoundary >= newLeftNode.TriEnd);
                Debug.Assert(newReservedSpaceBoundary <= newRightNode.TriStartOrChild);
            }

            blas.MaxTreeDepth = ComputeTreeDepth(blas);

            return nodesUsed;
        }

        public static void Refit(in BuildResult blas, in Geometry geometry)
        {
            for (int i = blas.Nodes.Length - 1; i >= 1; i--)
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

        public static float ComputeGlobalSAH(in BuildResult blas, BuildSettings settings)
        {
            float cost = 0.0f;

            float rootArea = blas.Root.HalfArea();
            for (int i = 1; i < blas.Nodes.Length; i++)
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

        public static float ComputeGlobalArea(in BuildResult blas, BuildSettings settings)
        {
            float cost = 0.0f;

            float rootArea = blas.Root.HalfArea();
            for (int i = 1; i < blas.Nodes.Length; i++)
            {
                ref readonly GpuBlasNode node = ref blas.Nodes[i];
                float probHitNode = node.HalfArea() / rootArea;
                cost += probHitNode;
            }
            return cost;
        }

        public static float ComputeOverlap(in BuildResult blas)
        {
            float overlap = 0.0f;

            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 2;

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

        private static float ComputeEPOArea(in BuildResult blas, in Geometry geometry, int subtreeRootId)
        {
            if (subtreeRootId == 1)
            {
                return 0.0f;
            }

            Box subtreeBox = Conversions.ToBox(blas.Nodes[subtreeRootId]);

            float area = 0.0f;

            Span<int> stack = stackalloc int[64];
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

        public static float ComputeGlobalEPO(in BuildResult blas, in Geometry geometry, BuildSettings settings, int subtreeRootId = 1)
        {
            // https://users.aalto.fi/~ailat1/publications/aila2013hpg_paper.pdf
            // https://research.nvidia.com/sites/default/files/pubs/2013-09_On-Quality-Metrics/aila2013hpg_slides.pdf

            float totalArea = 0.0f;

            float cost = 0.0f;

            Span<int> stack = stackalloc int[64];
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

        public static bool Intersect(
            in BuildResult blas,
            in Geometry geometry,
            in Ray ray, out RayHitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = tMaxDist;

            Span<int> stack = stackalloc int[blas.MaxTreeDepth];
            int stackPtr = 0;
            int stackTop = 2;

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
            int stackTop = 2;

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

        public static GpuIndicesTriplet[] GetUnindexedTriangles(in BuildResult blas, in BuildData buildData, in Geometry geometry, BuildSettings buildSettings)
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
                stack[stackPtr++] = 2;
                while (stackPtr > 0)
                {
                    int stackTop = stack[--stackPtr];

                    ref GpuBlasNode leftChild = ref blas.Nodes[stackTop];
                    ref GpuBlasNode rightChild = ref blas.Nodes[stackTop + 1];

                    if (leftChild.IsLeaf && rightChild.IsLeaf)
                    {
                        Span<int> leftUniqueTriIds = GetUniqueTriIds(leftChild, buildData);
                        Span<int> rightUniqueTriIds = GetUniqueTriIds(rightChild, buildData);

                        int onlyLeftTriCount = 0;
                        int backwardsCounter = 0;
                        for (int i = 0; i < leftUniqueTriIds.Length; i++)
                        {
                            int leftTriId = leftUniqueTriIds[i];
                            bool isStraddling = rightUniqueTriIds.Contains(leftTriId);
                            if (isStraddling)
                            {
                                triangles[globalTriCounter + (leftUniqueTriIds.Length - backwardsCounter++ - 1)] = geometry.Triangles[leftTriId];
                            }
                            else
                            {
                                triangles[globalTriCounter + onlyLeftTriCount++] = geometry.Triangles[leftTriId];
                            }
                        }

                        int onlyRightTriCount = 0;
                        for (int i = 0; i < rightUniqueTriIds.Length; i++)
                        {
                            int rightTriId = rightUniqueTriIds[i];
                            bool isStraddling = leftUniqueTriIds.Contains(rightTriId);
                            if (!isStraddling)
                            {
                                triangles[globalTriCounter + leftUniqueTriIds.Length + onlyRightTriCount++] = geometry.Triangles[rightTriId];
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
            }
            else
            {
                for (int i = 2; i < blas.Nodes.Length; i++)
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
            stack[stackPtr++] = 2;

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
            parents[1] = -1;

            for (int i = 2; i < blas.Nodes.Length; i++)
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

        private static ObjectSplit FindObjectSplit(in Box parentBox, int start, int end, in BuildData buildData, BuildSettings settings)
        {
            ObjectSplit split = new ObjectSplit();

            split.Axis = 0;
            split.Pivot = 0;
            split.NewCost = float.MaxValue;

            // Unfortunately we have to manually load the fields for best perf
            // as the JIT otherwise repeatedly loads them in the loop
            // https://github.com/dotnet/runtime/issues/113107
            Span<float> rightCostsAccum = buildData.RightCostsAccum;
            Span<Box> fragBounds = buildData.Fragments.Bounds;
            Span<int> fragIdsSorted;

            for (int axis = 0; axis < 3; axis++)
            {
                fragIdsSorted = buildData.FragmentIdsSortedOnAxis[axis];

                Box rightBoxAccum = Box.Empty();
                int firstRightTri = start + 1;

                for (int i = end - 1; i >= firstRightTri; i--)
                {
                    rightBoxAccum.GrowToFit(fragBounds[fragIdsSorted[i]]);

                    int fragCount = end - i;
                    float rightCost = rightBoxAccum.HalfArea() * fragCount;

                    rightCostsAccum[i] = rightCost;

                    if (rightCost >= split.NewCost)
                    {
                        // Don't need to consider split positions beyond this point as cost is already greater and will only get more
                        firstRightTri = i + 1;
                        break;
                    }
                }

                Box leftBoxAccum = Box.Empty();
                for (int i = start; i < firstRightTri - 1; i++)
                {
                    leftBoxAccum.GrowToFit(fragBounds[fragIdsSorted[i]]);
                }
                for (int i = firstRightTri - 1; i < end - 1; i++)
                {
                    leftBoxAccum.GrowToFit(fragBounds[fragIdsSorted[i]]);

                    // Implementation of "Surface Area Heuristic" described in "Spatial Splits in Bounding Volume Hierarchies"
                    // https://www.nvidia.in/docs/IO/77714/sbvh.pdf 2.1 BVH Construction
                    int fragIndex = i + 1;
                    int fragCount = fragIndex - start;

                    float leftCost = leftBoxAccum.HalfArea() * fragCount;
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
                        split.LeftBox = leftBoxAccum;
                        
                    }
                    else if (leftCost >= split.NewCost)
                    {
                        break;
                    }
                }
            }
            split.NewCost = TRAVERSAL_COST + (settings.TriangleCost * split.NewCost / parentBox.HalfArea());

            fragIdsSorted = buildData.FragmentIdsSortedOnAxis[split.Axis];

            split.RightBox = Box.Empty();
            for (int i = split.Pivot; i < end; i++)
            {
                split.RightBox.GrowToFit(fragBounds[fragIdsSorted[i]]);
            }

            return split;
        }

        private static SpatialSplit FindSpatialSplit(in Box parentBox, int start, int end, in Geometry geometry, in BuildData buildData, BuildSettings settings)
        {
            const int binCount = 16;
            Span<Bin> bins = stackalloc Bin[binCount];
            Span<Box> accumBoxes = stackalloc Box[binCount];

            SpatialSplit split = new SpatialSplit();
            split.NewCost = float.MaxValue;

            Span<Box> fragBounds = buildData.Fragments.Bounds;
            Span<int> originalTriIds = buildData.Fragments.OriginalTriIds;

            for (int axis = 0; axis < 3; axis++)
            {
                float min = parentBox.Min[axis];
                float max = parentBox.Max[axis];
                float size = max - min;

                float binSize = size / binCount;

                // Skip if we don't have enough precision to differentiate between two bins
                if (min == min + binSize)
                {
                    continue;
                }

                float invSize = 1.0f / binSize;
                bins.Fill(new Bin());

                Span<int> fragIdsSorted = buildData.FragmentIdsSortedOnAxis[axis];
                for (int i = start; i < end; i++)
                {
                    int fragmentId = fragIdsSorted[i];
                    Box fragmentBounds = fragBounds[fragmentId];

                    int firstBin = Math.Clamp((int)(invSize * (fragmentBounds.Min[axis] - min)), 0, binCount - 1);
                    int lastBin = Math.Clamp((int)(invSize * (fragmentBounds.Max[axis] - min)), 0, binCount - 1);
                    bins[firstBin].Entry++;
                    bins[lastBin].Exit++;

                    if (firstBin == lastBin)
                    {
                        bins[firstBin].Box.GrowToFit(fragmentBounds);
                    }
                    else
                    {
                        Triangle triangle = geometry.GetTriangle(originalTriIds[fragmentId]);

                        Box curBox = fragmentBounds;
                        for (int j = firstBin; j < lastBin; j++)
                        {
                            float splitPos = min + (j + 1) * binSize;
                            (Box lSplittedBox, Box rSplittedBox) = triangle.Split(axis, splitPos);
                            lSplittedBox.ShrinkToFit(curBox);
                            bins[j].Box.GrowToFit(lSplittedBox);

                            curBox.ShrinkToFit(rSplittedBox);
                        }
                        bins[lastBin].Box.GrowToFit(curBox);
                    }
                }

                Box accumBox = Box.Empty();
                for (int i = binCount - 1; i >= 0; i--)
                {
                    accumBox.GrowToFit(bins[i].Box);
                    accumBoxes[i] = accumBox;
                }

                int leftCount = 0;
                int rightCount = end - start;
                accumBox = Box.Empty();
                for (int i = 0; i < binCount - 1; i++)
                {
                    ref readonly Bin bin = ref bins[i];

                    leftCount += bin.Entry;
                    rightCount -= bin.Exit;
                    accumBox.GrowToFit(bin.Box);

                    Box leftBox = accumBox;
                    Box rightBox = accumBoxes[i + 1];

                    float cost = leftBox.HalfArea() * leftCount + rightBox.HalfArea() * rightCount;

                    if (cost < split.NewCost)
                    {
                        split.LeftBox = leftBox;
                        split.RightBox = rightBox;
                        split.LeftCount = leftCount;
                        split.RightCount = rightCount;

                        split.Axis = axis;
                        split.NewCost = cost;
                        split.Position = min + (i + 1) * binSize;
                    }
                }
            }

            split.NewCost = TRAVERSAL_COST + (settings.TriangleCost * split.NewCost / parentBox.HalfArea());

            return split;
        }

        private static SpatialSplit FindSpatialSplitSweep(float invParentArea, int start, int end, in Geometry geometry, in BuildData buildData, BuildSettings settings)
        {
            SpatialSplit split = new SpatialSplit();
            split.NewCost = float.MaxValue;

            Box[] fragBounds = buildData.Fragments.Bounds;
            Span<int> fragOriginalTriIds = buildData.Fragments.OriginalTriIds;

            for (int axis = 0; axis < 3; axis++)
            {
                Span<int> fragIdsSorted = buildData.FragmentIdsSortedOnAxis[axis].AsSpan(start, end - start);
                Span<Box> boxesAccum = buildData.RightBoxesAccum.AsSpan(start, fragIdsSorted.Length);
                Span<int> sortedByMax = buildData.SortedMax.AsSpan(start, fragIdsSorted.Length);
                Span<int> sortedByMin = buildData.SortedMin.AsSpan(start, fragIdsSorted.Length);
                Span<int> straddlingIds = buildData.StraddlingIds; // should pick correct slice to make multi-thread compatible

                Span<int> temp = Helper.ReUseMemory<float, int>(buildData.RightCostsAccum, fragIdsSorted.Length);
                fragIdsSorted.CopyTo(temp);

                Algorithms.RadixSort(temp, sortedByMax, i => Algorithms.FloatToKey(fragBounds[i].Max[axis]));
                Algorithms.RadixSort(temp, sortedByMin, i => Algorithms.FloatToKey(fragBounds[i].Min[axis]));

                {
                    {
                        Box rightBoxAccum = Box.Empty();
                        for (int i = boxesAccum.Length - 1; i >= 1; i--)
                        {
                            rightBoxAccum.GrowToFit(fragBounds[sortedByMin[i]]);
                            boxesAccum[i] = rightBoxAccum;
                        }
                    }
                    Box leftBox = Box.Empty();
                    int fullyLeftHead = 0;
                    int straddlingSearchStart = 0;
                    int lastStraddlingCount = 0;
                    for (int i = 1; i < fragIdsSorted.Length; i++)
                    {
                        float splitPos = fragBounds[sortedByMin[i]].Min[axis];
                        float nextSplitPos = (i + 1) == fragIdsSorted.Length ? float.NaN : fragBounds[sortedByMin[i + 1]].Min[axis];
                        if (splitPos == nextSplitPos)
                        {
                            continue;
                        }

                        // Find all fragments which are fully left to the split position
                        while (fullyLeftHead < i)
                        {
                            Box box = fragBounds[sortedByMax[fullyLeftHead]];

                            if (box.Max[axis] < splitPos)
                            {
                                leftBox.GrowToFit(box);
                                fullyLeftHead++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        // By having sorted we always known how many fragments are left.
                        // We also computed the fragments which are fully left.
                        // The difference between the two are the remaining straddling fragments
                        int leftCounter = fullyLeftHead;
                        int rightCounter = fragIdsSorted.Length - i;
                        int straddlingCount = i - leftCounter;

                        // Every iteration we move one to the left which means some straddling fragments may become fully left.
                        // Remove the no longer straddling fragments by testing all again. Could probably be optimized... 
                        int straddlingCounter = 0;
                        for (int j = 0; j < lastStraddlingCount; j++)
                        {
                            int id = straddlingIds[j];
                            Box box = fragBounds[id];

                            if (box.Max[axis] >= splitPos)
                            {
                                straddlingIds[straddlingCounter++] = id;
                            }
                        }

                        // Iterate until we found all new straddling fragments, if there are any
                        int iter = straddlingSearchStart;
                        while (straddlingCounter < straddlingCount)
                        {
                            int id = sortedByMin[iter];
                            Box box = fragBounds[id];

                            if (box.Max[axis] >= splitPos)
                            {
                                straddlingIds[straddlingCounter++] = id;
                                straddlingSearchStart = iter + 1;
                            }
                            iter++;
                        }
                        lastStraddlingCount = straddlingCount;

                        // Split the current straddling fragments
                        Box rightBox = boxesAccum[i];
                        for (int j = 0; j < straddlingCount; j++)
                        {
                            int id = straddlingIds[j];
                            Box box = fragBounds[id];

                            if (splitPos == box.Max[axis])
                            {
                                leftBox.GrowToFit(box);
                                leftCounter++;
                                continue;
                            }
                            if (splitPos == box.Min[axis])
                            {
                                rightBox.GrowToFit(box);
                                rightCounter++;
                                continue;
                            }

                            Triangle triangle = geometry.GetTriangle(fragOriginalTriIds[id]);
                            (Box lSplittedBox, Box rSplittedBox) = triangle.Split(axis, splitPos);
                            lSplittedBox.ShrinkToFit(box);
                            rSplittedBox.ShrinkToFit(box);

                            leftBox.GrowToFit(lSplittedBox);
                            rightBox.GrowToFit(rSplittedBox);

                            rightCounter++;
                            leftCounter++;
                        }

                        float probHitLeftChild = leftBox.HalfArea() * invParentArea;
                        float probHitRightChild = rightBox.HalfArea() * invParentArea;
                        float cost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(leftCounter, settings.TriangleCost), CostLeafNode(rightCounter, settings.TriangleCost));

                        if (cost < split.NewCost)
                        {
                            split.LeftBox = leftBox;
                            split.RightBox = rightBox;
                            split.LeftCount = leftCounter;
                            split.RightCount = rightCounter;

                            split.Axis = axis;
                            split.NewCost = cost;
                            split.Position = splitPos;
                        }
                    }
                }

                {
                    {
                        Box leftBoxAccum = Box.Empty();
                        for (int i = 0; i < boxesAccum.Length - 1; i++)
                        {
                            leftBoxAccum.GrowToFit(fragBounds[sortedByMax[i]]);
                            boxesAccum[i] = leftBoxAccum;
                        }
                    }
                    Box rightBox = Box.Empty();
                    int fullyRightHead = fragIdsSorted.Length - 1;
                    int straddlingSearchStart = fragIdsSorted.Length - 1;
                    int lastStraddlingCount = 0;
                    for (int i = fragIdsSorted.Length - 2; i >= 0; i--)
                    {
                        float splitPos = fragBounds[sortedByMax[i]].Max[axis];
                        float nextSplitPos = i == 0 ? float.NaN : fragBounds[sortedByMax[i - 1]].Max[axis];
                        if (splitPos == nextSplitPos)
                        {
                            continue;
                        }

                        while (fullyRightHead > i)
                        {
                            Box box = fragBounds[sortedByMin[fullyRightHead]];

                            if (box.Min[axis] > splitPos)
                            {
                                rightBox.GrowToFit(box);
                                fullyRightHead--;
                            }
                            else
                            {
                                break;
                            }
                        }

                        int leftCounter = i + 1;
                        int rightCounter = fragIdsSorted.Length - fullyRightHead - 1;
                        int straddlingCount = fullyRightHead - i;

                        int straddlingCounter = 0;
                        for (int j = 0; j < lastStraddlingCount; j++)
                        {
                            int id = straddlingIds[j];
                            Box box = fragBounds[id];

                            if (box.Min[axis] <= splitPos)
                            {
                                straddlingIds[straddlingCounter++] = id;
                            }
                        }

                        int iter = straddlingSearchStart;
                        while (straddlingCounter < straddlingCount)
                        {
                            int id = sortedByMax[iter];
                            Box box = fragBounds[id];

                            if (box.Min[axis] <= splitPos)
                            {
                                straddlingIds[straddlingCounter++] = id;
                                straddlingSearchStart = iter - 1;
                            }
                            iter--;
                        }
                        lastStraddlingCount = straddlingCount;

                        Box leftBox = boxesAccum[i];
                        for (int j = 0; j < straddlingCount; j++)
                        {
                            int id = straddlingIds[j];
                            Box box = fragBounds[id];

                            if (splitPos == box.Max[axis])
                            {
                                leftBox.GrowToFit(box);
                                leftCounter++;
                                continue;
                            }
                            if (splitPos == box.Min[axis])
                            {
                                rightBox.GrowToFit(box);
                                rightCounter++;
                                continue;
                            }

                            Triangle triangle = geometry.GetTriangle(fragOriginalTriIds[id]);
                            (Box lSplittedBox, Box rSplittedBox) = triangle.Split(axis, splitPos);
                            lSplittedBox.ShrinkToFit(box);
                            rSplittedBox.ShrinkToFit(box);

                            leftBox.GrowToFit(lSplittedBox);
                            rightBox.GrowToFit(rSplittedBox);

                            rightCounter++;
                            leftCounter++;
                        }

                        float probHitLeftChild = leftBox.HalfArea() * invParentArea;
                        float probHitRightChild = rightBox.HalfArea() * invParentArea;
                        float cost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(leftCounter, settings.TriangleCost), CostLeafNode(rightCounter, settings.TriangleCost));

                        if (cost < split.NewCost)
                        {
                            split.LeftBox = leftBox;
                            split.RightBox = rightBox;
                            split.LeftCount = leftCounter;
                            split.RightCount = rightCounter;

                            split.Axis = axis;
                            split.NewCost = cost;
                            split.Position = splitPos;
                        }
                    }
                }
            }

            return split;
        }

        public static int BuildPLOC(ref BuildResult blas, Span<Box> fragBounds, int searchRadius = 15)
        {
            int primitiveCount = fragBounds.Length;

            Span<GpuBlasNode> nodes = blas.Nodes;
            if (nodes.Length == 0) return 0;

            GpuBlasNode[] tempNodes = new GpuBlasNode[nodes.Length];

            {
                // Create all leaf nodes at the end of the nodes array
                Span<GpuBlasNode> leafNodes = tempNodes.AsSpan(nodes.Length - primitiveCount, primitiveCount);

                Box globalBounds = Box.Empty();
                for (int i = 0; i < leafNodes.Length; i++)
                {
                    Box bounds = fragBounds[i];
                    globalBounds.GrowToFit(bounds);

                    GpuBlasNode newNode = new GpuBlasNode();
                    newNode.SetBounds(bounds);
                    newNode.TriCount = 1;
                    newNode.TriStartOrChild = i;

                    leafNodes[i] = newNode;
                }

                // Sort the leaf nodes based on their position converted to a morton code.
                // That means nodes which are spatially close will also be close in memory.
                Span<GpuBlasNode> output = nodes.Slice(nodes.Length - primitiveCount, primitiveCount);
                Algorithms.RadixSort(leafNodes, output, (GpuBlasNode node) =>
                {
                    Vector3 mapped = MyMath.MapToZeroOne((node.Max + node.Min) * 0.5f, globalBounds.Min, globalBounds.Max);
                    uint mortonCode = MyMath.GetMortonCode(mapped);

                    return mortonCode;
                });
            }

            int activeRangeCount = primitiveCount;
            int activeRangeEnd = nodes.Length;
            int[] preferedNbors = new int[activeRangeCount];
            while (activeRangeCount > 1)
            {
                int activeRangeStart = activeRangeEnd - activeRangeCount;

                // Find the nodeId each node prefers to merge with
                for (int i = 0; i < activeRangeCount; i++)
                {
                    int nodeAId = activeRangeStart + i;
                    int searchStart = Math.Max(nodeAId - searchRadius, activeRangeStart);
                    int searchEnd = Math.Min(nodeAId + searchRadius + 1, activeRangeEnd);
                    int nodeBId = FindBestMatch(nodes, searchStart, searchEnd, nodeAId);
                    int nodeBIdLocal = nodeBId - activeRangeStart;
                    preferedNbors[i] = nodeBIdLocal;
                }

                // Find number of merged tlasNodes in advance so we know where to insert new parent tlasNodes
                int mergedNodesCount = 0;
                for (int i = 0; i < activeRangeCount; i++)
                {
                    int nodeAIdLocal = i;
                    int nodeBIdLocal = preferedNbors[nodeAIdLocal];
                    int nodeCIdLocal = preferedNbors[nodeBIdLocal];

                    if (nodeAIdLocal == nodeCIdLocal && nodeAIdLocal < nodeBIdLocal)
                    {
                        mergedNodesCount += 2;
                    }
                }

                int unmergedNodesCount = activeRangeCount - mergedNodesCount;
                int newNodesCount = mergedNodesCount / 2;

                int mergedNodesHead = activeRangeEnd - mergedNodesCount;
                int newBegin = mergedNodesHead - unmergedNodesCount - newNodesCount;
                int unmergedNodesHead = newBegin;
                for (int i = 0; i < activeRangeCount; i++)
                {
                    int nodeAIdLocal = i;
                    int nodeBIdLocal = preferedNbors[nodeAIdLocal];
                    int nodeCIdLocal = preferedNbors[nodeBIdLocal];
                    int nodeAId = nodeAIdLocal + activeRangeStart;

                    if (nodeAIdLocal == nodeCIdLocal)
                    {
                        if (nodeAIdLocal < nodeBIdLocal)
                        {
                            int nodeBId = nodeBIdLocal + activeRangeStart;

                            tempNodes[mergedNodesHead + 0] = nodes[nodeAId];
                            tempNodes[mergedNodesHead + 1] = nodes[nodeBId];

                            ref GpuBlasNode nodeA = ref tempNodes[mergedNodesHead + 0];
                            ref GpuBlasNode nodeB = ref tempNodes[mergedNodesHead + 1];

                            Box mergedBox = Box.From(Conversions.ToBox(nodeA), Conversions.ToBox(nodeB));

                            GpuBlasNode newNode = new GpuBlasNode();
                            newNode.SetBounds(mergedBox);
                            newNode.TriStartOrChild = mergedNodesHead;

                            tempNodes[unmergedNodesHead] = newNode;

                            unmergedNodesHead++;
                            mergedNodesHead += 2;
                        }
                    }
                    else
                    {
                        tempNodes[unmergedNodesHead++] = nodes[nodeAId];
                    }
                }

                // Copy from temp into final array
                Memory.CopyElements(ref tempNodes[newBegin], ref nodes[newBegin], activeRangeEnd - newBegin);

                // For every merged pair, 2 tlasNodes become inactive and 1 new one gets active
                activeRangeCount -= mergedNodesCount / 2;
                activeRangeEnd -= mergedNodesCount;
            }

            // Add padding. This increases performance on SponaMerged: 128fps->133fps.
            nodes[0] = new GpuBlasNode();

            int nodesUsed = nodes.Length;
            if (nodesUsed == 2)
            {
                // The root should never be a leaf.
                // Handle this edge case by creating a leaf-pair

                blas.Nodes[2] = blas.Root;
                blas.Nodes[3] = blas.Root;

                blas.Root.TriCount = 0;
                blas.Root.TriStartOrChild = 2;

                nodesUsed = 4;
            }
            blas.MaxTreeDepth = ComputeTreeDepth(blas);

            return nodesUsed;
        }

        private static int FindBestMatch(ReadOnlySpan<GpuBlasNode> nodes, int start, int end, int nodeIndex)
        {
            float smallestArea = float.MaxValue;
            int bestNodeIndex = -1;

            ref readonly GpuBlasNode node = ref nodes[nodeIndex];

            Box nodeBox = Conversions.ToBox(node);

            for (int i = start; i < end; i++)
            {
                if (i == nodeIndex)
                {
                    continue;
                }

                ref readonly GpuBlasNode otherNode = ref nodes[i];

                Box mergedBox = Box.From(nodeBox, Conversions.ToBox(otherNode));

                float area = mergedBox.HalfArea();
                if (area < smallestArea)
                {
                    smallestArea = area;
                    bestNodeIndex = i;
                }
            }

            return bestNodeIndex;
        }

        private static float CostInternalNode(float probabilityHitLeftChild, float probabilityHitRightChild, float costLeftChild, float costRightChild)
        {
            return TRAVERSAL_COST + (probabilityHitLeftChild * costLeftChild + probabilityHitRightChild * costRightChild);
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