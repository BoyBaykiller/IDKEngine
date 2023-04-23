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
            public int MeshID;
            public int InstanceID;
            public Vector3 Bary;
            public GLSLTriangle Triangle;
        }

        public readonly int MaxBlasTreeDepth;
        public readonly BLAS[] Blases;

        private readonly ModelSystem ModelSystem;
        private readonly BufferObject BlasBuffer;
        private readonly BufferObject BlasTriangleBuffer;
        private readonly GLSLTriangle[] triangles;

        public unsafe BVH(ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;

            triangles = new GLSLTriangle[modelSystem.Indices.Length / 3];
            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                ref readonly GLSLDrawElementsCommand cmd = ref modelSystem.DrawCommands[i];
                for (int j = cmd.FirstIndex; j < cmd.FirstIndex + cmd.Count; j += 3)
                {
                    triangles[j / 3].Vertex0 = modelSystem.Vertices[modelSystem.Indices[j + 0] + cmd.BaseVertex];
                    triangles[j / 3].Vertex1 = modelSystem.Vertices[modelSystem.Indices[j + 1] + cmd.BaseVertex];
                    triangles[j / 3].Vertex2 = modelSystem.Vertices[modelSystem.Indices[j + 2] + cmd.BaseVertex];
                }
            }

            int maxTreeDepth = 0;
            Blases = new BLAS[modelSystem.Meshes.Length];
            System.Threading.Tasks.Parallel.For(0, modelSystem.Meshes.Length, i =>
            {
                ref readonly GLSLDrawElementsCommand cmd = ref modelSystem.DrawCommands[i];
                int baseTriangleCount = cmd.FirstIndex / 3;
                fixed (GLSLTriangle* ptr = &triangles[baseTriangleCount])
                {
                    Blases[i] = new BLAS(ptr, cmd.Count / 3, out int treeDepth);
                    Helper.InterlockedMax(ref maxTreeDepth, treeDepth);
                }
                for (int j = 0; j < Blases[i].Nodes.Length; j++)
                {
                    if (Blases[i].Nodes[j].TriCount > 0)
                    {
                        Blases[i].Nodes[j].TriStartOrLeftChild += (uint)baseTriangleCount;
                    }
                }
            });
            MaxBlasTreeDepth = maxTreeDepth;

            BlasBuffer = new BufferObject();
            BlasTriangleBuffer = new BufferObject();

            if (triangles.Length > 0)
            {
                BlasBuffer.ImmutableAllocate(sizeof(GLSLBlasNode) * Blases.Sum(blas => blas.Nodes.Length), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
                BlasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);
                int nodesUploaded = 0;
                for (int i = 0; i < Blases.Length; i++)
                {
                    BlasBuffer.SubData(nodesUploaded * sizeof(GLSLBlasNode), Blases[i].Nodes.Length * sizeof(GLSLBlasNode), Blases[i].Nodes);
                    nodesUploaded += Blases[i].Nodes.Length;
                }

                BlasTriangleBuffer.ImmutableAllocate(sizeof(GLSLTriangle) * triangles.Length, triangles, BufferStorageFlags.None);
                BlasTriangleBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5);
            }
        }

        public unsafe bool Intersect(in Ray ray, out HitInfo hitInfo)
        {
            hitInfo = new HitInfo();
            hitInfo.T = float.MaxValue;

            float rayTMin = 0.0f;
            float rayTMax = 0.0f;
            uint* stack = stackalloc uint[MaxBlasTreeDepth];
            for (int i = 0; i < ModelSystem.Meshes.Length; i++)
            {
                ref readonly GLSLDrawElementsCommand cmd = ref ModelSystem.DrawCommands[i];

                int glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
                Ray localRay = ray.Transformed(ModelSystem.MeshInstances[glInstanceID].InvModelMatrix);

                uint stackPtr = 0;
                uint stackTop = 0;
                while (true)
                {
                    ref readonly GLSLBlasNode node = ref Blases[i].Nodes[stackTop];
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
                            hitInfo.Triangle = triangles[k];
                            if (MyMath.RayTriangleIntersect(localRay, hitInfo.Triangle.Vertex0.Position, hitInfo.Triangle.Vertex1.Position, hitInfo.Triangle.Vertex2.Position, out Vector4 baryT) && baryT.W > 0.0f && baryT.W < hitInfo.T)
                            {
                                hitInfo.Bary = baryT.Xyz;
                                hitInfo.T = baryT.W;
                                hitInfo.MeshID = i;
                                hitInfo.InstanceID = glInstanceID;
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
