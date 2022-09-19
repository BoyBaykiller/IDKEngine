#define USE_SAH
using System;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class BLAS
    {
        public const int MIN_TRIANGLES_PER_LEAF_COUNT = 3;
#if USE_SAH
        public const int SAH_SAMPLES = 8;
#endif

        public readonly GLSLBlasNode[] Nodes;

        private int nodesUsed;
        public unsafe BLAS(GLSLTriangle* triangles, int count, out int treeDepth)
        {
            treeDepth = (int)MathF.Ceiling(MathF.Log2(count)) + 1;
            
            Nodes = new GLSLBlasNode[2 * count];
            ref GLSLBlasNode root = ref Nodes[nodesUsed++];
            root.TriCount = (uint)count;

            nodesUsed++; // one empty node to align following childs in single 64 cache line (in theory)

            UpdateNodeBounds(ref root);
            Subdivide();

            void Subdivide(int nodeID = 0)
            {
                ref GLSLBlasNode parentNode = ref Nodes[nodeID];
#if !USE_SAH
                if (parentNode.TriCount <= MIN_TRIANGLES_PER_LEAF_COUNT)
                    return;
#endif

#if USE_SAH
                float splitSAH = FindBestSplitAxis(ref parentNode, out int splitAxis, out float splitPos);

                float parentSAH = CalculateSAH(MyMath.Area(parentNode.Max - parentNode.Min), parentNode.TriCount, 0, 0);
                if (splitSAH >= parentSAH || parentNode.TriCount <= MIN_TRIANGLES_PER_LEAF_COUNT)
                    return;

#else
                Vector3 extent = parentNode.Max - parentNode.Min;
                int splitAxis = 0;
                if (extent.Y > extent.X) splitAxis = 1;
                if (extent.Z > extent[splitAxis]) splitAxis = 2;

                float splitPos = parentNode.Min[splitAxis] + extent[splitAxis] * 0.5f;
#endif

                int rightStart = (int)parentNode.TriStartOrLeftChild;
                int endOfTris = (int)(rightStart + parentNode.TriCount - 1);
                while (rightStart <= endOfTris)
                {
                    ref GLSLTriangle tri = ref triangles[rightStart];
                    if ((tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) * (1.0f / 3.0f) < splitPos)
                        rightStart++;
                    else
                        Helper.Swap(ref tri, ref triangles[endOfTris--]);
                }

                uint leftCount = (uint)(rightStart - parentNode.TriStartOrLeftChild);
                if (leftCount == 0 || leftCount == parentNode.TriCount)
                    return;

                int leftChildID = nodesUsed++;
                int rightChildID = nodesUsed++;

                Nodes[leftChildID].TriStartOrLeftChild = parentNode.TriStartOrLeftChild;
                Nodes[leftChildID].TriCount |= leftCount;

                Nodes[rightChildID].TriStartOrLeftChild = (uint)rightStart;
                Nodes[rightChildID].TriCount |= parentNode.TriCount - leftCount;

                parentNode.TriStartOrLeftChild = (uint)leftChildID;
                parentNode.TriCount = 0;

                UpdateNodeBounds(ref Nodes[leftChildID]);
                UpdateNodeBounds(ref Nodes[rightChildID]);

                Subdivide(leftChildID);
                Subdivide(rightChildID);
            }

#if USE_SAH
            float FindBestSplitAxis(ref GLSLBlasNode node, out int splitAxis, out float splitPos)
            {
                splitAxis = -1;
                splitPos = 0;

                AABB uniformDivideArea = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                for (int i = 0; i < node.TriCount; i++)
                {
                    ref readonly GLSLTriangle tri = ref triangles[(int)(node.TriStartOrLeftChild + i)];
                    Vector3 centroid = (tri.Vertex0.Position + tri.Vertex1.Position + tri.Vertex2.Position) * (1.0f / 3.0f);
                    uniformDivideArea.Shrink(centroid);
                }

                float bestSAH = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    if (uniformDivideArea.Min[i] == uniformDivideArea.Max[i])
                        continue;

                    float scale = (uniformDivideArea.Max[i] - uniformDivideArea.Min[i]) / SAH_SAMPLES;
                    for (int j = 1; j < SAH_SAMPLES; j++)
                    {
                        float currentSplitPos = uniformDivideArea.Min[i] + j * scale;
                        float currentSAH = FindSAH(node, i, currentSplitPos);
                        if (currentSAH < bestSAH)
                        {
                            splitPos = currentSplitPos;
                            splitAxis = i;
                            bestSAH = currentSAH;
                        }
                    }
                }

                return bestSAH;
            }

            float FindSAH(in GLSLBlasNode node, int splitAxis, float splitPos)
            {
                AABB leftBox = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));
                AABB rightBox = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

                uint leftCount = 0;
                for (uint i = 0; i < node.TriCount; i++)
                {
                    ref readonly GLSLTriangle tri = ref triangles[(int)(node.TriStartOrLeftChild + i)];
                    float triSplitPos = (tri.Vertex0.Position[splitAxis] + tri.Vertex1.Position[splitAxis] + tri.Vertex2.Position[splitAxis]) * (1.0f / 3.0f);
                    if (triSplitPos < splitPos)
                    {
                        leftCount++;
                        leftBox.Shrink(tri);
                    }
                    else
                    {
                        rightBox.Shrink(tri);
                    }
                }
                uint rightCount = node.TriCount - leftCount;
                
                float sah = CalculateSAH(leftBox.Area(), leftCount, rightBox.Area(), rightCount);
                return sah;
            }
#endif

            void UpdateNodeBounds(ref GLSLBlasNode node)
            {
                node.Min = new Vector3(float.MaxValue);
                node.Max = new Vector3(float.MinValue);

                for (int i = 0; i < node.TriCount; i++)
                {
                    ref readonly GLSLTriangle tri = ref triangles[(int)(node.TriStartOrLeftChild + i)];

                    node.Min = Vector3.ComponentMin(node.Min, tri.Vertex0.Position);
                    node.Min = Vector3.ComponentMin(node.Min, tri.Vertex1.Position);
                    node.Min = Vector3.ComponentMin(node.Min, tri.Vertex2.Position);

                    node.Max = Vector3.ComponentMax(node.Max, tri.Vertex0.Position);
                    node.Max = Vector3.ComponentMax(node.Max, tri.Vertex1.Position);
                    node.Max = Vector3.ComponentMax(node.Max, tri.Vertex2.Position);
                }
            }
        }

        private static float CalculateSAH(float area0, uint triangles0, float area1, uint triangles1)
        {
            return area0 * triangles0 + area1 * triangles1;
        }
    }
}