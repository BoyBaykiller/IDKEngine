using System;
using System.Linq;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class TLAS
    {
        public struct HitInfo
        {
            public GLSLTriangle Triangle;
            public Vector3 Bary;
            public float T;
            public int MeshID;
            public int InstanceID;
        }

        public struct BlasInstances
        {
            public BLAS Blas;
            public ArraySegment<GLSLMeshInstance> Instances;

            public void Deconstruct(out BLAS blas, out ArraySegment<GLSLMeshInstance> instances)
            {
                blas = Blas;
                instances = Instances;
            }
        }

        public GLSLTlasNode Root => Nodes[0];
        public Box RootBounds => new Box(Root.Min, Root.Max);
        public int NodesUsed { get; private set; }

        public readonly int TreeDepth;
        public readonly GLSLTlasNode[] Nodes;
        public readonly BlasInstances[] BlasesInstances;
        public TLAS(BlasInstances[] blasesInstances)
        {
            BlasesInstances = blasesInstances;

            int instanceCount = BlasesInstances.Sum(blasInstances => blasInstances.Instances.Count);
            TreeDepth = (int)MathF.Ceiling(MathF.Log2(instanceCount)) + 1;
            Nodes = new GLSLTlasNode[2 * instanceCount];
        }

        public void Build()
        {
            int instanceCount = BlasesInstances.Sum(blasInstances => blasInstances.Instances.Count);
            NodesUsed = 0;

            {
                for (int i = 0; i < BlasesInstances.Length; i++)
                {
                    (BLAS blas, ArraySegment<GLSLMeshInstance> instances) = BlasesInstances[i];
                    for (int j = 0; j < instances.Count; j++)
                    {
                        GLSLTlasNode newNode;
                        newNode.Min = Vector3.TransformPosition(blas.RootBounds.Min, instances[j].ModelMatrix);
                        newNode.Max = Vector3.TransformPosition(blas.RootBounds.Max, instances[j].ModelMatrix);
                        newNode.LeftChild = 0;
                        newNode.BlasIndex = (uint)i;

                        int newNodeIndex = Nodes.Length - 2 - NodesUsed++;
                        Nodes[newNodeIndex] = newNode;
                    }
                }
            }

            int nodesSearchCount = instanceCount;
            int nodesSearchCountBackup = nodesSearchCount;
            int nodeStart = Nodes.Length - 2;
            while (nodesSearchCount > 1)
            {
                int nodeAId = nodeStart;
                int nodeBId = FindBestMatch(nodeStart, nodesSearchCount, nodeStart);
                int nodeCId = FindBestMatch(nodeStart, nodesSearchCount, nodeBId);

                if (nodeStart == nodeCId)
                {
                    MathHelper.Swap(ref Nodes[nodeStart - 1], ref Nodes[nodeBId]);
                    nodeBId = nodeStart - 1;

                    ref readonly GLSLTlasNode nodeB = ref Nodes[nodeBId];
                    ref readonly GLSLTlasNode nodeA = ref Nodes[nodeAId];

                    Box box = new Box(nodeA.Min, nodeA.Max);
                    box.GrowToFit(nodeB.Min);
                    box.GrowToFit(nodeB.Max);

                    int newNodeIndex = Nodes.Length - 2 - NodesUsed++;

                    GLSLTlasNode newNode = new GLSLTlasNode();
                    newNode.LeftChild = (uint)nodeBId;
                    newNode.Min = box.Min;
                    newNode.Max = box.Max;

                    Nodes[newNodeIndex] = newNode;

                    nodeStart -= 2;
                    nodesSearchCount = nodesSearchCountBackup - 1;
                    nodesSearchCountBackup = nodesSearchCount;
                }
                else
                {
                    MathHelper.Swap(ref Nodes[nodeStart], ref Nodes[nodeStart - --nodesSearchCount]);
                }
            }
        }

        private int FindBestMatch(int start, int count, int nodeIndex)
        {
            float smallestArea = float.MaxValue;
            int bestNodeIndex = -1;

            ref readonly GLSLTlasNode node = ref Nodes[nodeIndex];
            for (int i = start; i > start - count; i--)
            {
                if (i == nodeIndex)
                {
                    continue;
                }

                ref readonly GLSLTlasNode otherNode = ref Nodes[i];

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

        public static uint debugMaxStack = 0;
        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new HitInfo();
            hitInfo.T = tMax;

            uint stackPtr = 0;
            uint stackTop = 0;
            // FIX: Why is requied stack so much bigger than expected?
            uint* stack = stackalloc uint[TreeDepth * 10];
            while (true)
            {
                ref readonly GLSLTlasNode parent = ref Nodes[stackTop];
                if (parent.IsLeaf())
                {
                    BlasInstances blasInstances = BlasesInstances[parent.BlasIndex];
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
                    debugMaxStack = Math.Max(stackTop, stackPtr - 1);
                    stackTop = stack[--stackPtr];
                    continue;
                }

                uint leftChild = parent.LeftChild;
                uint rightChild = leftChild + 1;
                ref readonly GLSLTlasNode left = ref Nodes[leftChild];
                ref readonly GLSLTlasNode right = ref Nodes[rightChild];
                bool leftChildHit = MyMath.RayCuboidIntersect(ray, left.Min, left.Max, out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = MyMath.RayCuboidIntersect(ray, right.Min, right.Max, out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? leftChild : rightChild;
                        debugMaxStack = Math.Max(stackTop, stackPtr);
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
                    debugMaxStack = Math.Max(stackTop, stackPtr - 1);
                    stackTop = stack[--stackPtr];
                }
            }

            return hitInfo.T != tMax;
        }
    }
}
