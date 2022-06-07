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
        public readonly GLSLNode[] Nodes;
        public readonly GLSLVertex[] Vertices;
        public unsafe BLAS(ModelSystem modelSystem)
        {
            Vertices = new GLSLVertex[modelSystem.Indices.Length];
            Nodes = new GLSLNode[Vertices.Length / 3 * 2];
            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                GLSLDrawCommand cmd = modelSystem.DrawCommands[i];
                for (int j = cmd.FirstIndex; j < cmd.FirstIndex + cmd.Count; j++)
                {
                    Vertices[j] = modelSystem.Vertices[modelSystem.Indices[j] + cmd.BaseVertex];
                }
            }

            ParallelLoopResult bvhLoadResult = Parallel.For(0, modelSystem.Meshes.Length, (int i) =>
            {
                GLSLDrawCommand cmd = modelSystem.DrawCommands[i];
                fixed (void* ptr = &Vertices[cmd.FirstIndex])
                {
                    Span<GLSLTriangle> triangles = new Span<GLSLTriangle>(ptr, cmd.Count / 3);
                    Span<GLSLNode> nodes = new Span<GLSLNode>(Nodes, cmd.FirstIndex / 3 * 2, triangles.Length * 2);

                    BuildBVH(triangles, nodes);

                    for (int j = 0; j < nodes.Length; j++)
                    {
                        if (nodes[j].TriCount > 0)
                        {
                            nodes[j].TriStartOrLeftChild += (uint)cmd.FirstIndex / 3;
                        }
                    }
                    modelSystem.Meshes[i].NodeStart = cmd.FirstIndex / 3 * 2;
                }
            });
            while (!bvhLoadResult.IsCompleted) ;

            modelSystem.UpdateMeshBuffer(0, modelSystem.Meshes.Length);
            ModelSystem = modelSystem;
        }

        private static void BuildBVH(Span<GLSLTriangle> tris, Span<GLSLNode> nodes)
        {
            int nodesUsed = 1;
            ref GLSLNode root = ref nodes[0];
            root.TriCount = (uint)tris.Length;

            UpdateNodeBounds(tris, ref root);
            Subdivide(tris, nodes);

            void Subdivide(Span<GLSLTriangle> triangles, Span<GLSLNode> nodes, int nodeID = 0)
            {
                ref GLSLNode node = ref nodes[nodeID];
                if (node.TriCount <= BLAS_MIN_TRIANGLE_COUNT_LEAF)
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

        private static unsafe void UpdateNodeBounds(Span<GLSLTriangle> triangles, ref GLSLNode node)
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
