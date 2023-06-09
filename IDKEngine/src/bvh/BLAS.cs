using System;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class BLAS
    {
        public const int MIN_TRIANGLES_PER_LEAF_COUNT = 2;
        public const int SAH_SAMPLES = 8;

        public struct HitInfo
        {
            public GLSLTriangle Triangle;
            public Vector3 Bary;
            public float T;
        }

        public GLSLBlasNode Root => Nodes[0];
        public AABB RootBounds => new AABB(Root.Min, Root.Max);
        public int NodesUsed { get; private set; }

        public readonly int TreeDepth;
        public readonly GLSLBlasNode[] Nodes;
        public readonly GLSLTriangle[] Triangles;
        public BLAS(GLSLTriangle[] triangles)
        {
            Triangles = triangles;

            Nodes = new GLSLBlasNode[2 * Triangles.Length];

            ref GLSLBlasNode root = ref Nodes[NodesUsed++];
            root.TriCount = (uint)Triangles.Length;
            UpdateNodeBounds(ref root);
            Subdivide(ref root);

            // Artificially create child node and copy root node into it.
            // This is done because BVH traversal skips root node under the assumation there will always be at least one child
            if (NodesUsed == 1)
            {
                Nodes[NodesUsed++] = root;
            }

            TreeDepth = (int)MathF.Ceiling(MathF.Log2(NodesUsed));
        }

        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo, float tMaxDist = float.MaxValue)
        {
            hitInfo = new HitInfo();
            hitInfo.T = tMaxDist;

            ref readonly GLSLBlasNode rootNode = ref Nodes[0];
            if (!(MyMath.RayCuboidIntersect(ray, rootNode.Min, rootNode.Max, out float tMinRoot, out float tMaxRoot) && tMinRoot < hitInfo.T))
            {
                return false;
            }

            uint stackPtr = 0;
            uint stackTop = 1;
            uint* stack = stackalloc uint[TreeDepth];
            while (true)
            {
                ref readonly GLSLBlasNode left = ref Nodes[stackTop];
                ref readonly GLSLBlasNode right = ref Nodes[stackTop + 1];
                bool leftChildHit = MyMath.RayCuboidIntersect(ray, left.Min, left.Max, out float tMinLeft, out float rayTMax) && tMinLeft <= hitInfo.T;
                bool rightChildHit = MyMath.RayCuboidIntersect(ray, right.Min, right.Max, out float tMinRight, out rayTMax) && tMinRight <= hitInfo.T;

                uint triCount = (leftChildHit ? left.TriCount : 0) + (rightChildHit ? right.TriCount : 0);
                if (triCount > 0)
                {
                    uint first = (leftChildHit && (left.TriCount > 0)) ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                    for (uint k = first; k < first + triCount; k++)
                    {
                        ref readonly GLSLTriangle triangle = ref Triangles[k];
                        if (MyMath.RayTriangleIntersect(ray, triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position, out Vector3 bary, out float t) && t < hitInfo.T)
                        {
                            hitInfo.Triangle = triangle;
                            hitInfo.Bary = bary;
                            hitInfo.T = t;
                        }
                    }

                    leftChildHit = leftChildHit && (left.TriCount == 0);
                    rightChildHit = rightChildHit && (right.TriCount == 0);
                }

                // Push closest hit child to the stack at last
                if (leftChildHit || rightChildHit)
                {
                    if (leftChildHit && rightChildHit)
                    {
                        bool leftCloser = tMinLeft < tMinRight;
                        stackTop = leftCloser ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                        stack[stackPtr++] = leftCloser ? right.TriStartOrLeftChild : left.TriStartOrLeftChild;
                    }
                    else
                    {
                        stackTop = leftChildHit ? left.TriStartOrLeftChild : right.TriStartOrLeftChild;
                    }
                }
                else
                {
                    // Here: On a leaf node or didn't hit any children which means we should traverse up
                    if (stackPtr == 0) break;
                    stackTop = stack[--stackPtr];
                }
            }

            return hitInfo.T != tMaxDist;
        }

        private void Subdivide(ref GLSLBlasNode parentNode)
        {
            if (parentNode.TriCount <= MIN_TRIANGLES_PER_LEAF_COUNT)
            {
                return;
            }

            float splitCost = FindBestSplitAxis(parentNode, out int splitAxis, out float splitPos);
            float parentSAH = CalculateSAH(MyMath.HalfArea(parentNode.Max - parentNode.Min), parentNode.TriCount, 0, 0);
            if (!(splitCost < parentSAH))
            {
                return;
            }

            uint start = parentNode.TriStartOrLeftChild;
            uint end = start + parentNode.TriCount;

            uint mid = start;
            for (uint i = start; i < end; i++)
            {
                ref GLSLTriangle tri = ref Triangles[(int)i];
                if ((tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f < splitPos)
                {
                    MathHelper.Swap(ref tri, ref Triangles[(int)mid++]);
                }
            }

            parentNode.TriStartOrLeftChild = (uint)NodesUsed;
            parentNode.TriCount = 0;


            ref GLSLBlasNode leftChild = ref Nodes[NodesUsed++];
            leftChild.TriStartOrLeftChild = start;
            leftChild.TriCount = mid - start;

            ref GLSLBlasNode rightChild = ref Nodes[NodesUsed++];
            rightChild.TriStartOrLeftChild = mid;
            rightChild.TriCount = end - mid;

            UpdateNodeBounds(ref leftChild);
            UpdateNodeBounds(ref rightChild);

            Subdivide(ref leftChild);
            Subdivide(ref rightChild);
        }

        private float FindBestSplitAxis(in GLSLBlasNode node, out int splitAxis, out float splitPos)
        {
            splitAxis = 0;
            splitPos = 0;

            AABB uniformDivideArea = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            for (int i = 0; i < node.TriCount; i++)
            {
                ref readonly GLSLTriangle tri = ref Triangles[(int)(node.TriStartOrLeftChild + i)];
                Vector3 centroid = (tri.Vertex0.Position + tri.Vertex1.Position + tri.Vertex2.Position) / 3.0f;
                uniformDivideArea.GrowToFit(centroid);
            }

            float bestSplitCost = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (uniformDivideArea.Min[i] == uniformDivideArea.Max[i])
                {
                    continue;
                }

                float scale = (uniformDivideArea.Max[i] - uniformDivideArea.Min[i]) / SAH_SAMPLES;
                for (int j = 1; j < SAH_SAMPLES; j++)
                {
                    float currentSplitPos = uniformDivideArea.Min[i] + j * scale;
                    float currentSplitCost = GetSplitCost(node, i, currentSplitPos);
                    if (currentSplitCost < bestSplitCost)
                    {
                        splitPos = currentSplitPos;
                        splitAxis = i;
                        bestSplitCost = currentSplitCost;
                    }
                }
            }

            return bestSplitCost;
        }

        private float GetSplitCost(in GLSLBlasNode node, int splitAxis, float splitPos)
        {
            AABB leftBox = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
            AABB rightBox = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            uint leftCount = 0;
            for (uint i = 0; i < node.TriCount; i++)
            {
                ref readonly GLSLTriangle tri = ref Triangles[(int)(node.TriStartOrLeftChild + i)];

                float triSplitPos = (tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f;
                if (triSplitPos < splitPos)
                {
                    leftCount++;
                    leftBox.GrowToFit(tri);
                }
                else
                {
                    rightBox.GrowToFit(tri);
                }
            }
            uint rightCount = node.TriCount - leftCount;


            float sah = CalculateSAH(MyMath.HalfArea(leftBox.Max - leftBox.Min), leftCount, MyMath.HalfArea(rightBox.Max - rightBox.Min), rightCount);
            return sah;
        }

        private void UpdateNodeBounds(ref GLSLBlasNode node)
        {
            AABB bounds = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

            for (uint i = node.TriStartOrLeftChild; i < node.TriStartOrLeftChild + node.TriCount; i++)
            {
                ref readonly GLSLTriangle tri = ref Triangles[(int)i];
                bounds.GrowToFit(tri);
            }

            node.Min = bounds.Min;
            node.Max = bounds.Max;
        }

        private static float CalculateSAH(float area0, uint triangles0, float area1, uint triangles1)
        {
            return area0 * triangles0 + area1 * triangles1;
        }
    }
}