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

        public record struct RayHitInfo
        {
            public BLAS.IndicesTriplet TriangleIndices;
            public Vector3 Bary;
            public float T;
            public int InstanceID;
        }

        public record struct BoxHitInfo
        {
            public BLAS.IndicesTriplet TriangleIndices;
            public int InstanceID;
        }

        public record struct BlasDesc
        {
            public GeometryDesc GeometryDesc;

            public int RootNodeOffset;
            public int NodeCount;
            public int LeafIndicesOffset;
            public int LeafIndicesCount;

            public int MaxTreeDepth;
            public int UnpaddedNodesCount;

            public int NodesEnd => RootNodeOffset + NodeCount;
            public int LeafIndicesEnd => LeafIndicesOffset + LeafIndicesCount;
        }

        private record struct BlasBuildPhaseData
        {
            public ref GpuBlasNode Root => ref Nodes[0];

            public GpuBlasNode[] Nodes;
            public int[] ParentIds;
            public int[] LeafIds;
        }

        private record struct BlasRefittedBounds
        {
            public Vector3 Min;
            public Vector3 Max;
        }

        public record struct GeometryDesc
        {
            public int TriangleCount;
            public int TriangleOffset;
            public int BaseVertex;
            public int VertexOffset;
        }

        public bool CpuUseTlas;
        public bool RebuildTlas;

        public BlasDesc[] BlasesDesc = Array.Empty<BlasDesc>();

        public GpuBlasNode[] BlasNodes = Array.Empty<GpuBlasNode>();
        public BLAS.IndicesTriplet[] BlasTriangles = Array.Empty<BLAS.IndicesTriplet>();

        public GpuTlasNode[] TlasNodes = Array.Empty<GpuTlasNode>();

        private Vector3[] vertexPositions;
        private uint[] vertexIndices;
        private GpuMeshInstance[] meshInstances;

        private BBG.TypedBuffer<GpuBlasNode> blasNodesBuffer;
        private BBG.TypedBuffer<BLAS.IndicesTriplet> blasTriangleIndicesBuffer;
        private BBG.TypedBuffer<int> blasParentIdsBuffer;
        private BBG.TypedBuffer<int> blasLeafIndicesBuffer;
        private BBG.TypedBuffer<int> blasRefitLockBuffer;
        private BBG.TypedBuffer<GpuTlasNode> tlasBuffer;

        private readonly BBG.ShaderProgram refitBlasProgram;

        public BVH()
        {
            blasNodesBuffer = new BBG.TypedBuffer<GpuBlasNode>();
            blasNodesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 20);

            blasTriangleIndicesBuffer = new BBG.TypedBuffer<BLAS.IndicesTriplet>();
            blasTriangleIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 21);

            blasParentIdsBuffer = new BBG.TypedBuffer<int>();
            blasParentIdsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 22);

            blasLeafIndicesBuffer = new BBG.TypedBuffer<int>();
            blasLeafIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 23);

            blasRefitLockBuffer = new BBG.TypedBuffer<int>();
            blasRefitLockBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 24);

            tlasBuffer = new BBG.TypedBuffer<GpuTlasNode>();
            tlasBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 26);

            BlasTriangles = Array.Empty<BLAS.IndicesTriplet>();

            refitBlasProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "BLASRefit/compute.glsl"));

            GpuUseTlas = false; // Only pays of on scenes with many Blas'es (not sponza). So disabled by default.
            CpuUseTlas = false;
            RebuildTlas = false;
            MaxBlasTreeDepth = 1;
        }

        public bool Intersect(in Ray ray, out RayHitInfo hitInfo, float tMax = float.MaxValue)
        {
            if (CpuUseTlas)
            {
                return TLAS.Intersect(TlasNodes, GetBlasAndGeometry, ray, out hitInfo, tMax);
            }
            else
            {
                hitInfo = new RayHitInfo();
                hitInfo.T = tMax;

                for (int i = 0; i < meshInstances.Length; i++)
                {
                    ref readonly GpuMeshInstance meshInstance = ref meshInstances[i];
                    ref readonly BlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

                    Ray localRay = ray.Transformed(meshInstance.InvModelMatrix);
                    if (BLAS.Intersect(
                        GetBlas(blasDesc),
                        GetBlasGeometry(blasDesc.GeometryDesc),
                        localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                    {
                        hitInfo.TriangleIndices = blasHitInfo.TriangleIndices;
                        hitInfo.Bary = blasHitInfo.Bary;
                        hitInfo.T = blasHitInfo.T;
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
                TLAS.Intersect(TlasNodes, GetBlasAndGeometry, box, intersectFunc);
            }
            else
            {
                for (int i = 0; i < meshInstances.Length; i++)
                {
                    ref readonly GpuMeshInstance meshInstance = ref meshInstances[i];
                    ref readonly BlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

                    Box localBox = Box.Transformed(box, meshInstance.InvModelMatrix);

                    int copyMeshIndex = meshInstance.MeshId;
                    BLAS.Intersect(
                        GetBlas(blasDesc),
                        GetBlasGeometry(blasDesc.GeometryDesc),
                        localBox, (in BLAS.IndicesTriplet hitTriangle) =>
                    {
                        BoxHitInfo hitInfo;
                        hitInfo.TriangleIndices = hitTriangle;
                        hitInfo.InstanceID = i;

                        intersectFunc(hitInfo);
                    });
                }
            }
        }

        /// <summary>
        /// Builds new Blases from the associated mesh data, updates the TLAS for them and updates the corresponding GPU buffers.
        /// Building Blases is expected to be slow. The process is done in parallel so prefer to pass multiple meshes at once to.
        /// </summary>
        public void AddMeshes(ReadOnlySpan<GeometryDesc> geometriesDesc, Vector3[] vertexPositions, uint[] vertexIndices, GpuMeshInstance[] meshInstances)
        {
            this.vertexPositions = vertexPositions;
            this.vertexIndices = vertexIndices;
            this.meshInstances = meshInstances;

            Array.Resize(ref BlasesDesc, BlasesDesc.Length + geometriesDesc.Length);
            Span<BlasDesc> blasesDesc = new Span<BlasDesc>(BlasesDesc, BlasesDesc.Length - geometriesDesc.Length, geometriesDesc.Length);
            for (int i = 0; i < geometriesDesc.Length; i++)
            {
                BlasDesc blasDesc = new BlasDesc();
                blasDesc.GeometryDesc = geometriesDesc[i];

                blasesDesc[i] = blasDesc;
            }

            Array.Resize(ref BlasTriangles, BlasTriangles.Length + Helper.Sum(blasesDesc, it => it.GeometryDesc.TriangleCount));
            BlasesBuild(BlasesDesc.Length - geometriesDesc.Length, geometriesDesc.Length);

            Stopwatch sw = Stopwatch.StartNew();
            TlasNodes = TLAS.AllocateRequiredNodes(meshInstances.Length);
            TlasBuild(true);
            Logger.Log(Logger.LogLevel.Info, $"Build and uploaded Top Level Acceleration Structures (TLAS) for {meshInstances.Length} instances in {sw.ElapsedMilliseconds} milliseconds");
        }

        public void TlasBuild(bool force = false)
        {
            if (RebuildTlas || force)
            {
                TLAS.Build(TlasNodes, GetPrimitive, meshInstances.Length, new TLAS.BuildSettings());
                BBG.Buffer.Recreate(ref tlasBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, TlasNodes);

                void GetPrimitive(int primId, out BLAS.BuildResult blas, out Matrix4 worldTransform)
                {
                    ref readonly GpuMeshInstance meshInstance = ref meshInstances[primId];
                    blas = GetBlas(BlasesDesc[meshInstance.MeshId]);
                    worldTransform = meshInstance.ModelMatrix;
                }
            }
        }

        public unsafe void BlasesBuild(int start, int count)
        {
            if (count == 0) return;

            BLAS.BuildSettings buildSettings = new BLAS.BuildSettings();
            ReinsertionOptimizer.Settings optimizationSettings = new ReinsertionOptimizer.Settings();
            BlasBuildPhaseData[] newBlasesData = new BlasBuildPhaseData[count];

            Stopwatch swBuilding = Stopwatch.StartNew();
            Parallel.For(0, count, i =>
            //for (int i = start; i < start + count; i++)
            {
                ref BlasDesc blasDesc = ref BlasesDesc[start + i];
                BLAS.Geometry geometry = GetBlasGeometry(blasDesc.GeometryDesc);

                for (int j = 0; j < geometry.Triangles.Length; j++)
                {
                    geometry.Triangles[j].X = (int)(blasDesc.GeometryDesc.BaseVertex + vertexIndices[(blasDesc.GeometryDesc.TriangleOffset + j) * 3 + 0]);
                    geometry.Triangles[j].Y = (int)(blasDesc.GeometryDesc.BaseVertex + vertexIndices[(blasDesc.GeometryDesc.TriangleOffset + j) * 3 + 1]);
                    geometry.Triangles[j].Z = (int)(blasDesc.GeometryDesc.BaseVertex + vertexIndices[(blasDesc.GeometryDesc.TriangleOffset + j) * 3 + 2]);
                }

                GpuBlasNode[] nodes = BLAS.AllocateUpperBoundNodes(geometry.TriangleCount);
                BLAS.BuildResult blasBuildResult = new BLAS.BuildResult(nodes);

                int nodesUsed = BLAS.Build(ref blasBuildResult, geometry, buildSettings);
                blasDesc.NodeCount = nodesUsed;
                blasDesc.MaxTreeDepth = blasBuildResult.MaxTreeDepth;
                blasDesc.UnpaddedNodesCount = blasBuildResult.UnpaddedNodesCount;

                // Resize array and update the Span because array got mutated
                Array.Resize(ref nodes, blasDesc.NodeCount);
                blasBuildResult.Nodes = nodes;

                int[] parentIds = BLAS.GetParentIndices(blasBuildResult);
                ReinsertionOptimizer.Optimize(blasBuildResult.Nodes, parentIds, geometry.Triangles, optimizationSettings);

                int[] leafIds = BLAS.GetLeafIndices(blasBuildResult);
                blasDesc.LeafIndicesCount = leafIds.Length;

                BlasBuildPhaseData blasData = new BlasBuildPhaseData();
                blasData.Nodes = nodes;
                blasData.ParentIds = parentIds;
                blasData.LeafIds = leafIds;
                newBlasesData[i] = blasData;
            });
            swBuilding.Stop();

            Logger.Log(Logger.LogLevel.Info, $"Created {count} BLAS'es in {swBuilding.ElapsedMilliseconds}ms(Build) {swBuilding.ElapsedMilliseconds}ms");

            if (true)
            {
                float totalSAH = 0;
                for (int i = 0; i < count; i++)
                {
                    totalSAH += BLAS.ComputeGlobalCost(newBlasesData[i].Root, newBlasesData[i].Nodes, buildSettings);
                }
                Logger.Log(Logger.LogLevel.Info, $"Added SAH of all new BLAS'es = {totalSAH}");
            }

            BlasDesc prevLastBlasDesc = BlasesDesc[start + count - 1];
            for (int i = start; i < BlasesDesc.Length; i++)
            {
                ref BlasDesc blasDesc = ref BlasesDesc[i];
                blasDesc.RootNodeOffset = i == 0 ? 0 : BlasesDesc[i - 1].NodesEnd;
                blasDesc.LeafIndicesOffset = i == 0 ? 0 : BlasesDesc[i - 1].LeafIndicesEnd;
            }
            BlasDesc newLastBlasDesc = BlasesDesc[start + count - 1];

            blasParentIdsBuffer.DownloadElements(out int[] blasParentIds);
            blasLeafIndicesBuffer.DownloadElements(out int[] blasLeafIds);
            
            // Resize arrays to hold new data
            Array.Resize(ref BlasNodes, BlasesDesc[BlasesDesc.Length - 1].NodesEnd);
            Array.Resize(ref blasParentIds, BlasesDesc[BlasesDesc.Length - 1].NodesEnd);
            Array.Resize(ref blasLeafIds, BlasesDesc[BlasesDesc.Length - 1].LeafIndicesEnd);

            // Data in the middle resized, move the following data to new offsets so there are no holes
            Array.Copy(BlasNodes, prevLastBlasDesc.NodesEnd, BlasNodes, newLastBlasDesc.NodesEnd, BlasNodes.Length - newLastBlasDesc.NodesEnd);
            Array.Copy(blasParentIds, prevLastBlasDesc.NodesEnd, blasParentIds, newLastBlasDesc.NodesEnd, blasParentIds.Length - newLastBlasDesc.NodesEnd);
            Array.Copy(blasLeafIds, prevLastBlasDesc.LeafIndicesEnd, blasLeafIds, newLastBlasDesc.LeafIndicesEnd, blasLeafIds.Length - newLastBlasDesc.LeafIndicesEnd);

            // Copy new data into global arrays
            for (int i = 0; i < count; i++)
            {
                ref readonly BlasBuildPhaseData blas = ref newBlasesData[i];
                ref readonly BlasDesc blasDesc = ref BlasesDesc[start + i];

                Array.Copy(blas.Nodes, 0, BlasNodes, blasDesc.RootNodeOffset, blasDesc.NodeCount);
                Array.Copy(blas.ParentIds, 0, blasParentIds, blasDesc.RootNodeOffset, blasDesc.NodeCount);
                Array.Copy(blas.LeafIds, 0, blasLeafIds, blasDesc.LeafIndicesOffset, blasDesc.LeafIndicesCount);
            }

            int maxTreeDepth = MaxBlasTreeDepth;
            for (int i = start; i < start + count; i++)
            {
                maxTreeDepth = Math.Max(maxTreeDepth, BlasesDesc[i].MaxTreeDepth);
            }
            MaxBlasTreeDepth = maxTreeDepth;

            BBG.Buffer.Recreate(ref blasNodesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasNodes);
            BBG.Buffer.Recreate(ref blasTriangleIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasTriangles);
            BBG.Buffer.Recreate(ref blasParentIdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasParentIds);
            BBG.Buffer.Recreate(ref blasLeafIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasLeafIds);
            BBG.Buffer.Recreate(ref blasRefitLockBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc.Max(it => it.NodeCount));
        }

        public void GpuBlasesRefit(int start, int count)
        {
            for (int i = start; i < start + count; i++)
            {
                BlasDesc blasDesc = BlasesDesc[i];

                blasRefitLockBuffer.Clear(0, blasRefitLockBuffer.Size, 0);
                BBG.Computing.Compute("Refit BLAS", () =>
                {
                    refitBlasProgram.Upload(0, (uint)blasDesc.RootNodeOffset);
                    refitBlasProgram.Upload(1, (uint)blasDesc.GeometryDesc.TriangleOffset);
                    refitBlasProgram.Upload(2, (uint)blasDesc.LeafIndicesOffset);
                    refitBlasProgram.Upload(3, (uint)blasDesc.LeafIndicesCount);

                    BBG.Cmd.UseShaderProgram(refitBlasProgram);
                    BBG.Computing.Dispatch((blasDesc.LeafIndicesCount + 64 - 1) / 64, 1, 1);
                });
            }

            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);
        }

        public void CpuBlasRefit(in BlasDesc blasDesc)
        {
            BLAS.BuildResult blas = GetBlas(blasDesc);
            BLAS.Refit(blas, GetBlasGeometry(blasDesc.GeometryDesc));
        }

        public BLAS.BuildResult GetBlas(int index)
        {
            return GetBlas(BlasesDesc[index]);
        }

        public BLAS.BuildResult GetBlas(in BlasDesc blasDesc)
        {
            BLAS.BuildResult blasBuildResult = new BLAS.BuildResult();
            blasBuildResult.Nodes = new Span<GpuBlasNode>(BlasNodes, blasDesc.RootNodeOffset, blasDesc.NodeCount);
            blasBuildResult.MaxTreeDepth = blasDesc.MaxTreeDepth;
            blasBuildResult.UnpaddedNodesCount = blasDesc.UnpaddedNodesCount;

            return blasBuildResult;
        }
        
        public BLAS.Geometry GetBlasGeometry(in GeometryDesc geometryDesc)
        {
            BLAS.Geometry geometry = new BLAS.Geometry();
            geometry.VertexPositions = vertexPositions;
            geometry.Triangles = new Span<BLAS.IndicesTriplet>(BlasTriangles, geometryDesc.TriangleOffset, geometryDesc.TriangleCount);
            return geometry;
        }

        private void GetBlasAndGeometry(int instanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out Matrix4 invWorldTransform)
        {
            ref readonly GpuMeshInstance meshInstance = ref meshInstances[instanceId];
            ref readonly BlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

            blas = GetBlas(blasDesc);
            geometry = GetBlasGeometry(blasDesc.GeometryDesc);

            invWorldTransform = meshInstance.InvModelMatrix;
        }

        public void Dispose()
        {
            refitBlasProgram.Dispose();
            tlasBuffer.Dispose();
            blasRefitLockBuffer.Dispose();
            blasParentIdsBuffer.Dispose();
            blasLeafIndicesBuffer.Dispose();
            blasTriangleIndicesBuffer.Dispose();
            blasNodesBuffer.Dispose();
        }
    }
}
