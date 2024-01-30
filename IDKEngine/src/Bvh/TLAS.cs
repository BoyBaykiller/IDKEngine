using System;
using OpenTK.Mathematics;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    class TLAS
    {
        public GpuTlasNode Root => Nodes[0];
        public int TreeDepth { get; private set; }

        public GpuMeshInstance[] MeshInstances;
        public GpuDrawElementsCmd[] DrawCommands;
        public BLAS[] Blases;
        public GpuTlasNode[] Nodes;
        public TLAS(BLAS[] blases, GpuDrawElementsCmd[] drawCommands, GpuMeshInstance[] meshInstances)
        {
            Blases = blases;
            DrawCommands = drawCommands;
            MeshInstances = meshInstances;

            Nodes = new GpuTlasNode[Math.Max(2 * meshInstances.Length - 1, 1)];
            TreeDepth = (int)MathF.Ceiling(MathF.Log2(Nodes.Length));
        }

        public void Build()
        {
            int nodesUsed = 0;

            // Flatten and transform local space blas instances into
            // world space tlas nodes. These nodes are the primitives of the the TLAS.
            {
                for (int i = 0; i < Blases.Length; i++)
                {
                    BLAS blas = Blases[i];
                    ref readonly GpuDrawElementsCmd cmd = ref DrawCommands[i];

                    for (int j = 0; j < cmd.InstanceCount; j++)
                    {
                        int instanceID = cmd.BaseInstance + j;

                        Box worldSpaceBounds = Box.Transformed(Conversions.ToBox(blas.Root), MeshInstances[instanceID].ModelMatrix);

                        GpuTlasNode newNode = new GpuTlasNode();
                        newNode.Min = worldSpaceBounds.Min;
                        newNode.Max = worldSpaceBounds.Max;
                        newNode.IsLeaf = true;
                        newNode.LeftChildOrInstanceID = (uint)instanceID;
                        newNode.BlasIndex = (uint)i;

                        int newNodeIndex = Nodes.Length - 1 - nodesUsed++;
                        Nodes[newNodeIndex] = newNode;
                    }
                }
            }

            // Build TLAS from generated child nodes.
            // Technique: Every two nodes with the lowest combined area form a parent node.
            //            Apply this scheme recursivly until only a single root node encompassing the entire scene is left.          
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

                        Box boundsFittingChildren = Conversions.ToBox(nodeA);
                        boundsFittingChildren.GrowToFit(nodeB.Min);
                        boundsFittingChildren.GrowToFit(nodeB.Max);

                        GpuTlasNode newNode = new GpuTlasNode();
                        newNode.Min = boundsFittingChildren.Min;
                        newNode.Max = boundsFittingChildren.Max;
                        newNode.IsLeaf = false;
                        newNode.LeftChildOrInstanceID = (uint)nodeBId;

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


        public const float TRIANGLE_INTERSECT_COST = 1.1f;
        public const float NODE_INTERSECT_COST = 1.0f; // Keep it 1 so we effectively only have TRIANGLE_INTERSECT_COST as a paramater
        public const int SAH_SAMPLES = 8;

        public void BuildNew()
        {

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

        public static int debugMaxStack = 0;
        public unsafe bool Intersect(in Ray ray, out BVH.RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new BVH.RayHitInfo();
            hitInfo.T = tMax;

            int stackPtr = 0;
            uint stackTop = 0;
            Span<uint> stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GpuTlasNode parent = ref Nodes[stackTop];
                if (parent.IsLeaf)
                {
                    BLAS blas = Blases[(int)parent.BlasIndex];

                    uint instanceID = parent.LeftChildOrInstanceID;
                    Ray localRay = ray.Transformed(MeshInstances[instanceID].InvModelMatrix);
                    if (blas.Intersect(localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.TriangleIndices = blasHitInfo.TriangleIndices;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;

                        hitInfo.MeshID = (int)parent.BlasIndex;
                        hitInfo.InstanceID = (int)instanceID;
                    }

                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                    continue;
                }

                uint leftChild = parent.LeftChildOrInstanceID;
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
