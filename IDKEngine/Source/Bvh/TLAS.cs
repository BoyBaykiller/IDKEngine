using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public class TLAS
    {
        public ref readonly GpuTlasNode Root => ref Nodes[0];

        public int SearchRadius;

        public GpuTlasNode[] Nodes;
        public readonly GpuMeshInstance[] MeshInstances;
        public readonly BBG.DrawElementsIndirectCommand[] DrawCommands;
        public readonly BLAS[] Blases;
        public TLAS(BLAS[] blases, BBG.DrawElementsIndirectCommand[] drawCommands, GpuMeshInstance[] meshInstances)
        {
            Blases = blases;
            DrawCommands = drawCommands;
            MeshInstances = meshInstances;

            Nodes = new GpuTlasNode[Math.Max(2 * meshInstances.Length - 1, 0)];
            SearchRadius = 15;
        }

        public void Build()
        {
            Span<GpuTlasNode> initialChildNodes = new Span<GpuTlasNode>(Nodes, Nodes.Length - MeshInstances.Length, MeshInstances.Length);

            // Place initial nodes at the end of array
            Box globalBounds = Box.Empty();
            for (int i = 0; i < initialChildNodes.Length; i++)
            {
                ref readonly GpuMeshInstance meshInstance = ref MeshInstances[i];
                BLAS blas = Blases[meshInstance.MeshIndex];

                Box worldSpaceBounds = Box.Transformed(Conversions.ToBox(blas.Root), meshInstance.ModelMatrix);
                globalBounds.GrowToFit(worldSpaceBounds);

                GpuTlasNode newNode = new GpuTlasNode();
                newNode.SetBox(worldSpaceBounds);
                newNode.IsLeaf = true;
                newNode.ChildOrInstanceID = (uint)i;

                initialChildNodes[i] = newNode;
            }

            // Sort the initial child nodes according to space filling morton curve.
            // That means nodes which are spatially close will also be close in memory.
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

            int activeRangeCount = MeshInstances.Length;
            int activeRangeEnd = Nodes.Length;
            GpuTlasNode[] tempNodes = new GpuTlasNode[Nodes.Length];
            int[] preferedNbors = new int[activeRangeCount];
            while (activeRangeCount > 1)
            {
                int activeRangeStart = activeRangeEnd - activeRangeCount;

                // Find the nodeId each node prefers to merge with
                for (int i = 0; i < activeRangeCount; i++)
                {
                    int nodeAId = activeRangeStart + i;
                    int searchStart = Math.Max(nodeAId - SearchRadius, activeRangeStart);
                    int searchEnd = Math.Min(nodeAId + SearchRadius + 1, activeRangeEnd);
                    int nodeBId = FindBestMatch(Nodes, searchStart, searchEnd, nodeAId);
                    int nodeBIdLocal = nodeBId - activeRangeStart;
                    preferedNbors[i] = nodeBIdLocal;
                }

                // Find number of merged nodes in advance so we know where to insert new parent nodes
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

                            tempNodes[mergedNodesHead + 0] = Nodes[nodeAId];
                            tempNodes[mergedNodesHead + 1] = Nodes[nodeBId];

                            ref GpuTlasNode nodeA = ref tempNodes[mergedNodesHead + 0];
                            ref GpuTlasNode nodeB = ref tempNodes[mergedNodesHead + 1];

                            Box mergedBox = Conversions.ToBox(nodeA);
                            mergedBox.GrowToFit(nodeB.Min);
                            mergedBox.GrowToFit(nodeB.Max);

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
                        tempNodes[unmergedNodesHead++] = Nodes[nodeAId];
                    }
                }

                // Copy from temp into final array
                Array.Copy(tempNodes, newBegin, Nodes, newBegin, activeRangeEnd - newBegin);

                // For every merged pair, 2 nodes become inactive and 1 new one gets active
                activeRangeCount -= mergedNodesCount / 2;
                activeRangeEnd -= mergedNodesCount;
            }
        }

        public bool Intersect(in Ray ray, out BVH.RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new BVH.RayHitInfo();
            hitInfo.T = tMax;
            if (Nodes.Length == 0)
            {
                return false;
            }

            int stackPtr = 0;
            uint stackTop = 0;
            Span<uint> stack = stackalloc uint[32];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref Nodes[stackTop];
                if (parent.IsLeaf)
                {
                    uint instanceID = parent.ChildOrInstanceID;
                    ref readonly GpuMeshInstance meshInstance = ref MeshInstances[instanceID];
                    BLAS blas = Blases[meshInstance.MeshIndex];

                    Ray localRay = ray.Transformed(MeshInstances[instanceID].InvModelMatrix);
                    if (blas.Intersect(localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.TriangleIndices = blasHitInfo.TriangleIndices;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;
                        hitInfo.MeshID = meshInstance.MeshIndex;
                        hitInfo.InstanceID = (int)instanceID;
                    }

                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                uint leftChild = parent.ChildOrInstanceID;
                uint rightChild = leftChild + 1;
                ref readonly GpuTlasNode leftNode = ref Nodes[leftChild];
                ref readonly GpuTlasNode rightNode = ref Nodes[rightChild];
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

        public void Intersect(in Box box, BVH.FuncIntersectLeafNode intersectFunc)
        {
            if (Nodes.Length == 0)
            {
                return;
            }

            int stackPtr = 0;
            uint stackTop = 0;
            Span<uint> stack = stackalloc uint[32];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref Nodes[stackTop];
                if (parent.IsLeaf)
                {
                    uint instanceID = parent.ChildOrInstanceID;
                    ref readonly GpuMeshInstance meshInstance = ref MeshInstances[instanceID];
                    BLAS blas = Blases[meshInstance.MeshIndex];

                    // Copy out/ref paramters for access from inside the lambda function. This is needed because of "CS1628 - Cannot use in ref or out parameter inside an anonymous method, lambda expression, or query expression."
                    int meshIndexCopy = meshInstance.MeshIndex;

                    Box localBox = Box.Transformed(box, meshInstance.InvModelMatrix);
                    blas.Intersect(localBox, (in BLAS.IndicesTriplet leafNodeTriangle) =>
                    {
                        BVH.BoxHitInfo hitInfo;
                        hitInfo.TriangleIndices = leafNodeTriangle;
                        hitInfo.MeshID = meshIndexCopy;
                        hitInfo.InstanceID = (int)instanceID;

                        intersectFunc(hitInfo);
                    });

                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                uint leftChild = parent.ChildOrInstanceID;
                uint rightChild = leftChild + 1;
                ref readonly GpuTlasNode leftNode = ref Nodes[leftChild];
                ref readonly GpuTlasNode rightNode = ref Nodes[rightChild];
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

        private static int FindBestMatch(ReadOnlySpan<GpuTlasNode> nodes, int start, int end, int nodeIndex)
        {
            float smallestArea = float.MaxValue;
            int bestNodeIndex = -1;

            ref readonly GpuTlasNode node = ref nodes[nodeIndex];
            for (int i = start; i < end; i++)
            {
                if (i == nodeIndex)
                {
                    continue;
                }

                ref readonly GpuTlasNode otherNode = ref nodes[i];

                Box fittingBox = Conversions.ToBox(node);
                fittingBox.GrowToFit(otherNode.Min);
                fittingBox.GrowToFit(otherNode.Max);

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
