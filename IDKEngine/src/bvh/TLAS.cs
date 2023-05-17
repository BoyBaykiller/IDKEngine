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
        public AABB RootBounds => new AABB(Root.Min, Root.Max);
        public int NodesUsed { get; private set; }

        public readonly int TreeDepth;
        public readonly GLSLTlasNode[] Nodes;
        public readonly BlasInstances[] BlasesInstances;
        public TLAS(BlasInstances[] blasesInstances)
        {
            BlasesInstances = blasesInstances;

            int instanceCount = BlasesInstances.Sum(blasInstances => blasInstances.Instances.Count);
            TreeDepth = (int)MathF.Ceiling(MathF.Log2(instanceCount)) + 2;
            Nodes = new GLSLTlasNode[2 * instanceCount];
        }

        public void Build()
        {
            int instanceCount = BlasesInstances.Sum(blasInstances => blasInstances.Instances.Count);

            Span<uint> nodeIndices = stackalloc uint[instanceCount];
            int nodeIndicesLength = nodeIndices.Length;
            NodesUsed = 1;

            {
                for (int i = 0; i < BlasesInstances.Length; i++)
                {
                    (BLAS blas, ArraySegment<GLSLMeshInstance> instances) = BlasesInstances[i];
                    for (int j = 0; j < instances.Count; j++)
                    {
                        nodeIndices[NodesUsed - 1] = (uint)NodesUsed;

                        GLSLTlasNode newNode;
                        newNode.Min = Vector3.TransformPosition(blas.RootBounds.Min, instances[j].ModelMatrix);
                        newNode.Max = Vector3.TransformPosition(blas.RootBounds.Max, instances[j].ModelMatrix);
                        newNode.LeftChild = 0;
                        newNode.RightChild = 0;
                        newNode.BlasIndex = (uint)i;

                        Nodes[NodesUsed] = newNode;
                        NodesUsed++;
                    }
                }
            }

            // Source: https://jacco.ompf2.com/2022/05/13/how-to-build-a-bvh-part-6-all-together-now/
            {
                int A = 0, B = FindSmallestMergedHalfArea(nodeIndices, nodeIndicesLength, A);
                while (nodeIndicesLength > 1)
                {
                    int C = FindSmallestMergedHalfArea(nodeIndices, nodeIndicesLength, B);
                    if (A == C)
                    {
                        uint nodeIdxA = nodeIndices[A];
                        uint nodeIdxB = nodeIndices[B];
                        ref readonly GLSLTlasNode nodeA = ref Nodes[nodeIdxA];
                        ref readonly GLSLTlasNode nodeB = ref Nodes[nodeIdxB];

                        AABB aabb = new AABB(nodeA.Min, nodeA.Max);
                        aabb.GrowToFit(nodeB.Min);
                        aabb.GrowToFit(nodeB.Max);

                        ref GLSLTlasNode newNode = ref Nodes[NodesUsed];
                        newNode.RightChild = (ushort)nodeIdxA;
                        newNode.LeftChild = (ushort)nodeIdxB;
                        newNode.Min = aabb.Min;
                        newNode.Max = aabb.Max;

                        nodeIndices[A] = (uint)NodesUsed++;
                        nodeIndices[B] = nodeIndices[nodeIndicesLength - 1];
                        B = FindSmallestMergedHalfArea(nodeIndices, --nodeIndicesLength, A);
                    }
                    else
                    {
                        A = B;
                        B = C;
                    }
                }
                Nodes[0] = Nodes[nodeIndices[A]];
            }
        }

        private int FindSmallestMergedHalfArea(Span<uint> indices, int count, int nodeIndex)
        {
            float smallestArea = float.MaxValue;
            int bestNodeIndex = -1;

            ref readonly GLSLTlasNode node = ref Nodes[indices[nodeIndex]];
            for (int i = 0; i < count; i++)
            {
                if (i == nodeIndex)
                {
                    continue;
                }

                ref readonly GLSLTlasNode otherNode = ref Nodes[indices[i]];

                AABB fittingBox = new AABB(node.Min, node.Max);
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

        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo, float tMax = float.MaxValue)
        {
            hitInfo = new HitInfo();
            hitInfo.T = tMax;

            uint stackPtr = 0;
            uint stackTop = 0;
            uint* stack = stackalloc uint[TreeDepth];
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
                    stackTop = stack[--stackPtr];
                    continue;
                }

                ref readonly GLSLTlasNode left = ref Nodes[parent.LeftChild];
                ref readonly GLSLTlasNode right = ref Nodes[parent.RightChild];
                bool leftChildHit = MyMath.RayCuboidIntersect(ray, left.Min, left.Max, out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = MyMath.RayCuboidIntersect(ray, right.Min, right.Max, out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? parent.LeftChild : parent.RightChild;
                        stack[stackPtr++] = leftCloser ? parent.RightChild : parent.LeftChild;
                    }
                    else
                    {
                        stackTop = leftChildHit ? parent.LeftChild : parent.RightChild;
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
