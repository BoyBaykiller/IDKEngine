using System;
using System.Linq;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH
    {
        public struct HitInfo
        {
            public float T;
            public int HitID;
            public int InstanceID;
            public Vector3 Bary;
            public GLSLTriangle Triangle;
        }

        private readonly ModelSystem ModelSystem;
        private readonly BufferObject BlasBuffer;
        private readonly BufferObject TriangleBuffer;
        private readonly BLAS[] blases;
        private readonly GLSLTriangle[] triangles;
        public unsafe BVH(ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;

            triangles = new GLSLTriangle[modelSystem.Indices.Length / 3];
            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                ref readonly GLSLDrawCommand cmd = ref modelSystem.DrawCommands[i];
                for (int j = cmd.FirstIndex; j < cmd.FirstIndex + cmd.Count; j += 3)
                {
                    triangles[j / 3].Vertex0 = modelSystem.Vertices[modelSystem.Indices[j + 0] + cmd.BaseVertex];
                    triangles[j / 3].Vertex1 = modelSystem.Vertices[modelSystem.Indices[j + 1] + cmd.BaseVertex];
                    triangles[j / 3].Vertex2 = modelSystem.Vertices[modelSystem.Indices[j + 2] + cmd.BaseVertex];
                }
            }

            blases = new BLAS[modelSystem.Meshes.Length];
            System.Threading.Tasks.Parallel.For(0, modelSystem.Meshes.Length, i =>
            {
                ref readonly GLSLDrawCommand cmd = ref modelSystem.DrawCommands[i];
                int baseTriangleCount = cmd.FirstIndex / 3;
                fixed (GLSLTriangle* ptr = triangles)
                {
                    blases[i] = new BLAS(ptr + baseTriangleCount, cmd.Count / 3);
                }
                for (int j = 0; j < blases[i].Nodes.Length; j++)
                    if (blases[i].Nodes[j].TriCount > 0)
                        blases[i].Nodes[j].TriStartOrLeftChild += (uint)baseTriangleCount;
            });

            BlasBuffer = new BufferObject();
            BlasBuffer.ImmutableAllocate(sizeof(GLSLBlasNode) * blases.Sum(blas => blas.Nodes.Length), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            BlasBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BlasBuffer.Size);
            int nodesUploaded = 0;
            for (int i = 0; i < blases.Length; i++)
            {
                BlasBuffer.SubData(nodesUploaded * sizeof(GLSLBlasNode), blases[i].Nodes.Length * sizeof(GLSLBlasNode), blases[i].Nodes);
                nodesUploaded += blases[i].Nodes.Length;
            }

            TriangleBuffer = new BufferObject();
            TriangleBuffer.ImmutableAllocate(sizeof(GLSLTriangle) * triangles.Length, triangles, BufferStorageFlags.DynamicStorageBit);
            TriangleBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, TriangleBuffer.Size);
        }

        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo)
        {
            hitInfo = new HitInfo();
            hitInfo.T = float.MaxValue;

            float rayTMin = 0.0f;
            float rayTMax = 0.0f;
            uint* stack = stackalloc uint[32];
            for (int i = 0; i < ModelSystem.Meshes.Length; i++)
            {
                const uint glInstanceID = 0; // TODO: Work out actual instanceID value
                Ray localRay = ray.Transformed(ModelSystem.ModelMatrices[i][glInstanceID]);

                uint stackPtr = 0;
                uint stackTop = 0;
                while (true)
                {
                    ref readonly GLSLBlasNode node = ref blases[i].Nodes[stackTop];
                    if (!(MyMath.RayCuboidIntersect(localRay, node.Min, node.Max, out rayTMin, out rayTMax) && rayTMax > 0.0f && rayTMin < hitInfo.T))
                    {
                        if (stackPtr == 0) break;
                        stackTop = stack[--stackPtr];
                        continue;
                    }

                    if (node.TriCount > 0)
                    {
                        for (int k = (int)node.TriStartOrLeftChild; k < node.TriStartOrLeftChild + node.TriCount; k++)
                        {
                            ref readonly GLSLTriangle triangle = ref triangles[k];
                            if (MyMath.RayTriangleIntersect(localRay, triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position, out Vector4 baryT) && baryT.W > 0.0f && baryT.W < hitInfo.T)
                            {
                                hitInfo.Bary = baryT.Xyz;
                                hitInfo.T = baryT.W;
                                hitInfo.HitID = i;
                                hitInfo.Triangle = triangle;
                                hitInfo.InstanceID = k;
                            }
                        }
                        if (stackPtr == 0) break;
                        stackTop = stack[--stackPtr];
                    }
                    else
                    {
                        stackTop = node.TriStartOrLeftChild;
                        stack[stackPtr++] = node.TriStartOrLeftChild + 1;
                    }
                }
            }

            return hitInfo.T != float.MaxValue;
        }
    }
}
