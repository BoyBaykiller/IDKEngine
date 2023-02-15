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
        public struct RayHitInfo
        {
            public float T;
            public int MeshIndex;
            public int InstanceID;
            public Vector3 Bary;
            public GLSLTriangle Triangle;
        }

        public struct AABBHitInfo
        {
            public int HitID;
            public int InstanceID;
            public GLSLTriangle Triangle;
        }

        public readonly int MaxBlasTreeDepth;
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

            int maxTreeDepth = 0;
            blases = new BLAS[modelSystem.Meshes.Length];
            System.Threading.Tasks.Parallel.For(0, modelSystem.Meshes.Length, i =>
            {
                ref readonly GLSLDrawCommand cmd = ref modelSystem.DrawCommands[i];
                int baseTriangleCount = cmd.FirstIndex / 3;
                fixed (GLSLTriangle* ptr = triangles)
                {
                    blases[i] = new BLAS(ptr + baseTriangleCount, cmd.Count / 3, out int treeDepth);
                    Helper.InterlockedMax(ref maxTreeDepth, treeDepth);
                }
                for (int j = 0; j < blases[i].Nodes.Length; j++)
                    if (blases[i].Nodes[j].TriCount > 0)
                        blases[i].Nodes[j].TriStartOrLeftChild += (uint)baseTriangleCount;
            });
            MaxBlasTreeDepth = maxTreeDepth;

            BlasBuffer = new BufferObject();
            TriangleBuffer = new BufferObject();

            if (triangles.Length > 0)
            {
                BlasBuffer.ImmutableAllocate(sizeof(GLSLBlasNode) * blases.Sum(blas => blas.Nodes.Length), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
                BlasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 1);
                int nodesUploaded = 0;
                for (int i = 0; i < blases.Length; i++)
                {
                    BlasBuffer.SubData(nodesUploaded * sizeof(GLSLBlasNode), blases[i].Nodes.Length * sizeof(GLSLBlasNode), blases[i].Nodes);
                    nodesUploaded += blases[i].Nodes.Length;
                }

                TriangleBuffer.ImmutableAllocate(sizeof(GLSLTriangle) * triangles.Length, triangles, BufferStorageFlags.None);
                TriangleBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3);
            }
        }

        public unsafe bool Intersect(in Ray ray, out RayHitInfo hitInfo, float maxT = float.MaxValue)
        {
            hitInfo = new RayHitInfo();
            hitInfo.T = maxT;

            float rayTMin = 0.0f;
            float rayTMax = 0.0f;
            uint* stack = stackalloc uint[MaxBlasTreeDepth];
            for (int i = 0; i < ModelSystem.Meshes.Length; i++)
            {
                ref readonly GLSLDrawCommand cmd = ref ModelSystem.DrawCommands[i];

                int glInstanceID = cmd.BaseInstance + 0; // TODO: Work out actual instanceID value
                Ray localRay = ray.Transformed(ModelSystem.MeshInstances[glInstanceID].InvModelMatrix);

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
                            hitInfo.Triangle = triangles[k];
                            if (MyMath.RayTriangleIntersect(localRay, hitInfo.Triangle.Vertex0.Position, hitInfo.Triangle.Vertex1.Position, hitInfo.Triangle.Vertex2.Position, out Vector4 baryT) && baryT.W > 0.0f && baryT.W < hitInfo.T)
                            {
                                hitInfo.Bary = baryT.Xyz;
                                hitInfo.T = baryT.W;
                                hitInfo.MeshIndex = i;
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

            return hitInfo.T != maxT;
        }

        public unsafe bool Intersect(in AABB worldSpaceAabb, out AABBHitInfo hitInfo)
        {
            hitInfo = new AABBHitInfo();

            Vector3 boxCenter = worldSpaceAabb.Center;
            Vector3 halfSize = worldSpaceAabb.HalfSize;
            
            for (int i = 0; i < ModelSystem.Meshes.Length; i++)
            {
                ref readonly GLSLDrawCommand cmd = ref ModelSystem.DrawCommands[i];

                int glInstanceID = cmd.BaseInstance + 0;  // TODO: Work out actual instanceID value
                Matrix4 invModel = ModelSystem.MeshInstances[glInstanceID].InvModelMatrix;
                
                Vector3 localCenter = (new Vector4(boxCenter, 1.0f) * invModel).Xyz;
                AABB localAabb = worldSpaceAabb;
                localAabb.Transform(invModel);

                ref readonly GLSLBlasNode topNode = ref blases[i].Nodes[0];
                if (MyMath.AabbAabbIntersect(localAabb, topNode.Min, topNode.Max))
                {
                    for (int j = cmd.FirstIndex; j < cmd.FirstIndex + cmd.Count; j += 3)
                    {
                        hitInfo.Triangle = triangles[j / 3];
                        if (MyMath.TriangleBoxIntersect(hitInfo.Triangle.Vertex0.Position, hitInfo.Triangle.Vertex1.Position, hitInfo.Triangle.Vertex2.Position, localCenter, halfSize))
                        {
                            hitInfo.HitID = i;
                            hitInfo.InstanceID = glInstanceID;
                            return true;
                        }
                    }

                }
            }

            return false;
        }
    }
}
