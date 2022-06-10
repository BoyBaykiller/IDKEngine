using System;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using IDKEngine.Render;

namespace IDKEngine
{
    class BLAS
    {
        public const int BLAS_MIN_TRIANGLE_COUNT_LEAF = 8;

        public readonly ModelSystem ModelSystem;
        public readonly GLSLBlasNode[] Nodes;
        public readonly GLSLTriangle[] Triangles;
        public unsafe BLAS(ModelSystem modelSystem)
        {
            Triangles = new GLSLTriangle[modelSystem.Indices.Length / 3];
            Nodes = new GLSLBlasNode[Triangles.Length];
            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                GLSLDrawCommand cmd = modelSystem.DrawCommands[i];
                for (int j = cmd.FirstIndex; j < cmd.FirstIndex + cmd.Count; j += 3)
                {
                    Triangles[j / 3].Vertex0 = modelSystem.Vertices[modelSystem.Indices[j + 0] + cmd.BaseVertex];
                    Triangles[j / 3].Vertex1 = modelSystem.Vertices[modelSystem.Indices[j + 1] + cmd.BaseVertex];
                    Triangles[j / 3].Vertex2 = modelSystem.Vertices[modelSystem.Indices[j + 2] + cmd.BaseVertex];
                }
            }

            Helper.InParallel(0, modelSystem.Meshes.Length, i =>
            {
                GLSLDrawCommand cmd = modelSystem.DrawCommands[i];
                int baseTriangleCount = cmd.FirstIndex / 3;

                Span<GLSLTriangle> traverseTriangles = new Span<GLSLTriangle>(Triangles, baseTriangleCount, cmd.Count / 3);
                Span<GLSLBlasNode> nodes = new Span<GLSLBlasNode>(Nodes, baseTriangleCount, traverseTriangles.Length);

                BuildBVH(traverseTriangles, nodes);

                for (int j = 0; j < nodes.Length; j++)
                {
                    if (nodes[j].TriCount > 0)
                    {
                        nodes[j].TriStartOrLeftChild += (uint)baseTriangleCount;
                    }
                }
            }).Wait();

            ModelSystem = modelSystem;
        }

        private static void BuildBVH(Span<GLSLTriangle> tris, Span<GLSLBlasNode> nodes)
        {
            int nodesUsed = 1;
            ref GLSLBlasNode root = ref nodes[0];
            root.TriCount = (uint)tris.Length;

            UpdateNodeBounds(tris, ref root);
            Subdivide(tris, nodes);

            void Subdivide(Span<GLSLTriangle> triangles, Span<GLSLBlasNode> nodes, int nodeID = 0)
            {
                ref GLSLBlasNode node = ref nodes[nodeID];
                if (node.TriCount < BLAS_MIN_TRIANGLE_COUNT_LEAF)
                    return;
                                
                Vector3 extent = node.Max - node.Min;
                int axis = 0;
                if (extent.Y > extent.X) axis = 1;
                if (extent.Z > extent[axis]) axis = 2;

                float splitPos = node.Min[axis] + extent[axis] * 0.5f;

                int i = (int)node.TriStartOrLeftChild;
                int j = (int)(i + node.TriCount - 1);
                while (i <= j)
                {
                    ref GLSLTriangle tri = ref triangles[i];
                    if (MyMath.Average(tri.Vertex0.Position, tri.Vertex1.Position, tri.Vertex2.Position)[axis] < splitPos)
                        i++;
                    else
                        Helper.Swap(ref tri, ref triangles[j--]);
                }

                uint leftCount = (uint)(i - node.TriStartOrLeftChild);
                if (leftCount == 0 || leftCount == node.TriCount)
                    return;

                int leftChildID = nodesUsed++;
                int rightChildID = nodesUsed++;

                nodes[leftChildID].TriStartOrLeftChild = node.TriStartOrLeftChild;
                nodes[leftChildID].TriCount = leftCount;

                nodes[rightChildID].TriStartOrLeftChild = (uint)i;
                nodes[rightChildID].TriCount = node.TriCount - leftCount;

                node.TriStartOrLeftChild = (uint)leftChildID;
                node.TriCount = 0;

                UpdateNodeBounds(triangles, ref nodes[leftChildID]);
                UpdateNodeBounds(triangles, ref nodes[rightChildID]);

                Subdivide(triangles, nodes, leftChildID);
                Subdivide(triangles, nodes, rightChildID);
            }
        }

        private static unsafe void UpdateNodeBounds(Span<GLSLTriangle> triangles, ref GLSLBlasNode node)
        {
            node.Min = new Vector3(float.MaxValue);
            node.Max = new Vector3(float.MinValue);

            for (int i = 0; i < node.TriCount; i++)
            {
                GLSLTriangle triangle = triangles[(int)(node.TriStartOrLeftChild + i)];
                node.Min = Vector3.ComponentMin(node.Min, triangle.Vertex0.Position);
                node.Min = Vector3.ComponentMin(node.Min, triangle.Vertex1.Position);
                node.Min = Vector3.ComponentMin(node.Min, triangle.Vertex2.Position);
                
                node.Max = Vector3.ComponentMax(node.Max, triangle.Vertex0.Position);
                node.Max = Vector3.ComponentMax(node.Max, triangle.Vertex1.Position);
                node.Max = Vector3.ComponentMax(node.Max, triangle.Vertex2.Position);
            }
        }
    }
}
