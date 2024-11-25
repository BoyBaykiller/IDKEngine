using System;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public static class TLAS
    {
        public record struct BuildSettings
        {
            public int SearchRadius = 15;

            public BuildSettings()
            {
            }
        }

        public delegate void FuncGetBlas(int instanceId, out BLAS.BuildResult blas, out Matrix4 worldTransform);
        public delegate void FuncGetBlasAndGeometry(int instanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out Matrix4 invWorldTransform);

        public static void Build(Span<GpuTlasNode> nodes, FuncGetBlas funcGetBlas, int primitiveCount, BuildSettings buildSettings)
        {
            if (nodes.Length == 0) return;

            Span<GpuTlasNode> initialChildNodes = nodes.GetSpan(nodes.Length - primitiveCount, primitiveCount);

            // Place initial tlasNodes at the end of array
            Box globalBounds = Box.Empty();
            for (int i = 0; i < initialChildNodes.Length; i++)
            {
                funcGetBlas(i, out BLAS.BuildResult blas, out Matrix4 worldTransform);

                Box localBounds = Conversions.ToBox(blas.Root);
                Box worldSpaceBounds = Box.Transformed(localBounds, worldTransform);
                globalBounds.GrowToFit(worldSpaceBounds);

                GpuTlasNode newNode = new GpuTlasNode();
                newNode.SetBox(worldSpaceBounds);
                newNode.IsLeaf = true;
                newNode.ChildOrInstanceID = (uint)i;

                initialChildNodes[i] = newNode;
            }

            // Sort the initial child tlasNodes according to space filling morton curve.
            // That means tlasNodes which are spatially close will also be close in memory.
            MemoryExtensions.Sort(initialChildNodes, (GpuTlasNode a, GpuTlasNode b) =>
            {
                Vector3 mappedA = MyMath.MapToZeroOne((a.Max + a.Min) * 0.5f, globalBounds.Min, globalBounds.Max);
                ulong mortonCodeA = MyMath.GetMorton(mappedA);

                Vector3 mappedB = MyMath.MapToZeroOne((b.Max + b.Min) * 0.5f, globalBounds.Min, globalBounds.Max);
                ulong mortonCodeB = MyMath.GetMorton(mappedB);

                if (mortonCodeA > mortonCodeB) return 1;
                if (mortonCodeA == mortonCodeB) return 0;
                return -1;
            });

            int activeRangeCount = primitiveCount;
            int activeRangeEnd = nodes.Length;
            GpuTlasNode[] tempNodes = new GpuTlasNode[nodes.Length];
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

                            Box mergedBox = Conversions.ToBox(nodeA);
                            mergedBox.GrowToFit(Conversions.ToBox(nodeB));

                            GpuTlasNode newNode = new GpuTlasNode();
                            newNode.SetBox(mergedBox);
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
            in Ray ray, out BVH.RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new BVH.RayHitInfo();
            hitInfo.T = tMax;

            if (tlasNodes.Length == 0) return false;

            int stackPtr = 0;
            int stackTop = 0;
            Span<int> stack = stackalloc int[32];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref tlasNodes[stackTop];
                if (parent.IsLeaf)
                {
                    int instanceID = (int)parent.ChildOrInstanceID;
                    funcGetBlasAndGeometry(instanceID, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out Matrix4 invWorldTransform);

                    Ray localRay = ray.Transformed(invWorldTransform);
                    if (BLAS.Intersect(blas, geometry, localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.TriangleIndices = blasHitInfo.TriangleIndices;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;
                        hitInfo.InstanceID = instanceID;
                    }

                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                int leftChild = (int)parent.ChildOrInstanceID;
                int rightChild = leftChild + 1;
                ref readonly GpuTlasNode leftNode = ref tlasNodes[leftChild];
                ref readonly GpuTlasNode rightNode = ref tlasNodes[rightChild];
                bool leftChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(leftNode), out float tMinLeft, out float _) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, Conversions.ToBox(rightNode), out float tMinRight, out float _) && tMinRight <= hitInfo.T;

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? leftChild : rightChild;
                        stack[stackPtr++] = leftCloser ? rightChild : leftChild;
                    }
                    else
                    {
                        stackTop = leftChildHit ? leftChild : rightChild;
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

        public static void Intersect(
            ReadOnlySpan<GpuTlasNode> tlasNodes,
            FuncGetBlasAndGeometry funcGetBlasAndGeometry,
            in Box box, BVH.FuncIntersectLeafNode intersectFunc)
        {
            if (tlasNodes.Length == 0) return;

            int stackPtr = 0;
            int stackTop = 0;
            Span<int> stack = stackalloc int[32];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref tlasNodes[stackTop];
                if (parent.IsLeaf)
                {
                    int instanceID = (int)parent.ChildOrInstanceID;
                    funcGetBlasAndGeometry(instanceID, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out Matrix4 invWorldTransform);

                    Box localBox = Box.Transformed(box, invWorldTransform);
                    BLAS.Intersect(blas, geometry, localBox, (in BLAS.IndicesTriplet leafNodeTriangle) =>
                    {
                        BVH.BoxHitInfo hitInfo;
                        hitInfo.TriangleIndices = leafNodeTriangle;
                        hitInfo.InstanceID = instanceID;

                        return intersectFunc(hitInfo);
                    });

                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                int leftChild = (int)parent.ChildOrInstanceID;
                int rightChild = leftChild + 1;
                ref readonly GpuTlasNode leftNode = ref tlasNodes[leftChild];
                ref readonly GpuTlasNode rightNode = ref tlasNodes[rightChild];
                bool leftChildHit = Intersections.BoxVsBox(Conversions.ToBox(leftNode), box);
                bool rightChildHit = Intersections.BoxVsBox(Conversions.ToBox(rightNode), box);

                if (leftChildHit || rightChildHit)
                {
                    stackTop = leftChildHit ? leftChild : rightChild;
                    if (leftChildHit && rightChildHit)
                    {
                        stack[stackPtr++] = rightChild;
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

            // Doing the conversion here cuts 15ms with searchRadius=15!?
            Box nodeBox = Conversions.ToBox(node);

            for (int i = start; i < end; i++)
            {
                if (i == nodeIndex)
                {
                    continue;
                }

                ref readonly GpuTlasNode otherNode = ref nodes[i];

                Box fittingBox = nodeBox;
                fittingBox.GrowToFit(otherNode);

                float area = fittingBox.HalfArea();
                if (area < smallestArea)
                {
                    smallestArea = area;
                    bestNodeIndex = i;
                }
            }

            return bestNodeIndex;
        }
    }
}
