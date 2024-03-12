using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine
{
    public class BVH : IDisposable
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

        public struct BoxHitInfo
        {
            public BLAS.IndicesTriplet TriangleIndices;
            public int MeshID;
            public int InstanceID;
        }

        public TLAS Tlas { get; private set; }
        private BLAS[] blases;

        private readonly TypedBuffer<GpuBlasNode> blasBuffer;
        private readonly TypedBuffer<BLAS.IndicesTriplet> blasTriangleIndicesBuffer;
        private readonly TypedBuffer<GpuTlasNode> tlasBuffer;
        public BVH()
        {
            blasBuffer = new TypedBuffer<GpuBlasNode>();
            blasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 5);

            blasTriangleIndicesBuffer = new TypedBuffer<BLAS.IndicesTriplet>();
            blasTriangleIndicesBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 6);

            tlasBuffer = new TypedBuffer<GpuTlasNode>();
            tlasBuffer.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 7);

            blases = Array.Empty<BLAS>();
            Tlas = new TLAS(blases, Array.Empty<GpuDrawElementsCmd>(), Array.Empty<GpuMeshInstance>());
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
                            hitInfo.InstanceID = instanceID;
                        }
                    }

                }

                return hitInfo.T != tMax;
            }
        }

        public delegate void FuncIntersectLeafNode(in BoxHitInfo hitInfo);
        public void Intersect(in Box box, FuncIntersectLeafNode intersectFunc)
        {
            if (CPU_USE_TLAS)
            {
                Tlas.Intersect(box, intersectFunc);
            }
            else
            {
                for (int i = 0; i < blases.Length; i++)
                {
                    BLAS blas = Tlas.Blases[i];
                    ref readonly GpuDrawElementsCmd drawCmd = ref Tlas.DrawCommands[i];

                    for (int j = 0; j < drawCmd.InstanceCount; j++)
                    {
                        int instanceID = drawCmd.BaseInstance + j;
                        ref readonly GpuMeshInstance meshInstance = ref Tlas.MeshInstances[instanceID];

                        Box localBox = Box.Transformed(box, meshInstance.InvModelMatrix);
                        blas.Intersect(localBox, (in BLAS.IndicesTriplet hitTriangle) =>
                        {
                            BoxHitInfo hitInfo;
                            hitInfo.TriangleIndices = hitTriangle;
                            hitInfo.MeshID = i;
                            hitInfo.InstanceID = instanceID;

                            intersectFunc(hitInfo);
                        });
                    }
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
        public void AddMeshes(ReadOnlyMemory<GpuDrawElementsCmd> newMeshesDrawCommands, GpuDrawElementsCmd[] drawCommands, GpuMeshInstance[] meshInstances, Vector3[] vertexPositions, uint[] vertexIndices)
        {
            BLAS[] newBlases = new BLAS[newMeshesDrawCommands.Length];
            for (int i = 0; i < newBlases.Length; i++)
            {
                newBlases[i] = new BLAS(vertexPositions, vertexIndices, newMeshesDrawCommands.Span[i]);
            }
            Helper.ArrayAdd(ref blases, newBlases);
            BlasesBuild(blases.Length - newBlases.Length, newBlases.Length);

            Stopwatch sw = Stopwatch.StartNew();
            Tlas = new TLAS(blases, drawCommands, meshInstances);
            TlasBuild();
            Logger.Log(Logger.LogLevel.Info, $"Created Top Level Acceleration Structures (TLAS) for {Tlas.MeshInstances.Length} instances in {sw.ElapsedMilliseconds} milliseconds");
        }

        public void TlasBuild()
        {
            Tlas.Build();
            tlasBuffer.MutableAllocateElements(Tlas.Nodes);
        }

        public void BlasesBuild()
        {
            BlasesBuild(0, blases.Length);
        }

        public void BlasesBuild(int start, int count)
        {
            Stopwatch sw = Stopwatch.StartNew();
            
            Parallel.For(start, start + count, i =>
            //for (int i = start; i < start + count; i++)
            {
                blases[i].Build();
            });
            SetBlasBuffersContent();

            Logger.Log(Logger.LogLevel.Info, $"Created {blases.Length} new Bottom Level Acceleration Structures (BLAS) in {sw.ElapsedMilliseconds} milliseconds");
        }

        public unsafe void BlasesRefit(int start, int count)
        {
            //Parallel.For(start, start + count, i =>
            for (int i = start; i < start + count; i++)
            {
                blases[i].Refit();
            };

            int uploadedBlasNodes = 0;
            for (int i = 0; i < blases.Length; i++)
            {
                BLAS blas = blases[i];

                if (i >= start && i < start + count)
                {
                    blasBuffer.UploadElements(uploadedBlasNodes, blas.Nodes.Length, blas.Nodes[0]);
                }

                uploadedBlasNodes += blas.Nodes.Length;
            }
        }

        private unsafe void SetBlasBuffersContent()
        {
            blasBuffer.MutableAllocateElements(GetBlasesNodeCount());
            blasTriangleIndicesBuffer.MutableAllocateElements(GetBlasesTriangleIndicesCount());

            int uploadedBlasNodes = 0;
            int uploadedTriangleIndices = 0;
            for (int i = 0; i < blases.Length; i++)
            {
                BLAS blas = blases[i];

                blasBuffer.UploadElements(uploadedBlasNodes, blas.Nodes.Length, blas.Nodes[0]);
                blasTriangleIndicesBuffer.UploadElements(uploadedTriangleIndices, blas.TriangleIndices.Length, blas.TriangleIndices[0]);

                uploadedBlasNodes += blas.Nodes.Length;
                uploadedTriangleIndices += blas.TriangleIndices.Length;
            }
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
            tlasBuffer.Dispose();
        }
    }
}
