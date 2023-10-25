using System;
using System.Collections.Generic;
using System.Linq;
using IDKEngine.Shapes;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class TLAS
    {
        //public struct BlasInstances
        //{
        //    public BLAS Blas;
        //    public ArraySegment<GpuMeshInstance> Instances;

        //    public void Deconstruct(out BLAS blas, out ArraySegment<GpuMeshInstance> instances)
        //    {
        //        blas = Blas;
        //        instances = Instances;
        //    }
        //}

        public GpuTlasNode Root => Nodes[0];
        public int TreeDepth { get; private set; }

        public GpuMeshInstance[] MeshInstances;
        public GpuDrawElementsCmd[] DrawCommands;
        public List<BLAS> Blases;
        public GpuTlasNode[] Nodes;
        public TLAS()
        {
            Blases = new List<BLAS>();
        }

        public void AddBlases(BLAS[] blases, GpuDrawElementsCmd[] drawCommands, GpuMeshInstance[] meshInstances)
        {
            Blases.AddRange(blases);
            DrawCommands = drawCommands;
            MeshInstances = meshInstances;

            Array.Resize(ref Nodes, 2 * meshInstances.Length - 1);
            TreeDepth = (int)MathF.Ceiling(MathF.Log2(Nodes.Length));
        }

        public void Build()
        {
            int nodesUsed = 0;

            // Flatten and transform local space blas instances into
            // world space tlas nodes. These nodes are the primitives of the the TLAS.
            {
                for (int i = 0; i < Blases.Count; i++)
                {
                    BLAS blas = Blases[i];
                    ref readonly GpuDrawElementsCmd cmd = ref DrawCommands[i];

                    for (int j = 0; j < cmd.InstanceCount; j++)
                    {
                        GpuTlasNode newNode;
                        Box worldSpaceBounds = Box.Transformed(GpuTypes.Conversions.ToBox(blas.Root), MeshInstances[cmd.BaseInstance + j].ModelMatrix);
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
                int candidatesSearchCount = MeshInstances.Length;
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

                        Box boundsFittingChildren = GpuTypes.Conversions.ToBox(nodeA);
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

                Box fittingBox = GpuTypes.Conversions.ToBox(node);
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

        public static int debugMaxStack = 0;
        public unsafe bool Intersect(in Ray ray, out BVH.RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new BVH.RayHitInfo();
            hitInfo.T = tMax;

            uint stackPtr = 0;
            uint stackTop = 0;
            uint* stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref Nodes[stackTop];
                if (parent.LeftChild == 0)
                {
                    BLAS blas = Blases[(int)parent.BlasIndex];

                    int glInstanceID = 0; // TODO: Work out actual instanceID value
                    Ray localRay = ray.Transformed(MeshInstances[glInstanceID].InvModelMatrix);
                    if (blas.Intersect(localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
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
                ref readonly GpuTlasNode leftNode = ref Nodes[leftChild];
                ref readonly GpuTlasNode rightNode = ref Nodes[rightChild];
                bool leftChildHit = Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(leftNode), out float tMinLeft, out float _) && tMinLeft <= hitInfo.T;
                bool rightChildHit = Intersections.RayVsBox(ray, GpuTypes.Conversions.ToBox(rightNode), out float tMinRight, out float _) && tMinRight <= hitInfo.T;

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
