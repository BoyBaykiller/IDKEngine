#define USE_SAH
using System;
using OpenTK.Mathematics;

namespace IDKEngine
{
    class BLAS
    {
        public const int BLAS_MIN_TRIANGLE_COUNT_LEAF = 8;

        public readonly GLSLBlasNode[] Nodes;
        private int nodesUsed = 2;
        public unsafe BLAS(Span<GLSLTriangle> triangles)
        {
            Nodes = new GLSLBlasNode[2 * triangles.Length];
            ref GLSLBlasNode root = ref Nodes[0];
            root.TriCount = (uint)triangles.Length;

            UpdateNodeBounds(triangles, ref root);
            Subdivide(triangles);
            void Subdivide(Span<GLSLTriangle> triangles, int nodeID = 0)
            {
                ref GLSLBlasNode node = ref Nodes[nodeID];
#if !USE_SAH
                if (node.TriCount < BLAS_MIN_TRIANGLE_COUNT_LEAF)
                    return;
#endif

#if USE_SAH
                float splitSAH = FindBestSplitPlane(triangles, ref node, out int bestAxis, out float splitPos);

                float parentSAH = GetLeafSAH(MyMath.Area(node.Max - node.Min), node.TriCount);
                if (splitSAH >= parentSAH)
                    return;

#else
                Vector3 extent = node.Max - node.Min;
                int bestAxis = 0;
                if (extent.Y > extent.X) bestAxis = 1;
                if (extent.Z > extent[bestAxis]) bestAxis = 2;

                float splitPos = node.Min[bestAxis] + extent[bestAxis] * 0.5f;
#endif

                {
                    int i = (int)node.TriStartOrLeftChild;
                    int j = (int)(i + node.TriCount - 1);
                    while (i <= j)
                    {
                        ref GLSLTriangle tri = ref triangles[i];
                        if (MyMath.Average(tri.Vertex0.Position, tri.Vertex1.Position, tri.Vertex2.Position)[bestAxis] < splitPos)
                            i++;
                        else
                            Helper.Swap(ref tri, ref triangles[j--]);
                    }

                    uint leftCount = (uint)(i - node.TriStartOrLeftChild);
                    if (leftCount == 0 || leftCount == node.TriCount)
                        return;

                    int leftChildID = nodesUsed++;
                    int rightChildID = nodesUsed++;

                    Nodes[leftChildID].TriStartOrLeftChild = node.TriStartOrLeftChild;
                    Nodes[leftChildID].TriCount = leftCount;

                    Nodes[rightChildID].TriStartOrLeftChild = (uint)i;
                    Nodes[rightChildID].TriCount = node.TriCount - leftCount;

                    node.TriStartOrLeftChild = (uint)leftChildID;
                    node.TriCount = 0;

                    UpdateNodeBounds(triangles, ref Nodes[leftChildID]);
                    UpdateNodeBounds(triangles, ref Nodes[rightChildID]);

                    Subdivide(triangles, leftChildID);
                    Subdivide(triangles, rightChildID);
                }
            }

        }

        private static float FindBestSplitPlane(Span<GLSLTriangle> triangles, ref GLSLBlasNode node, out int bestAxis, out float splitPos)
        {
            bestAxis = -1;
            splitPos = 0;
            float bestSAH = float.MaxValue;

            AABB centroidBounds = new AABB();
            for (int i = 0; i < node.TriCount; i++)
            {
                ref readonly GLSLTriangle tri = ref triangles[(int)(node.TriStartOrLeftChild + i)];
                Vector3 centroid = MyMath.Average(tri.Vertex0.Position, tri.Vertex1.Position, tri.Vertex2.Position);
                centroidBounds.Grow(centroid);
            }

            for (int i = 0; i < 3; i++)
            {
                if (centroidBounds.Min[i] == centroidBounds.Max[i])
                    continue;

                const int INTERVALS = 8;
                float scale = (centroidBounds.Max[i] - centroidBounds.Min[i]) / INTERVALS;

                for (int j = 1; j < INTERVALS; j++)
                {
                    float candidatePos = centroidBounds.Min[i] + j * scale;
                    float cost = GetSAH(triangles, node, i, candidatePos);
                    if (cost < bestSAH)
                    {
                        splitPos = candidatePos;
                        bestAxis = i;
                        bestSAH = cost;
                    }
                }
            }

            return bestSAH;
        }

        private static float GetSAH(ReadOnlySpan<GLSLTriangle> triangles, in GLSLBlasNode node, int axis, float pos)
        {
            AABB leftBox = new AABB();
            AABB rightBox = new AABB();

            uint leftCount = 0, rightCount = 0;
            for (uint i = 0; i < node.TriCount; i++)
            {
                ref readonly GLSLTriangle tri = ref triangles[(int)(node.TriStartOrLeftChild + i)];
                if (MyMath.Average(tri.Vertex0.Position, tri.Vertex1.Position, tri.Vertex2.Position)[axis] < pos)
                {
                    leftCount++;
                    leftBox.Grow(tri);
                }
                else
                {
                    rightCount++;
                    rightBox.Grow(tri);
                }
            }
            float sah = GetLeafSAH(leftBox.Area(), leftCount) + GetLeafSAH(rightBox.Area(), rightCount);
            return sah > 0 ? sah : float.MaxValue;
        }

        private static float GetLeafSAH(float area, uint triangles)
        {
            return area * triangles;
        }

        private static unsafe void UpdateNodeBounds(ReadOnlySpan<GLSLTriangle> triangles, ref GLSLBlasNode node)
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
}
