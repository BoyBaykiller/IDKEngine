#define USE_SAH
using OpenTK.Mathematics;

namespace IDKEngine
{
    class BLAS
    {
#if !USE_SAH
        public const int BLAS_MIN_TRIANGLE_COUNT_LEAF = 8;
#endif

        public readonly GLSLBlasNode[] Nodes;

        private int nodesUsed = 2;
        public unsafe BLAS(GLSLTriangle* triangles, int count)
        {
            Nodes = new GLSLBlasNode[2 * count];

            ref GLSLBlasNode root = ref Nodes[0];
            root.TriCount = (uint)count;

            UpdateNodeBounds(ref root);
            Subdivide();

            void Subdivide(int nodeID = 0)
            {
                ref GLSLBlasNode parentNode = ref Nodes[nodeID];
#if !USE_SAH
                if (parentNode.TriCount < BLAS_MIN_TRIANGLE_COUNT_LEAF)
                    return;
#endif

#if USE_SAH
                float splitSAH = FindBestSplitPlane(ref parentNode, out int bestAxis, out float splitPos);

                float parentSAH = GetLeafSAH(MyMath.Area(parentNode.Max - parentNode.Min), parentNode.TriCount);
                if (splitSAH >= parentSAH)
                    return;

#else
                Vector3 extent = parentNode.Max - parentNode.Min;
                int bestAxis = 0;
                if (extent.Y > extent.X) bestAxis = 1;
                if (extent.Z > extent[bestAxis]) bestAxis = 2;

                float splitPos = parentNode.Min[bestAxis] + extent[bestAxis] * 0.5f;
#endif

                {
                    int rightStart = (int)parentNode.TriStartOrLeftChild;
                    int endOfTris = (int)(rightStart + parentNode.TriCount - 1);
                    while (rightStart <= endOfTris)
                    {
                        ref GLSLTriangle tri = ref triangles[rightStart];
                        if (MyMath.Average(tri.Vertex0.Position, tri.Vertex1.Position, tri.Vertex2.Position)[bestAxis] < splitPos)
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
                    Nodes[leftChildID].TriCount = leftCount;

                    Nodes[rightChildID].TriStartOrLeftChild = (uint)rightStart;
                    Nodes[rightChildID].TriCount = parentNode.TriCount - leftCount;

                    parentNode.TriStartOrLeftChild = (uint)leftChildID;
                    parentNode.TriCount = 0;

                    UpdateNodeBounds(ref Nodes[leftChildID]);
                    UpdateNodeBounds(ref Nodes[rightChildID]);

                    Subdivide(leftChildID);
                    Subdivide(rightChildID);
                }
            }

#if USE_SAH
            unsafe float FindBestSplitPlane(ref GLSLBlasNode node, out int bestAxis, out float splitPos)
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
                        float cost = GetSAH(node, i, candidatePos);
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

            unsafe float GetSAH(in GLSLBlasNode node, int axis, float pos)
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
#endif

            unsafe void UpdateNodeBounds(ref GLSLBlasNode node)
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

        private static float GetLeafSAH(float area, uint triangles)
        {
            return area * triangles;
        }
    }
}