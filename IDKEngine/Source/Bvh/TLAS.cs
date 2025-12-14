using System;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh;

/// <summary>
/// Simple serial implementation of "PLOC" in 
/// "Parallel Locally-Ordered Clustering for Bounding Volume Hierarchy Construction"
/// https://meistdan.github.io/publications/ploc/paper.pdf
/// </summary>
public static class TLAS
{
    public record struct BuildSettings
    {
        public int SearchRadius = 15;

        public BuildSettings()
        {
        }
    }

    public delegate Box FuncGetPrimitive(int primId);
    public delegate void FuncGetBlasAndGeometry(int instanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out int triangleOffset, out Matrix4 invWorldTransform);

    public static void Build(Span<GpuTlasNode> nodes, FuncGetPrimitive funcGetLeaf, int primitiveCount, BuildSettings buildSettings)
    {
        if (nodes.Length == 0) return;

        GpuTlasNode[] tempNodes = new GpuTlasNode[nodes.Length];

        {
            // Create all leaf nodes at the end of the nodes array
            Span<GpuTlasNode> leafNodes = tempNodes.AsSpan(nodes.Length - primitiveCount, primitiveCount);

            Box globalBounds = Box.Empty();
            for (int i = 0; i < leafNodes.Length; i++)
            {
                Box bounds = funcGetLeaf(i);
                globalBounds.GrowToFit(bounds);

                GpuTlasNode newNode = new GpuTlasNode();
                newNode.SetBounds(bounds);
                newNode.IsLeaf = true;
                newNode.ChildOrInstanceID = (uint)i;

                leafNodes[i] = newNode;
            }

            // Sort the leaf nodes based on their position converted to a morton code.
            // That means nodes which are spatially close will also be close in memory.
            Span<GpuTlasNode> output = nodes.Slice(nodes.Length - primitiveCount, primitiveCount);
            Algorithms.RadixSort(leafNodes, output, new LambdaSortNodes(globalBounds));
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
                int searchStart = Math.Max(nodeAId - buildSettings.SearchRadius, activeRangeStart);
                int searchEnd = Math.Min(nodeAId + buildSettings.SearchRadius + 1, activeRangeEnd);
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

                        ref GpuTlasNode nodeA = ref tempNodes[mergedNodesHead + 0];
                        ref GpuTlasNode nodeB = ref tempNodes[mergedNodesHead + 1];

                        Box mergedBox = Box.From(Conversions.ToBox(nodeA), Conversions.ToBox(nodeB));

                        GpuTlasNode newNode = new GpuTlasNode();
                        newNode.SetBounds(mergedBox);
                        newNode.IsLeaf = false;
                        newNode.ChildOrInstanceID = (uint)mergedNodesHead;

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
    }

    public static bool Intersect(
        ReadOnlySpan<GpuTlasNode> tlasNodes,
        FuncGetBlasAndGeometry funcGetBlasAndGeometry,
        Ray ray, out BVH.RayHitInfo hitInfo, float tMax = float.MaxValue)
    {
        hitInfo = new BVH.RayHitInfo();
        hitInfo.T = tMax;

        if (tlasNodes.Length == 0) return false;

        int stackPtr = 0;
        int stackTop = 0;
        Span<int> stack = stackalloc int[128];
        while (true)
        {
            ref readonly GpuTlasNode parent = ref tlasNodes[stackTop];
            if (parent.IsLeaf)
            {
                int blasInstanceId = (int)parent.ChildOrInstanceID;
                funcGetBlasAndGeometry(blasInstanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out int triangleOffset, out Matrix4 invWorldTransform);

                Ray localRay = ray.Transformed(invWorldTransform);
                if (BLAS.Intersect(blas, geometry, localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                {
                    hitInfo.Bary = blasHitInfo.Bary;
                    hitInfo.T = blasHitInfo.T;
                    hitInfo.BlasInstanceId = blasInstanceId;
                    hitInfo.TriangleId = triangleOffset + blasHitInfo.TriangleId;
                }

                if (stackPtr == 0) break;
                stackTop = stack[--stackPtr];
                continue;
            }

            int leftNodeId = (int)parent.ChildOrInstanceID;
            int rightNodeId = leftNodeId + 1;
            ref readonly GpuTlasNode leftNode = ref tlasNodes[leftNodeId];
            ref readonly GpuTlasNode rightNode = ref tlasNodes[rightNodeId];

            bool traverseLeft = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float _) && tMinLeft <= hitInfo.T;
            bool traverseRight = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out float _) && tMinRight <= hitInfo.T;

            if (traverseLeft || traverseRight)
            {
                if (traverseLeft && traverseRight)
                {
                    bool leftCloser = tMinLeft < tMinRight;
                    stackTop = leftCloser ? leftNodeId : rightNodeId;
                    stack[stackPtr++] = leftCloser ? rightNodeId : leftNodeId;
                }
                else
                {
                    stackTop = traverseLeft ? leftNodeId : rightNodeId;
                }
            }
            else
            {
                if (stackPtr == 0) break;
                stackTop = stack[--stackPtr];
            }
        }

        return hitInfo.T != tMax;
    }

    public static unsafe void Intersect(
        ReadOnlySpan<GpuTlasNode> tlasNodes,
        FuncGetBlasAndGeometry funcGetBlasAndGeometry,
        Box box, BVH.FuncIntersectLeafNode intersectFunc)
    {
        if (tlasNodes.Length == 0) return;

        int stackPtr = 0;
        int stackTop = 0;
        Span<int> stack = stackalloc int[128];
        while (true)
        {
            ref readonly GpuTlasNode parent = ref tlasNodes[stackTop];
            if (parent.IsLeaf)
            {
                int blasInstanceId = (int)parent.ChildOrInstanceID;
                funcGetBlasAndGeometry(blasInstanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out int triangleOffset, out Matrix4 invWorldTransform);

                Box localBox = Box.Transformed(box, invWorldTransform);
                BLAS.Intersect(blas, geometry, localBox, (int triangleId) =>
                {
                    BVH.BoxHitInfo hitInfo = new BVH.BoxHitInfo();
                    hitInfo.TriangleId = triangleOffset + triangleId;
                    hitInfo.BlasInstanceId = blasInstanceId;

                    return intersectFunc(hitInfo);
                });

                if (stackPtr == 0) break;
                stackTop = stack[--stackPtr];
                continue;
            }

            int leftNodeId = (int)parent.ChildOrInstanceID;
            int rightNodeId = leftNodeId + 1;
            ref readonly GpuTlasNode leftNode = ref tlasNodes[leftNodeId];
            ref readonly GpuTlasNode rightNode = ref tlasNodes[rightNodeId];

            bool traverseLeft = Intersections.BoxVsBox(Conversions.ToBox(leftNode), box);
            bool traverseRight = Intersections.BoxVsBox(Conversions.ToBox(rightNode), box);

            if (traverseLeft || traverseRight)
            {
                stackTop = traverseLeft ? leftNodeId : rightNodeId;
                if (traverseLeft && traverseRight)
                {
                    stack[stackPtr++] = rightNodeId;
                }
            }
            else
            {
                if (stackPtr == 0) break;
                stackTop = stack[--stackPtr];
            }
        }
    }

    public static GpuTlasNode[] AllocateRequiredNodes(int leafNodesCount)
    {
        return new GpuTlasNode[Math.Max(2 * leafNodesCount - 1, 0)];
    }

    private static int FindBestMatch(ReadOnlySpan<GpuTlasNode> nodes, int start, int end, int nodeIndex)
    {
        float smallestArea = float.MaxValue;
        int bestNodeIndex = -1;

        ref readonly GpuTlasNode node = ref nodes[nodeIndex];

        Box nodeBox = Conversions.ToBox(node);

        for (int i = start; i < end; i++)
        {
            if (i == nodeIndex)
            {
                continue;
            }

            ref readonly GpuTlasNode otherNode = ref nodes[i];

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

    private record struct LambdaSortNodes : Algorithms.IRadixSortable<GpuTlasNode>
    {
        private readonly Box globalBounds;

        public LambdaSortNodes(Box globalBounds)
        {
            this.globalBounds = globalBounds;
        }

        public readonly uint GetKey(GpuTlasNode node)
        {
            Vector3 mapped = MyMath.MapToZeroOne((node.Max + node.Min) * 0.5f, globalBounds.Min, globalBounds.Max);
            uint mortonCode = MyMath.GetMortonCode30(mapped);

            return mortonCode;
        }
    }
}
