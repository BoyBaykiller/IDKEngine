using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class TLAS
    {
        public struct HitInfo
        {
            public GpuTriangle Triangle;
            public Vector3 Bary;
            public float T;
            public int MeshID;
            public int InstanceID;
        }

        public struct BlasInstances
        {
            public BLAS Blas;
            public ArraySegment<GpuMeshInstance> Instances;

            public void Deconstruct(out BLAS blas, out ArraySegment<GpuMeshInstance> instances)
            {
                blas = Blas;
                instances = Instances;
            }
        }

        public GpuTlasNode Root => Nodes[0];
        public Box RootBounds => new Box(Root.Min, Root.Max);
        public int TreeDepth { get; private set; }

        public List<BlasInstances> BlasesInstances;
        public GpuTlasNode[] Nodes;
        private int allBlasInstancesCount;
        public TLAS()
        {
            BlasesInstances = new List<BlasInstances>();
            Nodes = Array.Empty<GpuTlasNode>();
        }

        public void AddBlases(BlasInstances[] blasesInstances)
        {
            BlasesInstances.AddRange(blasesInstances);

            allBlasInstancesCount = BlasesInstances.Sum(blasInstances => blasInstances.Instances.Count);
            Array.Resize(ref Nodes, 2 * allBlasInstancesCount - 1);
            TreeDepth = (int)MathF.Ceiling(MathF.Log2(Nodes.Length)) + 2;
        }

        public void Build()
        {
            int nodesUsed = 0;

            // Flatten and transform local space blas instances into
            // world space tlas nodes. These nodes are the primitives of the the TLAS.
            {
                for (int i = 0; i < BlasesInstances.Count; i++)
                {
                    (BLAS blas, ArraySegment<GpuMeshInstance> instances) = BlasesInstances[i];
                    for (int j = 0; j < instances.Count; j++)
                    {
                        GpuTlasNode newNode;
                        Box worldSpaceBounds = Box.Transformed(new Box(blas.RootBounds.Min, blas.RootBounds.Max), instances[j].ModelMatrix);
                        newNode.Min = worldSpaceBounds.Min;
                        newNode.Max = worldSpaceBounds.Max;
                        newNode.LeftChild = 0;
                        newNode.BlasIndex = (uint)i;

                        int newNodeIndex = Nodes.Length - 1 - nodesUsed++;
                        Nodes[newNodeIndex] = newNode;
                    }
                }
            }

            // Build TLAS from generated child nodes.
            // Technique: Every two nodes with the lowest combined area form a parent node.
            //            Apply this scheme recursivly until only a single root node encompassing the entire scene is left.          
            //            This exact implementation doesnt run in optimal time complexity but can still be considered real time.    
            {
                int candidatesSearchCount = allBlasInstancesCount;
                int candidatesSearchCountBackup = candidatesSearchCount;
                int candidatesSearchStart = Nodes.Length - 1;
                while (candidatesSearchCount > 1)
                {
                    int nodeAId = candidatesSearchStart;
                    int nodeBId = FindBestMatch(candidatesSearchStart, candidatesSearchCount, nodeAId);
                    int nodeCId = FindBestMatch(candidatesSearchStart, candidatesSearchCount, nodeBId);

                    // Check if BestMatch(BestMatch(nodeA)) == nodeA, that is a pair of nodes which agree on each other as the
                    // best candiate to form the smallest possible bounding box out of all left over nodes
                    if (nodeAId == nodeCId)
                    {
                        // Move other child node next to nodeA in memory
                        MathHelper.Swap(ref Nodes[nodeAId - 1], ref Nodes[nodeBId]);
                        nodeBId = nodeAId - 1;

                        ref readonly GpuTlasNode nodeB = ref Nodes[nodeBId];
                        ref readonly GpuTlasNode nodeA = ref Nodes[nodeAId];

                        Box boundsFittingChildren = new Box(nodeA.Min, nodeA.Max);
                        boundsFittingChildren.GrowToFit(nodeB.Min);
                        boundsFittingChildren.GrowToFit(nodeB.Max);

                        GpuTlasNode newNode = new GpuTlasNode();
                        newNode.LeftChild = (uint)nodeBId;
                        newNode.Min = boundsFittingChildren.Min;
                        newNode.Max = boundsFittingChildren.Max;

                        int newNodeIndex = Nodes.Length - 1 - nodesUsed++;
                        Nodes[newNodeIndex] = newNode;

                        // By subtracting two, the two child nodes (nodeAId, nodeBId) will no longer be included in the search for potential candidates
                        candidatesSearchStart -= 2;

                        // Newly created parent should be included in the search for potential candidates which is why -1 (i know intuitively should be +1 but we are building in reverse)
                        candidatesSearchCount = candidatesSearchCountBackup - 1;
                        candidatesSearchCountBackup = candidatesSearchCount;
                    }
                    else
                    {
                        // If no pair was found we swap nodeAID out with the end node, and exclude nodeAID for future search until a pair is found.
                        MathHelper.Swap(ref Nodes[nodeAId], ref Nodes[nodeAId - --candidatesSearchCount]);
                    }
                }
            }
        }

        private int FindBestMatch(int start, int count, int nodeIndex)
        {
            float smallestArea = float.MaxValue;
            int bestNodeIndex = -1;

            ref readonly GpuTlasNode node = ref Nodes[nodeIndex];
            for (int i = start; i > start - count; i--)
            {
                if (i == nodeIndex)
                {
                    continue;
                }

                ref readonly GpuTlasNode otherNode = ref Nodes[i];

                Box fittingBox = new Box(node.Min, node.Max);
                fittingBox.GrowToFit(otherNode.Min);
                fittingBox.GrowToFit(otherNode.Max);

                float area = MyMath.HalfArea(fittingBox.Max - fittingBox.Min);
                if (area < smallestArea)
                {
                    smallestArea = area;
                    bestNodeIndex = i;
                }
            }

            return bestNodeIndex;
        }

        public static int debugMaxStack = 0;
        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new HitInfo();
            hitInfo.T = tMax;

            uint stackPtr = 0;
            uint stackTop = 0;
            uint* stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref Nodes[stackTop];
                if (parent.IsLeaf())
                {
                    BlasInstances blasInstances = BlasesInstances[(int)parent.BlasIndex];
                    BLAS blas = blasInstances.Blas;

                    int glInstanceID = 0; // TODO: Work out actual instanceID value
                    Ray localRay = ray.Transformed(blasInstances.Instances[glInstanceID].InvModelMatrix);
                    if (blas.Intersect(localRay, out BLAS.HitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.Triangle = blasHitInfo.Triangle;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;

                        hitInfo.MeshID = (int)parent.BlasIndex;
                        hitInfo.InstanceID = glInstanceID;
                    }

                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                uint leftChild = parent.LeftChild;
                uint rightChild = leftChild + 1;
                ref readonly GpuTlasNode left = ref Nodes[leftChild];
                ref readonly GpuTlasNode right = ref Nodes[rightChild];
                bool leftChildHit = MyMath.RayCuboidIntersect(ray, left.Min, left.Max, out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = MyMath.RayCuboidIntersect(ray, right.Min, right.Max, out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? leftChild : rightChild;
                        debugMaxStack = Helper.InterlockedMax(ref debugMaxStack, (int)(stackPtr));
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
    }
}
