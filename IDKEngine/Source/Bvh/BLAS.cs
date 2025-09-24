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

            float globalArea = rootNode.HalfArea();

            int stackPtr = 0;
            Span<BuildStackItem> stack = stackalloc BuildStackItem[64];
            stack[stackPtr++] = new BuildStackItem() { ParentNodeId = 1, ReservedSpaceBoundary = buildData.Fragments.ReservedSpace, FragmentsNeedSorting = true };
            while (stackPtr > 0)
            {
                ref readonly BuildStackItem buildStackItem = ref stack[--stackPtr];
                ref GpuBlasNode parentNode = ref blas.Nodes[buildStackItem.ParentNodeId];

                Box parentBox = Conversions.ToBox(parentNode);

                if (parentNode.TriCount <= settings.MinLeafTriangleCount || parentBox.HalfArea() == 0.0f)
                {
                    continue;
                }

                if (buildStackItem.FragmentsNeedSorting)
                {
                    // FindObjectSplit method requires sorted fragments.
                    // If spatial split added new fragments we need to sort again

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

                float invParentArea = 1.0f / parentBox.HalfArea();

                ObjectSplit objectSplit = FindObjectSplit(invParentArea, parentNode.TriStartOrChild, parentNode.TriEnd, buildData, settings);
                    
                SpatialSplit spatialSplit = new SpatialSplit() { NewCost = float.MaxValue };
                if (settings.SBVH.Enabled)
                {
                    float overlap = Box.GetOverlappingHalfArea(objectSplit.LeftBox, objectSplit.RightBox);
                    float percentOverlap = overlap / globalArea;
                    if (percentOverlap > settings.SBVH.OverlapTreshold)
                    {
                        //spatialSplit = FindSpatialSplit(invParentArea, parentNode.TriStartOrChild, parentNode.TriEnd, geometry, buildData, settings);
                        spatialSplit = FindSpatialSplit(parentBox, parentNode.TriStartOrChild, parentNode.TriEnd, geometry, buildData, settings);

                        bool debug = parentBox.HalfArea() > 370.0f;
                        if (debug)
                        {
                            Console.WriteLine($"area = {parentBox.HalfArea()} used = {nodesUsed} cost = {spatialSplit.NewCost}");
                        }

                    }
                }

                bool useSpatialSplit = spatialSplit.NewCost < objectSplit.NewCost; // spatialSplit.NewCost < objectSplit.NewCost;
                {
                    float notSplitCost = CostLeafNode(parentNode.TriCount, settings.TriangleCost);
                    float splitCost = useSpatialSplit ? spatialSplit.NewCost : objectSplit.NewCost;
                    if (splitCost >= notSplitCost)
                    {
                        if (parentNode.TriCount <= settings.MaxLeafTriangleCount)
                        {
                            continue;
                        }
                        else
                        {
                            // Spatial splits aren't worth it at the bottom and we just want to reach MaxLeafTriangleCount fast.
                            // Chances are we dont have the necessary split budget at this point anyway
                            useSpatialSplit = false;
                        }
                    }
                }

                bool parentIsLeft = parentNode.TriEnd <= buildStackItem.ReservedSpaceBoundary;
                int reservedStart = parentIsLeft ? parentNode.TriStartOrChild : buildStackItem.ReservedSpaceBoundary;
                int reservedEnd = parentIsLeft ? buildStackItem.ReservedSpaceBoundary : parentNode.TriEnd;
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

                    // See Figure 1. in "Parallel Spatial Splits in Bounding Volume Hierarchies"
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
                    for (int i = parentNode.TriStartOrChild; i < parentNode.TriEnd; i++)
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
                                float probHitLeftChild = spatialSplit.LeftBox.HalfArea() * invParentArea;
                                float probHitRightChild = spatialSplit.RightBox.HalfArea() * invParentArea;
                                straddlingCost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(spatialSplit.LeftCount, settings.TriangleCost), CostLeafNode(spatialSplit.RightCount, settings.TriangleCost));
                            }
                            float putInLeftCost;
                            {
                                float probHitLeftChild = putInLeftBox.HalfArea() * invParentArea;
                                float probHitRightChild = spatialSplit.RightBox.HalfArea() * invParentArea;
                                putInLeftCost = CostInternalNode(probHitLeftChild, probHitRightChild, CostLeafNode(spatialSplit.LeftCount, settings.TriangleCost), CostLeafNode(spatialSplit.RightCount - 1, settings.TriangleCost));
                            }
                            float putInRightCost;
                            {
                                float probHitLeftChild = spatialSplit.LeftBox.HalfArea() * invParentArea;
                                float probHitRightChild = putInRightBox.HalfArea() * invParentArea;
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
                                }
                                else
                                {
                                    buildData.FragmentIdsSortedOnAxis[(spatialSplit.Axis + 1) % 3][rightDest + rightCounter] = fragmentId;
                                    buildData.Fragments.Bounds[fragmentId] = fragmentBounds;
                                    rightCounter++;

                                    spatialSplit.RightBox.GrowToFit(fragmentBounds);
                                }
                            }
                        }
                    }

                    if (spatialSplit.LeftCount == 0 || spatialSplit.RightCount == 0)
                    {
                        throw new Exception("uh no handle this");
                    }

                    Debug.Assert(leftDest + leftCounter <= rightDest);
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

                stack[stackPtr++] = new BuildStackItem() { ParentNodeId = rightNodeId, ReservedSpaceBoundary = newReservedSpaceBoundary, FragmentsNeedSorting = useSpatialSplit };
                stack[stackPtr++] = new BuildStackItem() { ParentNodeId = leftNodeId, ReservedSpaceBoundary = newReservedSpaceBoundary, FragmentsNeedSorting = useSpatialSplit };

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

        private static float EPOArea(in BuildResult blas, in Geometry geometry, int subtreeRoot, int nodeId = 1)
        {
            if (nodeId == subtreeRoot) return 0;

            ref readonly GpuBlasNode parent = ref blas.Nodes[nodeId];
            ref readonly GpuBlasNode subtree = ref blas.Nodes[subtreeRoot];

            float area = 0.0f;
            if (parent.IsLeaf)
            {
                // clip triangles to AABB of subtreeRoot and sum resulting areas

                Span<Vector3> vin = stackalloc Vector3[10];
                Span<Vector3> vout = stackalloc Vector3[10];
                Vector3 bmin = subtree.Min;
                Vector3 bmax = subtree.Max;

                for (int i = parent.TriStartOrChild; i < parent.TriEnd; i++)
                {
                    {
                        Triangle tri = geometry.GetTriangle(i);
                        vin[0] = tri[0];
                        vin[1] = tri[1];
                        vin[2] = tri[2];
                    }

                    // Sutherland-Hodgeman against six bounding planes
                    Vector3 C = new Vector3();
                    int Nin = 3;
                    for (int a = 0; a < 3; a++)
                    {
                        int Nout = 0;
                        float l = bmin[a], r = bmax[a];
                        for (int v = 0; v < Nin; v++)
                        {
                            Vector3 v0 = vin[v];
                            Vector3 v1 = vin[(v + 1) % Nin];
                            bool v0in = v0[a] >= l;
                            bool v1in = v1[a] >= l;

                            if (!(v0in || v1in))
                            {
                                continue;
                            }
                            else if (v0in ^ v1in)
                            {
                                C = v0 + (l - v0[a]) / (v1[a] - v0[a]) * (v1 - v0);
                                C[a] = l;
                                vout[Nout++] = C;
                            }

                            if (v1in)
                            {
                                vout[Nout++] = v1;
                            }
                        }

                        Nin = 0;
                        for (int v = 0; v < Nout; v++)
                        {
                            Vector3 v0 = vout[v];
                            Vector3 v1 = vout[(v + 1) % Nout];
                            bool v0in = v0[a] <= r;
                            bool v1in = v1[a] <= r;

                            if (!(v0in || v1in))
                            {
                                continue;
                            }
                            else if (v0in ^ v1in)
                            {
                                C = v0 + (r - v0[a]) / (v1[a] - v0[a]) * (v1 - v0);
                                C[a] = r;
                                vin[Nin++] = C;
                            }

                            if (v1in)
                            {
                                vin[Nin++] = v1;
                            }
                        }
                    }

                    if (Nin < 3)
                    {
                        continue;
                    }

                    {
                        // calculate area of remaining convex shape in vin
                        Triangle tri = new Triangle();
                        tri.Position0 = vin[0];
                        for (int j = 0; j < Nin - 2; j++)
                        {
                            tri[1] = vin[j + 1];
                            tri[2] = vin[j + 2];
                            area += tri.Area;
                        }
                    }
                }
                return area;
            }

            ref GpuBlasNode left = ref blas.Nodes[parent.TriStartOrChild];
            ref GpuBlasNode right = ref blas.Nodes[parent.TriStartOrChild + 1];

            if (Intersections.BoxVsBox(Conversions.ToBox(left), Conversions.ToBox(subtree)))
            {
                area += EPOArea(blas, geometry, subtreeRoot, parent.TriStartOrChild);
            }

            if (Intersections.BoxVsBox(Conversions.ToBox(right), Conversions.ToBox(subtree)))
            {
                area += EPOArea(blas, geometry, subtreeRoot, parent.TriStartOrChild + 1);
            }

            return area;
        }

        public static float ComputeGlobalEPO(in BuildResult blas, in Geometry geometry, BuildSettings settings, int nodeId = 1)
        {
            ref readonly GpuBlasNode node = ref blas.Nodes[nodeId];
            float area = EPOArea(blas,  geometry, nodeId);
            float cost = (node.IsLeaf ? (node.TriCount * settings.TriangleCost) : TRAVERSAL_COST) * area;

            if (!node.IsLeaf)
            {
                cost += ComputeGlobalEPO(blas, geometry, settings, node.TriStartOrChild);
                cost += ComputeGlobalEPO(blas, geometry, settings, node.TriStartOrChild + 1);
            }

            if (nodeId > 1)
            {
                return cost;
            }

            float totalArea = 0.0f;
            for (int i = 1; i < blas.Nodes.Length; i++)
            {
                ref readonly GpuBlasNode n = ref blas.Nodes[i];
                if (n.IsLeaf)
                {
                    for (int j = n.TriStartOrChild; j < n.TriEnd; j++)
                    {
                        Triangle triangle = geometry.GetTriangle(j);
                        totalArea += triangle.Area;
                    }
                }
            }
            cost /= totalArea;

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

        private static ObjectSplit FindObjectSplit(float invParentArea, int start, int end, BuildData buildData, BuildSettings settings)
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
                    float probHitRightChild = rightBoxAccum.HalfArea() * invParentArea;
                    float rightCost = probHitRightChild * fragCount;

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
                        split.LeftBox = leftBoxAccum;
                        
                    }
                    else if (leftCost >= split.NewCost)
                    {
                        break;
                    }
                }
            }
            split.NewCost = TRAVERSAL_COST + (settings.TriangleCost * split.NewCost);

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
            SpatialSplit split = new SpatialSplit();
            split.NewCost = float.MaxValue;

            for (int axis = 0; axis < 3; axis++)
            {
                float size = parentBox.Max[axis] - parentBox.Min[axis];
                if (size < 0.0001f)
                {
                    continue;
                }

                const int bins = 16;

                for (int i = 0; i < bins - 1; i++)
                {
                    float position = parentBox.Min[axis] + size * ((i + 1.0f) / (bins + 1.0f));

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

        private static SpatialSplit FindSpatialSplit(float invParentArea, int start, int end, in Geometry geometry, in BuildData buildData, BuildSettings settings)
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
                    int completlyLeftHead = 0;
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
                        while (completlyLeftHead < i)
                        {
                            Box box = fragBounds[sortedByMax[completlyLeftHead]];

                            if (box.Max[axis] < splitPos)
                            {
                                leftBox.GrowToFit(box);
                                completlyLeftHead++;
                            }
                            else
                            {
                                break;
                            }
                        }

                        // By having sorted we always known how many fragments are left.
                        // We also computed the fragments which are fully left.
                        // The difference between the two are the remaining straddling fragments
                        int leftCounter = completlyLeftHead;
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
                    int completlyRightHead = fragIdsSorted.Length - 1;
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

                        while (completlyRightHead > i)
                        {
                            Box box = fragBounds[sortedByMin[completlyRightHead]];

                            if (box.Min[axis] > splitPos)
                            {
                                rightBox.GrowToFit(box);
                                completlyRightHead--;
                            }
                            else
                            {
                                break;
                            }
                        }

                        int leftCounter = i + 1;
                        int rightCounter = fragIdsSorted.Length - completlyRightHead - 1;
                        int straddlingCount = completlyRightHead - i;

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