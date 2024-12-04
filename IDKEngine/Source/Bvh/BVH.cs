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
                if (MaxBlasTreeDepth == value)
                {
                    return;
                }

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
            public int VertexOffset;
            public int VertexCount;
        }

        public bool CpuUseTlas;

        private bool _rebuildTlas;
        public bool RebuildTlas
        {
            get => _rebuildTlas;

            set
            {
                _rebuildTlas = value;

                if (RebuildTlas)
                {
                    TlasBuild();
                }
            }
        }

        public GpuTlasNode[] TlasNodes = [];
        public GpuBlasNode[] BlasNodes = [];
        public BLAS.IndicesTriplet[] BlasTriangles = [];
        public BlasDesc[] BlasesDesc = [];

        private NativeMemoryView<Vector3> vertexPositions;
        private uint[] vertexIndices = [];
        private GpuMeshInstance[] meshInstances = [];

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

        public delegate bool FuncIntersectLeafNode(in BoxHitInfo hitInfo);
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

                    BLAS.Intersect(
                        GetBlas(blasDesc),
                        GetBlasGeometry(blasDesc.GeometryDesc),
                        localBox, (in BLAS.IndicesTriplet hitTriangle) =>
                        {
                            BoxHitInfo hitInfo;
                            hitInfo.TriangleIndices = hitTriangle;
                            hitInfo.InstanceID = i;

                            return intersectFunc(hitInfo);
                        });
                }
            }
        }

        public void SetSourceGeometry(NativeMemoryView<Vector3> vertexPositions, uint[] vertexIndices)
        {
            this.vertexPositions = vertexPositions;
            this.vertexIndices = vertexIndices;
        }

        public void SetSourceInstances(GpuMeshInstance[] meshInstances)
        {
            this.meshInstances = meshInstances;
            TlasNodes = TLAS.AllocateRequiredNodes(meshInstances.Length);
        }

        public void Add(ReadOnlySpan<GeometryDesc> geometriesDesc)
        {
            Array.Resize(ref BlasesDesc, BlasesDesc.Length + geometriesDesc.Length);
            Span<BlasDesc> blasesDesc = new Span<BlasDesc>(BlasesDesc, BlasesDesc.Length - geometriesDesc.Length, geometriesDesc.Length);
            for (int i = 0; i < geometriesDesc.Length; i++)
            {
                BlasDesc blasDesc = new BlasDesc();
                blasDesc.GeometryDesc = geometriesDesc[i];

                blasesDesc[i] = blasDesc;
            }

            Array.Resize(ref BlasTriangles, Helper.Sum<BlasDesc>(BlasesDesc, it => it.GeometryDesc.TriangleCount));
        }

        public void RemoveBlas(Range rmBlasRange, NativeMemoryView<Vector3> vertexPositions, uint[] vertexIndices, GpuMeshInstance[] meshInstances)
        {
            blasParentIdsBuffer.DownloadElements(out int[] blasParentIds);
            blasLeafIndicesBuffer.DownloadElements(out int[] blasLeafIds);

            Range rmNodeRange = GetBlasesNodeRange(rmBlasRange);
            Helper.ArrayRemove(ref BlasNodes, rmNodeRange.Start, rmNodeRange.Count);
            Helper.ArrayRemove(ref blasParentIds, rmNodeRange.Start, rmNodeRange.Count);

            Range rmLeafIdRange = GetBlasesLeafIdsRange(rmBlasRange);
            Helper.ArrayRemove(ref blasLeafIds, rmLeafIdRange.Start, rmLeafIdRange.Count);

            Range rmTriangleRange = GetBlasesTriangleRange(rmBlasRange);
            Range rmVerticesRange = GetBlasesVerticesRange(rmBlasRange);
            for (int i = rmTriangleRange.End; i < BlasTriangles.Length; i++)
            {
                ref BLAS.IndicesTriplet triangles = ref BlasTriangles[i];
                triangles.X -= rmVerticesRange.Count;
                triangles.Y -= rmVerticesRange.Count;
                triangles.Z -= rmVerticesRange.Count;
            }
            Helper.ArrayRemove(ref BlasTriangles, rmTriangleRange.Start, rmTriangleRange.Count);

            for (int i = rmBlasRange.End; i < BlasesDesc.Length; i++)
            {
                ref BlasDesc desc = ref BlasesDesc[i];
                desc.RootNodeOffset -= rmNodeRange.Count;
                desc.LeafIndicesOffset -= rmLeafIdRange.Count;

                desc.GeometryDesc.TriangleOffset -= rmTriangleRange.Count;
                desc.GeometryDesc.VertexOffset -= rmVerticesRange.Count;
            }
            Helper.ArrayRemove(ref BlasesDesc, rmBlasRange.Start, rmBlasRange.Count);

            BBG.Buffer.Recreate(ref blasNodesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasNodes);
            BBG.Buffer.Recreate(ref blasTriangleIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasTriangles);
            BBG.Buffer.Recreate(ref blasParentIdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasParentIds);
            BBG.Buffer.Recreate(ref blasLeafIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasLeafIds);
            BBG.Buffer.Recreate(ref blasRefitLockBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc.Select(it => it.NodeCount).DefaultIfEmpty(0).Max());

            SetSourceGeometry(vertexPositions, vertexIndices);
            SetSourceInstances(meshInstances);

            UpdateMaxBlasTreeDepth();
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
            //for (int i = 0; i < count; i++)
            {
                ref BlasDesc blasDesc = ref BlasesDesc[start + i];
                BLAS.Geometry geometry = GetBlasGeometry(blasDesc.GeometryDesc);

                for (int j = 0; j < geometry.Triangles.Length; j++)
                {
                    geometry.Triangles[j].X = (int)(blasDesc.GeometryDesc.VertexOffset + vertexIndices[(blasDesc.GeometryDesc.TriangleOffset + j) * 3 + 0]);
                    geometry.Triangles[j].Y = (int)(blasDesc.GeometryDesc.VertexOffset + vertexIndices[(blasDesc.GeometryDesc.TriangleOffset + j) * 3 + 1]);
                    geometry.Triangles[j].Z = (int)(blasDesc.GeometryDesc.VertexOffset + vertexIndices[(blasDesc.GeometryDesc.TriangleOffset + j) * 3 + 2]);
                }

                GpuBlasNode[] nodes = BLAS.AllocateUpperBoundNodes(geometry.TriangleCount);
                BLAS.BuildResult blas = new BLAS.BuildResult(nodes);

                int nodesUsed = BLAS.Build(ref blas, geometry, buildSettings);
                blasDesc.NodeCount = nodesUsed;
                blasDesc.UnpaddedNodesCount = blas.UnpaddedNodesCount;

                // Resize array and update the Span because array got mutated
                Array.Resize(ref nodes, blasDesc.NodeCount);
                blas.Nodes = nodes;

                int[] parentIds = BLAS.GetParentIndices(blas);
                ReinsertionOptimizer.Optimize(ref blas, parentIds, geometry.Triangles, optimizationSettings);

                blasDesc.MaxTreeDepth = blas.MaxTreeDepth;

                int[] leafIds = BLAS.GetLeafIndices(blas);
                blasDesc.LeafIndicesCount = leafIds.Length;

                BlasBuildPhaseData blasData = new BlasBuildPhaseData();
                blasData.Nodes = nodes;

                // TODO: Add concept of refitable BLAS and only store parent and leaf ids for those
                blasData.ParentIds = parentIds;
                blasData.LeafIds = leafIds;
                newBlasesData[i] = blasData;
            });
            swBuilding.Stop();

            Logger.Log(Logger.LogLevel.Info, $"Created {count} BLAS'es in {swBuilding.ElapsedMilliseconds}ms");

            if (true)
            {
                float totalSAH = 0;
                for (int i = 0; i < count; i++)
                {
                    totalSAH += BLAS.ComputeGlobalCost(newBlasesData[i].Root, newBlasesData[i].Nodes, buildSettings);
                }
                Logger.Log(Logger.LogLevel.Info, $"Added SAH of all new BLAS'es = {totalSAH}");
            }

            // Adjust offsets of all BLASes starting from the the ones that were rebuild
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
                ref readonly BlasBuildPhaseData blasData = ref newBlasesData[i];
                ref readonly BlasDesc blasDesc = ref BlasesDesc[start + i];

                Array.Copy(blasData.Nodes, 0, BlasNodes, blasDesc.RootNodeOffset, blasDesc.NodeCount);
                Array.Copy(blasData.ParentIds, 0, blasParentIds, blasDesc.RootNodeOffset, blasDesc.NodeCount);
                Array.Copy(blasData.LeafIds, 0, blasLeafIds, blasDesc.LeafIndicesOffset, blasDesc.LeafIndicesCount);
            }

            UpdateMaxBlasTreeDepth();

            BBG.Buffer.Recreate(ref blasTriangleIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasTriangles);
            BBG.Buffer.Recreate(ref blasNodesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasNodes);
            BBG.Buffer.Recreate(ref blasParentIdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasParentIds);
            BBG.Buffer.Recreate(ref blasLeafIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasLeafIds);
            BBG.Buffer.Recreate(ref blasRefitLockBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc.Select(it => it.NodeCount).DefaultIfEmpty(0).Max());
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
            BLAS.BuildResult blas = new BLAS.BuildResult();
            blas.Nodes = new Span<GpuBlasNode>(BlasNodes, blasDesc.RootNodeOffset, blasDesc.NodeCount);
            blas.MaxTreeDepth = blasDesc.MaxTreeDepth;
            blas.UnpaddedNodesCount = blasDesc.UnpaddedNodesCount;

            return blas;
        }

        public BLAS.Geometry GetBlasGeometry(in GeometryDesc geometryDesc)
        {
            BLAS.Geometry geometry = new BLAS.Geometry();
            geometry.VertexPositions = vertexPositions;
            geometry.Triangles = new Span<BLAS.IndicesTriplet>(BlasTriangles, geometryDesc.TriangleOffset, geometryDesc.TriangleCount);
            return geometry;
        }

        public Range GetBlasesNodeRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].RootNodeOffset;
            range.End = BlasesDesc[blases.End - 1].RootNodeOffset + BlasesDesc[blases.End - 1].NodeCount;

            return range;
        }

        public Range GetBlasesLeafIdsRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].LeafIndicesOffset;
            range.End = BlasesDesc[blases.End - 1].LeafIndicesOffset + BlasesDesc[blases.End - 1].LeafIndicesCount;

            return range;
        }

        public Range GetBlasesTriangleRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].GeometryDesc.TriangleOffset;
            range.End = BlasesDesc[blases.End - 1].GeometryDesc.TriangleOffset + BlasesDesc[blases.End - 1].GeometryDesc.TriangleCount;

            return range;
        }

        public Range GetBlasesVerticesRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].GeometryDesc.VertexOffset;
            range.End = BlasesDesc[blases.End - 1].GeometryDesc.VertexOffset + BlasesDesc[blases.End - 1].GeometryDesc.VertexCount;

            return range;
        }

        private void GetBlasAndGeometry(int instanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out Matrix4 invWorldTransform)
        {
            ref readonly GpuMeshInstance meshInstance = ref meshInstances[instanceId];
            ref readonly BlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

            blas = GetBlas(blasDesc);
            geometry = GetBlasGeometry(blasDesc.GeometryDesc);

            invWorldTransform = meshInstance.InvModelMatrix;
        }

        private void UpdateMaxBlasTreeDepth()
        {
            int maxTreeDepth = 0;
            for (int i = 0; i < BlasesDesc.Length; i++)
            {
                maxTreeDepth = Math.Max(maxTreeDepth, BlasesDesc[i].MaxTreeDepth);
            }
            MaxBlasTreeDepth = maxTreeDepth;
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
