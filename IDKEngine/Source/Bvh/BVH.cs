using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.GpuTypes;
using IDKEngine.Shapes;
using IDKEngine.Utils;

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
            public GpuIndicesTriplet TriangleIndices;
            public Vector3 Bary;
            public float T;
            public int InstanceID;
        }

        public record struct BoxHitInfo
        {
            public GpuIndicesTriplet TriangleIndices;
            public int InstanceID;
        }

        private record struct BlasBuildPhaseData
        {
            public GpuBlasNode[] Nodes;
            public int[] ParentIds;
            public int[] LeafIds;

            // Pre-splitting may create additional Triangles
            public GpuIndicesTriplet[] Triangles;
        }

        public bool CpuUseTlas;

        private bool _rebuildTlas;
        public bool RebuildTlas
        {
            get => _rebuildTlas;

            set
            {
                _rebuildTlas = value;

                // Force update on toggle as automatic update may be supressed for when nothing moves
                if (RebuildTlas)
                {
                    TlasBuild();
                }
            }
        }

        public GpuTlasNode[] TlasNodes = [];
        public GpuBlasNode[] BlasNodes = [];
        public GpuIndicesTriplet[] BlasTriangles = [];
        public GpuBlasDesc[] BlasesDesc = [];

        private NativeMemoryView<Vector3> vertexPositions;
        private uint[] vertexIndices = [];
        private GpuMeshInstance[] meshInstances = [];

        private BBG.TypedBuffer<GpuBlasDesc> blasDescBuffer;
        private BBG.TypedBuffer<GpuBlasNode> blasNodesBuffer;
        private BBG.TypedBuffer<GpuIndicesTriplet> blasTriangleIndicesBuffer;
        private BBG.TypedBuffer<int> blasParentIdsBuffer;
        private BBG.TypedBuffer<int> blasLeafIndicesBuffer;
        private BBG.TypedBuffer<int> blasRefitLockBuffer;
        private BBG.TypedBuffer<GpuTlasNode> tlasBuffer;

        private readonly BBG.ShaderProgram refitBlasProgram;

        public BVH()
        {
            blasDescBuffer = new BBG.TypedBuffer<GpuBlasDesc>();
            blasDescBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 20);

            blasNodesBuffer = new BBG.TypedBuffer<GpuBlasNode>();
            blasNodesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 21);

            blasTriangleIndicesBuffer = new BBG.TypedBuffer<GpuIndicesTriplet>();
            blasTriangleIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 22);

            blasParentIdsBuffer = new BBG.TypedBuffer<int>();
            blasParentIdsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 23);

            blasLeafIndicesBuffer = new BBG.TypedBuffer<int>();
            blasLeafIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 24);

            blasRefitLockBuffer = new BBG.TypedBuffer<int>();
            blasRefitLockBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 25);

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
                    ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

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
                    ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

                    Box localBox = Box.Transformed(box, meshInstance.InvModelMatrix);

                    BLAS.Intersect(
                        GetBlas(blasDesc),
                        GetBlasGeometry(blasDesc.GeometryDesc),
                        localBox, (in GpuIndicesTriplet hitTriangle) =>
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
            TlasBuild(true);
        }

        public void Add(ReadOnlySpan<GpuGeometryDesc> geometriesDesc)
        {
            Array.Resize(ref BlasesDesc, BlasesDesc.Length + geometriesDesc.Length);
            Array.Resize(ref BlasTriangles, BlasTriangles.Length + geometriesDesc.Sum(it => it.TriangleCount));

            Span<GpuBlasDesc> blasesDesc = new Span<GpuBlasDesc>(BlasesDesc, BlasesDesc.Length - geometriesDesc.Length, geometriesDesc.Length);
            for (int i = 0; i < blasesDesc.Length; i++)
            {
                GpuGeometryDesc userGeometryDesc = geometriesDesc[i];

                GpuBlasDesc blasDesc = new GpuBlasDesc();
                blasDesc.GeometryDesc.VertexOffset = userGeometryDesc.VertexOffset;
                blasDesc.GeometryDesc.VertexCount = userGeometryDesc.VertexCount;

                // We don't use the user provided TriangleOffset because the BVH builder may create additional triangles on it's own.
                // Instead we compute our own offset based on the previous one, having them tighly packed
                int offset = BlasesDesc.Length - geometriesDesc.Length;
                blasDesc.GeometryDesc.TriangleOffset = (offset + i) > 0 ? BlasesDesc[offset + i - 1].GeometryDesc.TrianglesEnd : 0;

                blasDesc.GeometryDesc.TriangleCount = userGeometryDesc.TriangleCount;

                BLAS.Geometry geometry = GetBlasGeometry(blasDesc.GeometryDesc);
                for (int j = 0; j < geometry.Triangles.Length; j++)
                {
                    geometry.Triangles[j].X = (int)(userGeometryDesc.VertexOffset + vertexIndices[(userGeometryDesc.TriangleOffset + j) * 3 + 0]);
                    geometry.Triangles[j].Y = (int)(userGeometryDesc.VertexOffset + vertexIndices[(userGeometryDesc.TriangleOffset + j) * 3 + 1]);
                    geometry.Triangles[j].Z = (int)(userGeometryDesc.VertexOffset + vertexIndices[(userGeometryDesc.TriangleOffset + j) * 3 + 2]);
                }

                blasesDesc[i] = blasDesc;
            }
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
                ref GpuIndicesTriplet triIndices = ref BlasTriangles[i];
                triIndices.X -= rmVerticesRange.Count;
                triIndices.Y -= rmVerticesRange.Count;
                triIndices.Z -= rmVerticesRange.Count;
            }
            Helper.ArrayRemove(ref BlasTriangles, rmTriangleRange.Start, rmTriangleRange.Count);

            for (int i = rmBlasRange.End; i < BlasesDesc.Length; i++)
            {
                ref GpuBlasDesc desc = ref BlasesDesc[i];
                desc.RootNodeOffset -= rmNodeRange.Count;
                desc.LeafIndicesOffset -= rmLeafIdRange.Count;

                desc.GeometryDesc.TriangleOffset -= rmTriangleRange.Count;
                desc.GeometryDesc.VertexOffset -= rmVerticesRange.Count;
            }
            Helper.ArrayRemove(ref BlasesDesc, rmBlasRange.Start, rmBlasRange.Count);

            BBG.Buffer.Recreate(ref blasDescBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc);
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

                Box GetPrimitive(int primId)
                {
                    ref readonly GpuMeshInstance meshInstance = ref meshInstances[primId];

                    BLAS.BuildResult blas = GetBlas(BlasesDesc[meshInstance.MeshId]);
                    Box localBounds = Conversions.ToBox(blas.Root);
                    Box worldSpaceBounds = Box.Transformed(localBounds, meshInstance.ModelMatrix);

                    return worldSpaceBounds;
                }
            }
        }

        public record struct Statistics
        {
            public ulong BoxIntersections;
            public ulong TriIntersections;
        }

        public static Statistics DebugStatistics;

        public unsafe void BlasesBuild(int start, int count)
        {
            if (count == 0) return;

            BlasBuildPhaseData[] newBlasesData = new BlasBuildPhaseData[count];
            GpuBlasDesc prevEndBlasDesc = BlasesDesc[start + count - 1];
            
            BLAS.BuildSettings buildSettings = new BLAS.BuildSettings();
            //buildSettings.SBVH.SplitFactor = 0.0f;
            buildSettings.PreSplitting.SplitFactor = 0.0f;

            // Statistics
            int preSplitNewTris = 0;
            int sbvhNewTris = 0;
            int newTrisDeduplicated = 0;

            Stopwatch swBuilding = Stopwatch.StartNew();
            Parallel.For(0, count, i =>
            //for (int i = 0; i < count; i++)
            {
                ref GpuBlasDesc blasDesc = ref BlasesDesc[start + i];
                BLAS.Geometry geometry = GetBlasGeometry(blasDesc.GeometryDesc);

                // TODO: Only store parent+leaf ids for "refitable BLAS"

                // Don't do pre-splitting repeatedly as that creates excessive amounts of triangles
                bool doPresplitting = buildSettings.PreSplitting.Enabled && !blasDesc.PreSplittingWasDone;
                if (doPresplitting)
                {
                    blasDesc.PreSplittingWasDone = true;
                }

                // Generate fragments from triangles.
                // If pre-splitting is enabled a triangle may be referenced by multiple fragments.
                BLAS.Fragments fragments = BLAS.GetFragments(geometry, buildSettings);

                // Allocate temporary build data and upper bound of required nodes
                BLAS.BuildData buildData = BLAS.GetBuildData(fragments);
                GpuBlasNode[] nodes = new GpuBlasNode[BLAS.GetUpperBoundNodes(fragments.ReservedSpace)];

                // Build BLAS and resize nodes
                BLAS.BuildResult blas = new BLAS.BuildResult(nodes);
                int nodesUsed = BLAS.Build(ref blas, geometry, ref buildData, buildSettings);

                Array.Resize(ref nodes, nodesUsed);
                blas.Nodes = nodes;

                int[] parentIds = BLAS.GetParentIndices(blas);

                if (false)
                {
                    // Post build optimizaton.
                    // Although it still decreases SAH from a Full-Sweep BLAS it either has no or even a harmful impact on net performance
                    // It was more effective on the older binned BLAS. More investigation needed. For now it's disabled
                    ReinsertionOptimizer.Optimize(ref blas, parentIds, new ReinsertionOptimizer.Settings());
                }

                // The BLAS holds permutated indices into the inital triangles. Let's get rid of this indirection
                GpuIndicesTriplet[] blasTriangles = BLAS.GetUnindexedTriangles(blas, buildData, geometry, buildSettings);

                int[] leafIds = BLAS.GetLeafIndices(blas);

                // Statistics
                Interlocked.Add(ref preSplitNewTris, fragments.Count - geometry.TriangleCount);
                Interlocked.Add(ref newTrisDeduplicated, blasTriangles.Length - geometry.TriangleCount);
                Interlocked.Add(ref sbvhNewTris, buildData.Fragments.Count - fragments.Count);

                blasDesc.NodeCount = nodes.Length;
                blasDesc.MaxTreeDepth = blas.MaxTreeDepth;
                blasDesc.LeafIndicesCount = leafIds.Length;
                blasDesc.GeometryDesc.TriangleCount = blasTriangles.Length;

                BlasBuildPhaseData blasData = new BlasBuildPhaseData();
                blasData.Triangles = blasTriangles;
                blasData.Nodes = nodes;
                blasData.ParentIds = parentIds;
                blasData.LeafIds = leafIds;
                newBlasesData[i] = blasData;
            });
            swBuilding.Stop();

            // Adjust offsets of all BLASes starting from the the ones that were rebuild
            for (int i = start; i < BlasesDesc.Length; i++)
            {
                ref GpuBlasDesc blasDesc = ref BlasesDesc[i];
                blasDesc.RootNodeOffset = i > 0 ? BlasesDesc[i - 1].NodesEnd : 0;
                blasDesc.LeafIndicesOffset = i > 0 ? BlasesDesc[i - 1].LeafIndicesEnd : 0;

                blasDesc.GeometryDesc.TriangleOffset = i > 0 ? BlasesDesc[i - 1].GeometryDesc.TrianglesEnd : 0;
            }
            GpuBlasDesc newEndBlasDesc = BlasesDesc[start + count - 1];

            blasParentIdsBuffer.DownloadElements(out int[] blasParentIds);
            blasLeafIndicesBuffer.DownloadElements(out int[] blasLeafIds);

            // Data in the middle resized, move the following data to new offsets so there are no holes
            {
                if (newEndBlasDesc.NodesEnd < prevEndBlasDesc.NodesEnd)
                {
                    Helper.ArrayRemove(ref BlasNodes, newEndBlasDesc.NodesEnd, prevEndBlasDesc.NodesEnd - newEndBlasDesc.NodesEnd);
                    Helper.ArrayRemove(ref blasParentIds, newEndBlasDesc.NodesEnd, prevEndBlasDesc.NodesEnd - newEndBlasDesc.NodesEnd);
                }
                else
                {
                    Helper.ArrayShiftElementsResize(ref BlasNodes, prevEndBlasDesc.NodesEnd, newEndBlasDesc.NodesEnd);
                    Helper.ArrayShiftElementsResize(ref blasParentIds, prevEndBlasDesc.NodesEnd, newEndBlasDesc.NodesEnd);
                }

                if (newEndBlasDesc.GeometryDesc.TrianglesEnd < prevEndBlasDesc.GeometryDesc.TrianglesEnd)
                {
                    Helper.ArrayRemove(ref BlasTriangles, newEndBlasDesc.GeometryDesc.TrianglesEnd, prevEndBlasDesc.GeometryDesc.TrianglesEnd - newEndBlasDesc.GeometryDesc.TrianglesEnd);
                }
                else
                {
                    Helper.ArrayShiftElementsResize(ref BlasTriangles, prevEndBlasDesc.GeometryDesc.TrianglesEnd, newEndBlasDesc.GeometryDesc.TrianglesEnd);
                }

                if (newEndBlasDesc.LeafIndicesEnd < prevEndBlasDesc.LeafIndicesEnd)
                {
                    Helper.ArrayRemove(ref blasLeafIds, newEndBlasDesc.LeafIndicesEnd, prevEndBlasDesc.LeafIndicesEnd - newEndBlasDesc.LeafIndicesEnd);
                }
                else
                {
                    Helper.ArrayShiftElementsResize(ref blasLeafIds, prevEndBlasDesc.LeafIndicesEnd, newEndBlasDesc.LeafIndicesEnd);
                }
            }

            // Copy new data into global arrays
            for (int i = 0; i < count; i++)
            {
                ref readonly BlasBuildPhaseData blasData = ref newBlasesData[i];
                ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[start + i];

                Array.Copy(blasData.Nodes, 0, BlasNodes, blasDesc.RootNodeOffset, blasDesc.NodeCount);
                Array.Copy(blasData.Triangles, 0, BlasTriangles, blasDesc.GeometryDesc.TriangleOffset, blasDesc.GeometryDesc.TriangleCount);
                Array.Copy(blasData.ParentIds, 0, blasParentIds, blasDesc.RootNodeOffset, blasDesc.NodeCount);
                Array.Copy(blasData.LeafIds, 0, blasLeafIds, blasDesc.LeafIndicesOffset, blasDesc.LeafIndicesCount);
            }

            UpdateMaxBlasTreeDepth();

            BBG.Buffer.Recreate(ref blasDescBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc);
            BBG.Buffer.Recreate(ref blasTriangleIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasTriangles);
            BBG.Buffer.Recreate(ref blasNodesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasNodes);
            BBG.Buffer.Recreate(ref blasParentIdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasParentIds);
            BBG.Buffer.Recreate(ref blasLeafIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasLeafIds);
            BBG.Buffer.Recreate(ref blasRefitLockBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc.Select(it => it.NodeCount).DefaultIfEmpty(0).Max());

            Logger.Log(Logger.LogLevel.Info, $"Created {count} BLAS'es in {swBuilding.ElapsedMilliseconds}ms");

            if (preSplitNewTris > 0 || sbvhNewTris > 0)
            {
                Logger.Log(Logger.LogLevel.Info, $"Added triangles {preSplitNewTris} + {sbvhNewTris} => {newTrisDeduplicated} (Pre-Splitting, SBVH, after deduplication)");
            }

            if (true)
            {
                float totalSAH = 0;
                for (int i = 0; i < count; i++)
                {
                    totalSAH += BLAS.ComputeGlobalSAH(GetBlas(i), buildSettings);
                }
                Logger.Log(Logger.LogLevel.Info, $"Added SAH of all new BLAS'es = {totalSAH}");
                Logger.Log(Logger.LogLevel.Info, $"Added Area of all new BLAS'es = {BLAS.ComputeGlobalArea(GetBlas(0), buildSettings)}");
                Logger.Log(Logger.LogLevel.Info, $"Added EPO of all new BLAS'es = {BLAS.ComputeGlobalEPO(GetBlas(0), GetBlasGeometry(BlasesDesc[0].GeometryDesc), buildSettings)}");
                Logger.Log(Logger.LogLevel.Info, $"Added Overlap of all new BLAS'es = {BLAS.ComputeOverlap(GetBlas(0))}");
            }

        }

        public void GpuBlasesRefit(int start, int count)
        {
            for (int i = start; i < start + count; i++)
            {
                GpuBlasDesc blasDesc = BlasesDesc[i];

                blasRefitLockBuffer.Fill(0, blasRefitLockBuffer.Size, 0);
                BBG.Computing.Compute("Refit BLAS", () =>
                {
                    refitBlasProgram.Upload(0, (uint)i);

                    BBG.Cmd.UseShaderProgram(refitBlasProgram);
                    BBG.Computing.Dispatch(MyMath.DivUp(blasDesc.LeafIndicesCount, 64), 1, 1);
                });
            }

            BBG.Cmd.MemoryBarrier(BBG.Cmd.MemoryBarrierMask.ShaderStorageBarrierBit);
        }

        public void CpuBlasRefit(in GpuBlasDesc blasDesc)
        {
            BLAS.BuildResult blas = GetBlas(blasDesc);
            BLAS.Refit(blas, GetBlasGeometry(blasDesc.GeometryDesc));
        }

        public BLAS.BuildResult GetBlas(int index)
        {
            return GetBlas(BlasesDesc[index]);
        }

        public BLAS.BuildResult GetBlas(in GpuBlasDesc blasDesc)
        {
            BLAS.BuildResult blas = new BLAS.BuildResult();
            blas.Nodes = new Span<GpuBlasNode>(BlasNodes, blasDesc.RootNodeOffset, blasDesc.NodeCount);
            blas.MaxTreeDepth = blasDesc.MaxTreeDepth;

            return blas;
        }

        public BLAS.Geometry GetBlasGeometry(in GpuGeometryDesc geometryDesc)
        {
            BLAS.Geometry geometry = new BLAS.Geometry();
            geometry.VertexPositions = vertexPositions;
            geometry.Triangles = new Span<GpuIndicesTriplet>(BlasTriangles, geometryDesc.TriangleOffset, geometryDesc.TriangleCount);
            return geometry;
        }

        public Range GetBlasesNodeRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].RootNodeOffset;
            range.End = BlasesDesc[blases.End - 1].NodesEnd;

            return range;
        }

        public Range GetBlasesLeafIdsRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].LeafIndicesOffset;
            range.End = BlasesDesc[blases.End - 1].LeafIndicesEnd;

            return range;
        }

        public Range GetBlasesTriangleRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].GeometryDesc.TriangleOffset;
            range.End = BlasesDesc[blases.End - 1].GeometryDesc.TrianglesEnd;

            return range;
        }

        public Range GetBlasesVerticesRange(Range blases)
        {
            Range range = new Range();
            range.Start = BlasesDesc[blases.Start].GeometryDesc.VertexOffset;
            range.End = BlasesDesc[blases.End - 1].GeometryDesc.VerticesEnd;

            return range;
        }

        private void GetBlasAndGeometry(int instanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out Matrix4 invWorldTransform)
        {
            ref readonly GpuMeshInstance meshInstance = ref meshInstances[instanceId];
            ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[meshInstance.MeshId];

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
            blasDescBuffer.Dispose();
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
