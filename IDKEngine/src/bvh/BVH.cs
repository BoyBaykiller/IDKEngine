using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH : IDisposable
    {
        public const bool CPU_USE_TLAS = false;

        public int MaxBlasTreeDepth { get; private set; }
        public TLAS Tlas { get; private set; }

        private readonly ModelSystem ModelSystem;
        private readonly BufferObject blasBuffer;
        private readonly BufferObject blasTriangleBuffer;
        private readonly BufferObject tlasBuffer;
        public BVH(ModelSystem modelSystem)
        {
            ModelSystem = modelSystem;

            blasBuffer = new BufferObject();
            blasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);

            blasTriangleBuffer = new BufferObject();
            blasTriangleBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5);

            tlasBuffer = new BufferObject();
            tlasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            FullRebuild();
        }

        public bool Intersect(in Ray ray, out TLAS.HitInfo hitInfo, float tMax = float.MaxValue)
        {
            if (CPU_USE_TLAS)
            {
                return Tlas.Intersect(ray, out hitInfo, tMax);
            }
            else
            {
                hitInfo = new TLAS.HitInfo();
                hitInfo.T = tMax;

                for (int i = 0; i < Tlas.BlasesInstances.Length; i++)
                {
                    TLAS.BlasInstances blasInstances = Tlas.BlasesInstances[i];
                    BLAS blas = blasInstances.Blas;

                    int glInstanceID = 0; // TODO: Work out actual instanceID value
                    Ray localRay = ray.Transformed(blasInstances.Instances[glInstanceID].InvModelMatrix);
                    if (blas.Intersect(localRay, out BLAS.HitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.Triangle = blasHitInfo.Triangle;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;

                        hitInfo.MeshID = i;
                        hitInfo.InstanceID = glInstanceID;
                    }
                }

                return hitInfo.T != tMax;
            }
        }
       
        private void FullRebuild()
        {
            TLAS.BlasInstances[] blasesInstances = new TLAS.BlasInstances[ModelSystem.Meshes.Length];
            Parallel.For(0, blasesInstances.Length, i =>
            {
                ref readonly GLSLDrawElementsCmd cmd = ref ModelSystem.DrawCommands[i];

                GLSLTriangle[] blasTriangles = new GLSLTriangle[cmd.Count / 3];
                for (int j = 0; j < blasTriangles.Length; j++)
                {
                    blasTriangles[j].Vertex0 = ModelSystem.Vertices[cmd.BaseVertex + ModelSystem.Indices[cmd.FirstIndex + (j * 3) + 0]];
                    blasTriangles[j].Vertex1 = ModelSystem.Vertices[cmd.BaseVertex + ModelSystem.Indices[cmd.FirstIndex + (j * 3) + 1]];
                    blasTriangles[j].Vertex2 = ModelSystem.Vertices[cmd.BaseVertex + ModelSystem.Indices[cmd.FirstIndex + (j * 3) + 2]];
                }

                BLAS blas = new BLAS(blasTriangles);
                blasesInstances[i] = new TLAS.BlasInstances()
                { 
                    Blas = blas,
                    Instances = new ArraySegment<GLSLMeshInstance>(ModelSystem.MeshInstances, cmd.BaseInstance, cmd.InstanceCount),
                };
            });
            Tlas = new TLAS(blasesInstances);
            UpdateBlasBuffer();
            
            MaxBlasTreeDepth = 0;
            for (int i = 0; i < blasesInstances.Length; i++)
            {
                MaxBlasTreeDepth = Math.Max(MaxBlasTreeDepth, blasesInstances[i].Blas.TreeDepth);
            }

            Tlas.Build();
            UpdateTlasBuffer();
        }

        public unsafe void UpdateBlasBuffer()
        {
            blasBuffer.MutableAllocate(sizeof(GLSLBlasNode) * Tlas.BlasesInstances.Sum(blasInstances => blasInstances.Blas.Nodes.Length), IntPtr.Zero);
            blasTriangleBuffer.MutableAllocate(sizeof(GLSLTriangle) * Tlas.BlasesInstances.Sum(blasInstances => blasInstances.Blas.Triangles.Length), IntPtr.Zero);

            int uploadedBlasNodesCount = 0;
            int uploadedTrianglesCount = 0;
            for (int i = 0; i < Tlas.BlasesInstances.Length; i++)
            {
                BLAS blas = Tlas.BlasesInstances[i].Blas;

                blasBuffer.SubData(uploadedBlasNodesCount * sizeof(GLSLBlasNode), blas.Nodes.Length * sizeof(GLSLBlasNode), blas.Nodes);
                blasTriangleBuffer.SubData(uploadedTrianglesCount * sizeof(GLSLTriangle), blas.Triangles.Length * sizeof(GLSLTriangle), blas.Triangles);

                uploadedBlasNodesCount += blas.Nodes.Length;
                uploadedTrianglesCount += blas.Triangles.Length;
            }
        }

        public unsafe void UpdateTlasBuffer()
        {
            tlasBuffer.MutableAllocate(sizeof(GLSLTlasNode) * Tlas.Nodes.Length, Tlas.Nodes);
        }

        public void Dispose()
        {
            blasTriangleBuffer.Dispose();
            blasBuffer.Dispose();
        }
    }
}
