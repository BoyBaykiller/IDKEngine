using System;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public class BVH : IDisposable
    {
        private bool _gpuUseTlas;
        public bool GpuUseTlas
        {
            get => _gpuUseTlas;

            set
            {
                _gpuUseTlas = value;
                BBG.AbstractShaderProgram.SetShaderInsertionValue("USE_TLAS", GpuUseTlas);
            }
        }

        private int _maxBlasTreeDepth;
        public int MaxBlasTreeDepth
        {
            get => _maxBlasTreeDepth;

            set
            {
                _maxBlasTreeDepth = value;

                BBG.AbstractShaderProgram.SetShaderInsertionValue("MAX_BLAS_TREE_DEPTH", MaxBlasTreeDepth);
            }
        }

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

        public bool CpuUseTlas;
        public bool UpdateTlas;

        public TLAS Tlas { get; private set; }
        private BLAS[] blases;

        private readonly BBG.TypedBuffer<GpuBlasNode> blasBuffer;
        private readonly BBG.TypedBuffer<BLAS.IndicesTriplet> blasTriangleIndicesBuffer;
        private readonly BBG.TypedBuffer<GpuTlasNode> tlasBuffer;
        public BVH()
        {
            blasBuffer = new BBG.TypedBuffer<GpuBlasNode>();
            blasBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 4);

            blasTriangleIndicesBuffer = new BBG.TypedBuffer<BLAS.IndicesTriplet>();
            blasTriangleIndicesBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 5);

            tlasBuffer = new BBG.TypedBuffer<GpuTlasNode>();
            tlasBuffer.BindBufferBase(BBG.Buffer.BufferTarget.ShaderStorage, 6);

            blases = Array.Empty<BLAS>();
            Tlas = new TLAS(blases, Array.Empty<BBG.DrawElementsIndirectCommand>(), Array.Empty<GpuMeshInstance>());

            GpuUseTlas = false; // Only pays of on scenes with many BLAS'es (not sponza). So disabled by default.
            CpuUseTlas = false;
            UpdateTlas = false;

            MaxBlasTreeDepth = 1;
        }

        public bool Intersect(in Ray ray, out RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            if (CpuUseTlas)
            {
                return Tlas.Intersect(ray, out hitInfo, tMax);
            }
            else
            {
                hitInfo = new RayHitInfo();
                hitInfo.T = tMax;

                for (int i = 0; i < Tlas.MeshInstances.Length; i++)
                {
                    ref readonly GpuMeshInstance meshInstance = ref Tlas.MeshInstances[i];
                    BLAS blas = Tlas.Blases[meshInstance.MeshIndex];

                    Ray localRay = ray.Transformed(meshInstance.InvModelMatrix);
                    if (blas.Intersect(localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.TriangleIndices = blasHitInfo.TriangleIndices;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;

                        hitInfo.MeshID = meshInstance.MeshIndex;
                        hitInfo.InstanceID = i;
                    }
                }

                return hitInfo.T != tMax;
            }
        }

        public delegate void FuncIntersectLeafNode(in BoxHitInfo hitInfo);
        public void Intersect(in Box box, FuncIntersectLeafNode intersectFunc)
        {
            if (CpuUseTlas)
            {
                Tlas.Intersect(box, intersectFunc);
            }
            else
            {
                for (int i = 0; i < Tlas.MeshInstances.Length; i++)
                {
                    ref readonly GpuMeshInstance meshInstance = ref Tlas.MeshInstances[i];
                    BLAS blas = Tlas.Blases[meshInstance.MeshIndex];

                    Box localBox = Box.Transformed(box, meshInstance.InvModelMatrix);

                    int copyMeshIndex = meshInstance.MeshIndex;
                    blas.Intersect(localBox, (in BLAS.IndicesTriplet hitTriangle) =>
                    {
                        BoxHitInfo hitInfo;
                        hitInfo.TriangleIndices = hitTriangle;
                        hitInfo.MeshID = copyMeshIndex;
                        hitInfo.InstanceID = i;

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
        public void AddMeshes(
            // New BLASes build info
            ReadOnlySpan<BBG.DrawElementsIndirectCommand> newMeshesDrawCommands,
            Vector3[] vertexPositions,
            ReadOnlySpan<uint> vertexIndices,

            // TLAS build info
            BBG.DrawElementsIndirectCommand[] drawCommands,
            GpuMeshInstance[] meshInstances)
        {
            BLAS[] newBlases = new BLAS[newMeshesDrawCommands.Length];
            for (int i = 0; i < newBlases.Length; i++)
            {
                newBlases[i] = new BLAS(vertexPositions, vertexIndices, newMeshesDrawCommands[i]);
            }
            Helper.ArrayAdd(ref blases, newBlases);
            BlasesBuild(blases.Length - newBlases.Length, newBlases.Length);

            Stopwatch sw = Stopwatch.StartNew();
            Tlas = new TLAS(blases, drawCommands, meshInstances);
            TlasBuild(true);
            Logger.Log(Logger.LogLevel.Info, $"Build and uploaded Top Level Acceleration Structures (TLAS) for {Tlas.MeshInstances.Length} instances in {sw.ElapsedMilliseconds} milliseconds");
        }

        public void TlasBuild(bool force = false)
        {
            if (UpdateTlas || force)
            {
                Tlas.Build();
                tlasBuffer.MutableAllocateElements(Tlas.Nodes);
            }
        }

        public void BlasesBuild()
        {
            BlasesBuild(0, blases.Length);
        }

        public void BlasesBuild(int start, int count)
        {
            Stopwatch swBuilding = Stopwatch.StartNew();
            Parallel.For(start, start + count, i =>
            //for (int i = start; i < start + count; i++)
            {
                blases[i].Build();
            });
            swBuilding.Stop();

            Stopwatch swOptimization = Stopwatch.StartNew();
            Parallel.For(start, start + count, i =>
            //for (int i = start; i < start + count; i++)
            {
                blases[i].Optimize(new BLAS.OptimizationSettings());
            });
            swOptimization.Stop();

            Logger.Log(Logger.LogLevel.Info, 
                $"Created {count} BLAS'es in {swBuilding.ElapsedMilliseconds}ms(Build) + {swOptimization.ElapsedMilliseconds}ms(Optimization) = " +
                $"{swBuilding.ElapsedMilliseconds + swOptimization.ElapsedMilliseconds}ms"
            );

            if (true)
            {
                float totalSAH = 0;
                for (int i = start; i < start + count; i++)
                {
                    totalSAH += blases[i].ComputeGlobalCost(blases[i].Root);
                }
                Logger.Log(Logger.LogLevel.Info, $"Added SAH of all new BLAS'es = {totalSAH}");
            }
            SetBlasBuffersContent();

            int maxTreeDepth = MaxBlasTreeDepth;
            for (int i = start; i < start + count; i++)
            {
                maxTreeDepth = Math.Max(maxTreeDepth, blases[i].MaxTreeDepth);
            }
            MaxBlasTreeDepth = maxTreeDepth;
        }

        public void BlasesRefit(int start, int count)
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
            SetBlasBuffersContent();
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
