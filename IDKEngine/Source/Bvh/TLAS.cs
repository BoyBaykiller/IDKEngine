using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class TLAS
    {
        public GpuTlasNode Root => Nodes[0];

        public int SearchRadius;

        public GpuMeshInstance[] MeshInstances;
        public BBG.DrawElementsIndirectCommand[] DrawCommands;
        public BLAS[] Blases;
        public GpuTlasNode[] Nodes;
        public TLAS(BLAS[] blases, BBG.DrawElementsIndirectCommand[] drawCommands, GpuMeshInstance[] meshInstances)
        {
            Blases = blases;
            DrawCommands = drawCommands;
            MeshInstances = meshInstances;

            Nodes = new GpuTlasNode[Math.Max(2 * meshInstances.Length - 1, 1)];
            SearchRadius = 15;
        }

        public void Build()
        {
            Span<GpuTlasNode> initialChildNodes = new Span<GpuTlasNode>(Nodes, Nodes.Length - MeshInstances.Length, MeshInstances.Length);

            // Place initial nodes at the end of array
            Box globalBounds = new Box(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < initialChildNodes.Length; i++)
            {
                ref readonly GpuMeshInstance meshInstance = ref MeshInstances[i];
                ref readonly BBG.DrawElementsIndirectCommand cmd = ref DrawCommands[meshInstance.MeshIndex];
                BLAS blas = Blases[meshInstance.MeshIndex];

                Box worldSpaceBounds = Box.Transformed(Conversions.ToBox(blas.Root), meshInstance.ModelMatrix);
                globalBounds.GrowToFit(worldSpaceBounds);

                GpuTlasNode newNode = new GpuTlasNode();
                newNode.Min = worldSpaceBounds.Min;
                newNode.Max = worldSpaceBounds.Max;
                newNode.IsLeaf = true;
                newNode.ChildOrInstanceID = (uint)i;

                initialChildNodes[i] = newNode;
            }

            // Sort the initial child nodes according to space filling morton curve.
            // That means nodes which are spatially close will also be close in memory.
            MemoryExtensions.Sort(initialChildNodes, (GpuTlasNode a, GpuTlasNode b) =>
            {
                Vector3 mappedA = MyMath.MapToZeroOne((a.Max + a.Min) * 0.5f, globalBounds.Min, globalBounds.Max);
                ulong mortonCodeA = MyMath.Morton3D(mappedA);

                Vector3 mappedB = MyMath.MapToZeroOne((b.Max + b.Min) * 0.5f, globalBounds.Min, globalBounds.Max);
                ulong mortonCodeB = MyMath.Morton3D(mappedB);

                if (mortonCodeA < mortonCodeB)
                {
                    return -1;
                }
                if (mortonCodeA > mortonCodeB)
                {
                    return 1;
                }

                return 0;
            });

            int activeRangeCount = MeshInstances.Length;
            int activeRangeEnd = Nodes.Length;
            GpuTlasNode[] tempNodes = new GpuTlasNode[Nodes.Length];
            while (activeRangeCount > 1)
            {
                // Find the nodeId each node prefers to merge with
                // TODO: When search radius is very low, like 5, we sometimes end up never finding a node, causing infinite loop. This shouldnt happen
                int[] preferedNbors = new int[activeRangeCount];
                for (int i = 0; i < preferedNbors.Length; i++)
                {
                    int baseOffset = activeRangeEnd - activeRangeCount;

                    int nodeAid = baseOffset + i;
                    int searchStart = Math.Max(nodeAid - SearchRadius, baseOffset);
                    int searchEnd = Math.Min(nodeAid + SearchRadius, activeRangeEnd);
                    int nodeBId = FindBestMatch(Nodes, searchStart, searchEnd, nodeAid);

                    preferedNbors[i] = nodeBId;
                }

                // Find number of merged nodes in advance so we know where to insert new parent nodes
                int mergedNodes = 0;
                for (int i = 0; i < activeRangeCount; i++)
                {
                    int baseOffset = activeRangeEnd - activeRangeCount;

                    int nodeAId = baseOffset + i;
                    int nodeAIdLocal = i;

                    int nodeBId = preferedNbors[nodeAIdLocal];
                    int nodeBIdLocal = nodeBId - baseOffset;

                    int nodeCId = preferedNbors[nodeBIdLocal];

                    if (nodeAId == nodeCId && nodeAId < nodeBId)
                    {
                        mergedNodes += 2;
                    }
                }

                // Merge nodes and create parents
                int mergedNodesHead = activeRangeEnd;
                int unmergedNodesHead = activeRangeEnd - mergedNodes;
                for (int i = 0; i < activeRangeCount; i++)
                {
                    int baseOffset = activeRangeEnd - activeRangeCount;

                    int nodeAId = baseOffset + i;
                    int nodeAIdLocal = i;

                    int nodeBId = preferedNbors[nodeAIdLocal];
                    int nodeBIdLocal = nodeBId - baseOffset;

                    int nodeCId = preferedNbors[nodeBIdLocal];
                    if (nodeAId == nodeCId)
                    {
                        if (nodeAId < nodeBId)
                        {
                            ref GpuTlasNode nodeB = ref tempNodes[--mergedNodesHead];
                            ref GpuTlasNode nodeA = ref tempNodes[--mergedNodesHead];

                            nodeB = Nodes[nodeBId];
                            nodeA = Nodes[nodeAId];

                            Box boundsFittingChildren = Conversions.ToBox(nodeA);
                            boundsFittingChildren.GrowToFit(nodeB.Min);
                            boundsFittingChildren.GrowToFit(nodeB.Max);

                            GpuTlasNode newNode = new GpuTlasNode();
                            newNode.Min = boundsFittingChildren.Min;
                            newNode.Max = boundsFittingChildren.Max;
                            newNode.IsLeaf = false;
                            newNode.ChildOrInstanceID = (uint)mergedNodesHead;

                            tempNodes[--unmergedNodesHead] = newNode;
                        }
                    }
                    else
                    {
                        tempNodes[--unmergedNodesHead] = Nodes[nodeAId];
                    }
                }
                Array.Copy(tempNodes, unmergedNodesHead, Nodes, unmergedNodesHead, activeRangeEnd - unmergedNodesHead);

                // For every merged pair, 2 nodes become inactive and 1 new one gets active
                activeRangeCount -= mergedNodes / 2;
                activeRangeEnd = mergedNodesHead;
            }
        }


        public bool Intersect(in Ray ray, out BVH.RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new BVH.RayHitInfo();
            hitInfo.T = tMax;

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
                ref readonly GpuTlasNode otherNode = ref nodes[i];

                if (i == nodeIndex)
                {
                    continue;
                }

                Box fittingBox = Conversions.ToBox(node);
                fittingBox.GrowToFit(otherNode.Min);
                fittingBox.GrowToFit(otherNode.Max);

                float area = MyMath.Area(fittingBox.Max - fittingBox.Min);
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
