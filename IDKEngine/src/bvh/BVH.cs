using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.Shapes;

namespace IDKEngine
{
    class BVH : IDisposable
    {
        public const bool CPU_USE_TLAS = false;

        public struct RayHitInfo
        {
            public BLAS.IndicesTriplet TriangleIndices;
            public Vector3 Bary;
            public float T;
            public int MeshID;
            public int InstanceID;
        }

        public struct PrimitiveHitInfo
        {
            public BLAS.IndicesTriplet TriangleIndices;
            public int MeshID;
            public int InstanceID;
        }

        public TLAS Tlas { get; private set; }
        private BLAS[] blases;

        private readonly BufferObject blasBuffer;
        private readonly BufferObject blasTriangleIndicesBuffer;
        private readonly BufferObject tlasBuffer;
        public BVH()
        {
            blasBuffer = new BufferObject();
            blasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5);

            blasTriangleIndicesBuffer = new BufferObject();
            blasTriangleIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            tlasBuffer = new BufferObject();
            tlasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7);

            blases = Array.Empty<BLAS>();
        }

        public bool Intersect(in Ray ray, out RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            if (CPU_USE_TLAS)
            {
                return Tlas.Intersect(ray, out hitInfo, tMax);
            }
            else
            {
                hitInfo = new RayHitInfo();
                hitInfo.T = tMax;

                for (int i = 0; i < Tlas.Blases.Length; i++)
                {
                    BLAS blas = Tlas.Blases[i];
                    ref readonly GpuDrawElementsCmd drawCmd = ref Tlas.DrawCommands[i];

                    for (int j = 0; j < drawCmd.InstanceCount; j++)
                    {
                        int instanceID = drawCmd.BaseInstance + j;
                        ref readonly GpuMeshInstance meshInstance = ref Tlas.MeshInstances[instanceID];

                        Ray localRay = ray.Transformed(meshInstance.InvModelMatrix);
                        if (blas.Intersect(localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                        {
                            hitInfo.TriangleIndices = blasHitInfo.TriangleIndices;
                            hitInfo.Bary = blasHitInfo.Bary;
                            hitInfo.T = blasHitInfo.T;

                            hitInfo.MeshID = i;
                            hitInfo.InstanceID = j;
                        }
                    }

                }

                return hitInfo.T != tMax;
            }
        }

        public delegate void IntersectFunc(in PrimitiveHitInfo hitInfo);
        public void Intersect(in Box box, IntersectFunc intersectFunc)
        {
            for (int i = 0; i < Tlas.Blases.Length; i++)
            {
                BLAS blas = Tlas.Blases[i];
                ref readonly GpuDrawElementsCmd drawCmd = ref Tlas.DrawCommands[i];

                for (int j = 0; j < drawCmd.InstanceCount; j++)
                {
                    int instanceID = drawCmd.BaseInstance + j;
                    ref readonly GpuMeshInstance meshInstance = ref Tlas.MeshInstances[instanceID];

                    Box localBox = Box.Transformed(box, meshInstance.InvModelMatrix);
                    blas.Intersect(localBox, (in BLAS.IndicesTriplet indicesTriplet) =>
                    {
                        PrimitiveHitInfo hitInfo;
                        hitInfo.TriangleIndices = indicesTriplet;
                        hitInfo.MeshID = i;
                        hitInfo.InstanceID = instanceID;

                        intersectFunc(hitInfo);
                    });
                }
            }
        }

        /// <summary>
        /// Builds new BLAS'es from the associated mesh data, updates the TLAS for them and updates the corresponding GPU buffers.
        /// Building BLAS'es is expected to be slow. The process is done in parallel so prefer to pass multiple meshes at once to better utilize the hardware.
        /// </summary>
        /// <param name="meshInstances"></param>
        /// <param name="newMeshesDrawCommands"></param>
        /// <param name="vertexPositions"></param>
        /// <param name="vertexIndices"></param>
        public void AddMeshesAndBuild(ReadOnlyMemory<GpuDrawElementsCmd> newMeshesDrawCommands, GpuDrawElementsCmd[] drawCommands, GpuMeshInstance[] meshInstances, Vector3[] vertexPositions, uint[] vertexIndices)
        {
            BLAS[] newBlases = new BLAS[newMeshesDrawCommands.Length];
            for (int i = 0; i < newBlases.Length; i++)
            {
                newBlases[i] = new BLAS(vertexPositions, vertexIndices, newMeshesDrawCommands.Span[i]);
            }
            Helper.ArrayAdd(ref blases, newBlases);
            BlasesBuild(blases.Length - newBlases.Length, newBlases.Length);

            Tlas = new TLAS(blases, drawCommands, meshInstances);
            TlasBuild();
        }

        public void TlasBuild()
        {
            Tlas.Build();
            SetTlasBufferContent(Tlas.Nodes);
        }

        public void BlasesBuild()
        {
            BlasesBuild(0, blases.Length);
        }

        public void BlasesBuild(int start, int count)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            
            Parallel.For(start, start + count, i =>
            {
                blases[i].Build();
            });
            SetBlasBuffersContent();

            Logger.Log(Logger.LogLevel.Info, $"Created {blases.Length} new Bottom Level Acceleration Structures (BLAS) in {sw.ElapsedMilliseconds} milliseconds");
        }

        private unsafe void SetBlasBuffersContent()
        {
            blasBuffer.MutableAllocate((nint)sizeof(GpuBlasNode) * GetBlasesNodeCount(), IntPtr.Zero);
            blasTriangleIndicesBuffer.MutableAllocate((nint)sizeof(BLAS.IndicesTriplet) * GetBlasesTriangleIndicesCount(), IntPtr.Zero);

            nint uploadedBlasNodesCount = 0;
            nint uploadedTriangleIndicesCount = 0;
            for (int i = 0; i < blases.Length; i++)
            {
                BLAS blas = blases[i];

                blasBuffer.SubData(uploadedBlasNodesCount * sizeof(GpuBlasNode), blas.Nodes.Length * (nint)sizeof(GpuBlasNode), blas.Nodes);
                blasTriangleIndicesBuffer.SubData(uploadedTriangleIndicesCount * sizeof(BLAS.IndicesTriplet), blas.TriangleIndices.Length * (nint)sizeof(BLAS.IndicesTriplet), blas.TriangleIndices);

                uploadedBlasNodesCount += blas.Nodes.Length;
                uploadedTriangleIndicesCount += blas.TriangleIndices.Length;
            }
        }
        private unsafe void SetTlasBufferContent(ReadOnlySpan<GpuTlasNode> tlasNodes)
        {
            tlasBuffer.MutableAllocate(sizeof(GpuTlasNode) * tlasNodes.Length, tlasNodes[0]);
        }

        public int GetBlasesTriangleIndicesCount()
        {
            return blases.Sum(blasInstances => blasInstances.TriangleIndices.Length);
        }

        public int GetBlasesNodeCount()
        {
            return blases.Sum(blas => blas.Nodes.Length);
        }

        public void Dispose()
        {
            blasTriangleIndicesBuffer.Dispose();
            blasBuffer.Dispose();
        }
    }
}
