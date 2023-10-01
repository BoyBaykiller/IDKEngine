using System;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH : IDisposable
    {
        public const bool CPU_USE_TLAS = false;

        public int MaxBlasTreeDepth { get; private set; }
        public readonly TLAS Tlas;

        private readonly BufferObject blasBuffer;
        private readonly BufferObject blasTriangleBuffer;
        private readonly BufferObject tlasBuffer;
        public BVH()
        {
            blasBuffer = new BufferObject();
            blasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 4);

            blasTriangleBuffer = new BufferObject();
            blasTriangleBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5);

            tlasBuffer = new BufferObject();
            tlasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            Tlas = new TLAS();
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

                for (int i = 0; i < Tlas.BlasesInstances.Count; i++)
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

        /// <summary>
        /// Builds new BLAS'es from the associated mesh data, updates the TLAS for them and updates the corresponding GPU buffers.
        /// Building BLAS'es is expected to be slow. The process is done in parallel so prefer to pass multiple meshes at once to better utilize the hardware.
        /// </summary>
        /// <param name="meshInstances"></param>
        /// <param name="newMeshesDrawCommands"></param>
        /// <param name="vertices"></param>
        /// <param name="indices"></param>
        public void AddMeshesAndBuild(ReadOnlyMemory<GpuDrawElementsCmd> newMeshesDrawCommands, GpuMeshInstance[] meshInstances, GpuDrawVertex[] vertices, uint[] indices)
        {
            TLAS.BlasInstances[] newBlasInstances = CreateBlasInstancesFromGeometry(newMeshesDrawCommands, meshInstances, vertices, indices);
            AddBlases(newBlasInstances);
            Logger.Log(Logger.LogLevel.Info, $"Created {newBlasInstances.Length} new Bottom Level Acceleration Structures (BLAS)");

            TlasBuild();
            Logger.Log(Logger.LogLevel.Info, $"Created Top Level Acceleration Structures (TLAS) for {Tlas.BlasesInstances.Sum(blasInstances => blasInstances.Instances.Count)} instances");
        }

        private void AddBlases(TLAS.BlasInstances[] newBlasInstances)
        {
            Tlas.AddBlases(newBlasInstances);
            SetBlasBuffersContent(CollectionsMarshal.AsSpan(Tlas.BlasesInstances));

            for (int i = 0; i < newBlasInstances.Length; i++)
            {
                MaxBlasTreeDepth = Math.Max(MaxBlasTreeDepth, newBlasInstances[i].Blas.TreeDepth);
            }
        }

        public void TlasBuild()
        {
            Tlas.Build();
            SetTlasBufferContent(Tlas.Nodes);
        }

        private unsafe void SetBlasBuffersContent(ReadOnlySpan<TLAS.BlasInstances> blasInstances)
        {
            blasBuffer.MutableAllocate((nint)sizeof(GpuBlasNode) * blasInstances.Sum(blasInstances => blasInstances.Blas.Nodes.Length), IntPtr.Zero);
            blasTriangleBuffer.MutableAllocate((nint)sizeof(GpuTriangle) * blasInstances.Sum(blasInstances => blasInstances.Blas.Triangles.Length), IntPtr.Zero);

            nint uploadedBlasNodesCount = 0;
            nint uploadedTrianglesCount = 0;
            for (int i = 0; i < blasInstances.Length; i++)
            {
                BLAS blas = blasInstances[i].Blas;

                blasBuffer.SubData(uploadedBlasNodesCount * sizeof(GpuBlasNode), blas.Nodes.Length * (nint)sizeof(GpuBlasNode), blas.Nodes);
                blasTriangleBuffer.SubData(uploadedTrianglesCount * sizeof(GpuTriangle), blas.Triangles.Length * (nint)sizeof(GpuTriangle), blas.Triangles);

                uploadedBlasNodesCount += blas.Nodes.Length;
                uploadedTrianglesCount += blas.Triangles.Length;
            }
        }
        private unsafe void SetTlasBufferContent(ReadOnlySpan<GpuTlasNode> tlasNodes)
        {
            tlasBuffer.MutableAllocate(sizeof(GpuTlasNode) * tlasNodes.Length, tlasNodes[0]);
        }

        public void Dispose()
        {
            blasTriangleBuffer.Dispose();
            blasBuffer.Dispose();
        }

        private static TLAS.BlasInstances[] CreateBlasInstancesFromGeometry(ReadOnlyMemory<GpuDrawElementsCmd> drawCommands, GpuMeshInstance[] meshInstances, GpuDrawVertex[] vertices, uint[] indices)
        {
            TLAS.BlasInstances[] blasInstances = new TLAS.BlasInstances[drawCommands.Length];
            Parallel.For(0, blasInstances.Length, i =>
            {
                ref readonly GpuDrawElementsCmd cmd = ref drawCommands.Span[i];

                GpuTriangle[] blasTriangles = new GpuTriangle[cmd.Count / 3];
                for (int j = 0; j < blasTriangles.Length; j++)
                {
                    blasTriangles[j].Vertex0 = vertices[cmd.BaseVertex + indices[cmd.FirstIndex + (j * 3) + 0]];
                    blasTriangles[j].Vertex1 = vertices[cmd.BaseVertex + indices[cmd.FirstIndex + (j * 3) + 1]];
                    blasTriangles[j].Vertex2 = vertices[cmd.BaseVertex + indices[cmd.FirstIndex + (j * 3) + 2]];
                }

                BLAS blas = new BLAS(blasTriangles);
                blas.Build();
                blasInstances[i] = new TLAS.BlasInstances()
                {
                    Blas = blas,
                    Instances = new ArraySegment<GpuMeshInstance>(meshInstances, cmd.BaseInstance, cmd.InstanceCount),
                };
            });

            return blasInstances;
        }
    }
}
