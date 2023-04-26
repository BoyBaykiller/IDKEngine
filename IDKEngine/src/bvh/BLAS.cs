using System;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class BLAS
    {
        public const int MIN_TRIANGLES_PER_LEAF_COUNT = 2;
        public const int SAH_SAMPLES = 8;

        public readonly GLSLBlasNode[] Nodes;

        private int nodesUsed;
        public unsafe BLAS(GLSLTriangle* triangles, int count, out int treeDepth)
        {
            treeDepth = (int)Math.Ceiling(MathF.Log2(count));

            Nodes = new GLSLBlasNode[2 * count];
            ref GLSLBlasNode root = ref Nodes[nodesUsed++];
            root.TriCount = (uint)count;
            UpdateNodeBounds(ref root);
            Subdivide(ref root);

            // Artificially create child node and copy root node into it.
            // This is done because BVH traversal skips root node under the assumation there will always be at least one child
            if (nodesUsed == 1)
            {
                Nodes[nodesUsed++] = root;
            }

            void Subdivide(ref GLSLBlasNode parentNode)
            {
                if (parentNode.TriCount <= MIN_TRIANGLES_PER_LEAF_COUNT)
                {
                    return;
                }

                float splitSAH = FindBestSplitAxis(parentNode, out int splitAxis, out float splitPos);
                float parentSAH = CalculateSAH(MyMath.Area(parentNode.Max - parentNode.Min), parentNode.TriCount, 0, 0);
                if (splitSAH >= parentSAH)
                {
                    return;
                }

                uint start = parentNode.TriStartOrLeftChild;
                uint end = start + parentNode.TriCount;

                uint mid = start;
                for (uint i = start; i < end; i++)
                {
                    ref GLSLTriangle tri = ref triangles[i];
                    if ((tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) / 3.0f < splitPos)
                    {
                        MathHelper.Swap(ref tri, ref triangles[mid++]);
                    }
                }

                int leftChildID = nodesUsed++;
                int rightChildID = nodesUsed++;

                ref GLSLBlasNode leftChild = ref Nodes[leftChildID];
                leftChild.TriStartOrLeftChild = start;
                leftChild.TriCount = mid - start;

                ref GLSLBlasNode rightChild = ref Nodes[rightChildID];
                rightChild.TriStartOrLeftChild = mid;
                rightChild.TriCount = end - mid;

                parentNode.TriStartOrLeftChild = (uint)leftChildID;
                parentNode.TriCount = 0;

                UpdateNodeBounds(ref Nodes[leftChildID]);
                UpdateNodeBounds(ref Nodes[rightChildID]);

                Subdivide(ref leftChild);
                Subdivide(ref rightChild);
            }

            float FindBestSplitAxis(in GLSLBlasNode node, out int splitAxis, out float splitPos)
            {
                splitAxis = 1;
                splitPos = 0;

                AABB uniformDivideArea = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                for (int i = 0; i < node.TriCount; i++)
                {
                    ref readonly GLSLTriangle tri = ref triangles[node.TriStartOrLeftChild + i];
                    Vector3 centroid = (tri.Vertex0.Position + tri.Vertex1.Position + tri.Vertex2.Position) / 3.0f;
                    uniformDivideArea.Shrink(centroid);
                }

                float bestSplitCost = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    if (uniformDivideArea.Min[i] == uniformDivideArea.Max[i])
                        continue;

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

            float GetSplitCost(in GLSLBlasNode node, int splitAxis, float splitPos)
            {
                AABB leftBox = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                AABB rightBox = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

                uint leftCount = 0;
                for (uint i = 0; i < node.TriCount; i++)
                {
                    ref readonly GLSLTriangle tri = ref triangles[node.TriStartOrLeftChild + i];
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
                
                float sah = CalculateSAH(leftBox.Area(), leftCount, rightBox.Area(), rightCount);
                return sah;
            }

            void UpdateNodeBounds(ref GLSLBlasNode node)
            {
                AABB bounds = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

                for (uint i = node.TriStartOrLeftChild; i < node.TriStartOrLeftChild + node.TriCount; i++)
                {
                    bounds.GrowToFit(triangles[i]);
                }

                node.Min = bounds.Min;
                node.Max = bounds.Max;
            }
        }

        private static float CalculateSAH(float area0, uint triangles0, float area1, uint triangles1)
        {
            return area0 * triangles0 + area1 * triangles1;
        }
    }
}