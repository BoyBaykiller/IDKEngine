using System;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using OpenTK.Mathematics;
using BBLogger;
using BBOpenGL;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh;

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

    private int _blasStackSize;
    public int BlasStackSize
    {
        get => _blasStackSize;

        set
        {
            if (BlasStackSize == value)
            {
                return;
            }

            _blasStackSize = value;

            BBG.AbstractShaderProgram.SetShaderInsertionValue("BLAS_STACK_SIZE", BlasStackSize);
        }
    }

    public record struct RayHitInfo
    {
        public Vector3 Bary;
        public float T;
        public int TriangleId;
        public int BlasInstanceId;
    }

    public record struct BoxHitInfo
    {
        public int TriangleId;
        public int BlasInstanceId;
    }

    public record struct BlasBuildDesc
    {
        public record struct Geometry
        {
            public int TriangleOffset;
            public int TriangleCount;
            public int VertexOffset;
            public int MeshId;
        }

        public Geometry[] Geometries;
        public bool IsRefittable;
    }

    public record struct Statistics
    {
        public ulong BoxIntersections;
        public ulong TriIntersections;
    }

    private record struct BlasBuildPhaseData
    {
        public GpuBlasNode[] Nodes;
        public int[] ParentIds;
        public int[] LeafIds;

        // PreSplitting may create additional Triangles
        public GpuBlasTriangle[] Triangles;
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

    // Owning
    public GpuTlasNode[] TlasNodes = [];
    public GpuBlasNode[] BlasNodes = [];
    public GpuBlasTriangle[] BlasTriangles = [];
    public GpuBlasDesc[] BlasesDesc = [];
    public GpuBlasInstance[] BlasInstances = [];

    // Non owning
    private NativeMemoryView<Vector3> vertexPositions;
    private uint[] vertexIndices = [];
    private GpuMeshTransform[] meshTransforms = [];

    private BBG.TypedBuffer<GpuBlasDesc> blasDescBuffer;
    private BBG.TypedBuffer<GpuBlasInstance> blasInstanceBuffer;
    private BBG.TypedBuffer<GpuBlasNode> blasNodesBuffer;
    private BBG.TypedBuffer<GpuBlasTriangle> blasTriangleIndicesBuffer;
    private BBG.TypedBuffer<int> blasParentIdsBuffer;
    private BBG.TypedBuffer<int> blasLeafIndicesBuffer;
    private BBG.TypedBuffer<int> blasRefitLockBuffer;
    private BBG.TypedBuffer<GpuTlasNode> tlasBuffer;

    private readonly BBG.ShaderProgram refitBlasProgram;
    public static Statistics DebugStatistics;

    public BVH()
    {
        blasDescBuffer = new BBG.TypedBuffer<GpuBlasDesc>();
        blasInstanceBuffer = new BBG.TypedBuffer<GpuBlasInstance>();
        blasNodesBuffer = new BBG.TypedBuffer<GpuBlasNode>();
        blasTriangleIndicesBuffer = new BBG.TypedBuffer<GpuBlasTriangle>();
        blasParentIdsBuffer = new BBG.TypedBuffer<int>();
        blasLeafIndicesBuffer = new BBG.TypedBuffer<int>();
        blasRefitLockBuffer = new BBG.TypedBuffer<int>();
        tlasBuffer = new BBG.TypedBuffer<GpuTlasNode>();

        blasDescBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 20);
        blasInstanceBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 21);
        blasNodesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 22);
        blasTriangleIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 23);
        blasParentIdsBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 24);
        blasLeafIndicesBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 25);
        blasRefitLockBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 26);
        tlasBuffer.BindToBufferBackedBlock(BBG.Buffer.BufferBackedBlockTarget.ShaderStorage, 27);

        refitBlasProgram = new BBG.AbstractShaderProgram(BBG.AbstractShader.FromFile(BBG.ShaderStage.Compute, "BLASRefit/compute.glsl"));

        GpuUseTlas = false; // Only pays of on scenes with many Blas'es (not sponza). So disabled by default.
        CpuUseTlas = false;
        RebuildTlas = false;
        BlasStackSize = 1;
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

            for (int i = 0; i < BlasInstances.Length; i++)
            {
                GpuBlasInstance blasInstance = BlasInstances[i];
                ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[blasInstance.BlasId];
                ref readonly GpuMeshTransform meshTransform = ref meshTransforms[blasInstance.MeshTransformId];

                Ray localRay = ray.Transformed(meshTransform.InvModelMatrix);
                BLAS.Geometry geometry = GetBlasGeometry(blasDesc);

                if (BLAS.Intersect(GetBlas(blasDesc), geometry, localRay, out BLAS.RayHitInfo blasHitInfo, hitInfo.T))
                {
                    hitInfo.Bary = blasHitInfo.Bary;
                    hitInfo.T = blasHitInfo.T;
                    hitInfo.TriangleId = blasDesc.TriangleOffset + blasHitInfo.TriangleId;
                    hitInfo.BlasInstanceId = i;
                }
            }

            return hitInfo.T != tMax;
        }
    }

    public delegate bool FuncIntersectLeafNode(in BoxHitInfo hitInfo);
    public unsafe void Intersect(in Box box, FuncIntersectLeafNode intersectFunc)
    {
        if (CpuUseTlas)
        {
            TLAS.Intersect(TlasNodes, GetBlasAndGeometry, box, intersectFunc);
        }
        else
        {
            for (int i = 0; i < BlasInstances.Length; i++)
            {
                GpuBlasInstance blasInstance = BlasInstances[i];
                GpuBlasDesc blasDesc = BlasesDesc[blasInstance.BlasId];
                ref readonly GpuMeshTransform meshTransform = ref meshTransforms[blasInstance.MeshTransformId];

                Box localBox = Box.Transformed(box, meshTransform.InvModelMatrix);
                BLAS.Geometry geometry = GetBlasGeometry(blasDesc);

                BLAS.Intersect(GetBlas(blasDesc), geometry, localBox, (int triangleId) =>
                {
                    BoxHitInfo hitInfo;
                    hitInfo.TriangleId = blasDesc.TriangleOffset + triangleId;
                    hitInfo.BlasInstanceId = i;

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

    public void SetBlasTransforms(GpuMeshTransform[] meshTransforms)
    {
        this.meshTransforms = meshTransforms;
    }

    public void Add(ReadOnlySpan<BlasBuildDesc> blasBuilds, ReadOnlySpan<GpuBlasInstance> blasInstances)
    {
        int triangleOffset = BlasesDesc.Length > 0 ? BlasesDesc[BlasesDesc.Length - 1].TrianglesEnd : 0;

        Helper.ArrayAdd(ref BlasInstances, blasInstances);
        Array.Resize(ref BlasesDesc, BlasesDesc.Length + blasBuilds.Length);
        Array.Resize(ref BlasTriangles, BlasTriangles.Length + blasBuilds.Sum(it => it.Geometries.Sum(it => it.TriangleCount)));

        Span<GpuBlasDesc> blasesDesc = new Span<GpuBlasDesc>(BlasesDesc, BlasesDesc.Length - blasBuilds.Length, blasBuilds.Length);
        for (int i = 0; i < blasesDesc.Length; i++)
        {
            BlasBuildDesc buildDesc = blasBuilds[i];

            GpuBlasDesc blasDesc = new GpuBlasDesc();
            blasDesc.TriangleOffset = triangleOffset;
            blasDesc.TriangleCount = buildDesc.Geometries.Sum(it => it.TriangleCount);
            blasDesc.IsRefittable = buildDesc.IsRefittable;
            // remaining fields get assigned when building

            blasesDesc[i] = blasDesc;

            for (int j = 0; j < buildDesc.Geometries.Length; j++)
            {
                BlasBuildDesc.Geometry userGeometryDesc = buildDesc.Geometries[j];

                for (int k = 0; k < userGeometryDesc.TriangleCount; k++)
                {
                    ref GpuBlasTriangle blasTriangle = ref BlasTriangles[triangleOffset + k];

                    blasTriangle.X = (int)(userGeometryDesc.VertexOffset + vertexIndices[(userGeometryDesc.TriangleOffset + k) * 3 + 0]);
                    blasTriangle.Y = (int)(userGeometryDesc.VertexOffset + vertexIndices[(userGeometryDesc.TriangleOffset + k) * 3 + 1]);
                    blasTriangle.Z = (int)(userGeometryDesc.VertexOffset + vertexIndices[(userGeometryDesc.TriangleOffset + k) * 3 + 2]);
                    blasTriangle.GeometryId = userGeometryDesc.MeshId;
                }

                triangleOffset += userGeometryDesc.TriangleCount;
            }
        }

        TlasNodes = TLAS.AllocateRequiredNodes(BlasInstances.Length);
    }

    public void TlasBuild(bool force = false)
    {
        if (RebuildTlas || force)
        {
            TLAS.Build(TlasNodes, GetPrimitive, BlasInstances.Length, new TLAS.BuildSettings());
            BBG.Buffer.Recreate(ref tlasBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, TlasNodes);

            Box GetPrimitive(int primId)
            {
                GpuBlasInstance blasInstance = BlasInstances[primId];
                GpuBlasDesc blasDesc = BlasesDesc[blasInstance.BlasId];
                ref readonly GpuMeshTransform meshTransform = ref meshTransforms[blasInstance.MeshTransformId];

                BLAS.BuildResult blas = GetBlas(blasDesc);
                Box localBounds = Conversions.ToBox(blas.Root);
                Box worldSpaceBounds = Box.Transformed(localBounds, meshTransform.ModelMatrix);

                return worldSpaceBounds;
            }
        }
    }

    public unsafe void BlasesBuild(int start, int count)
    {
        if (count == 0) return;

        BlasBuildPhaseData[] newBlasesData = new BlasBuildPhaseData[count];
        GpuBlasDesc prevEndBlasDesc = BlasesDesc[start + count - 1];
        
        BLAS.BuildSettings buildSettings = new BLAS.BuildSettings();
        PreSplitting.Settings preSplittingSettings = new PreSplitting.Settings();
        
        // Statistics
        int preSplitNewTris = 0;
        int newTrisDeduplicated = 0;

        Stopwatch swBuilding = Stopwatch.StartNew();
        Parallel.For(0, count, i =>
        //for (int i = 0; i < count; i++)
        {
            ref GpuBlasDesc blasDesc = ref BlasesDesc[start + i];
            BLAS.Geometry geometry = GetBlasGeometry(blasDesc);

            //System.IO.File.WriteAllBytes(@"C:\Programming\Main\BlasBuilder\BlasBuilder\vertices.bin", System.Runtime.InteropServices.MemoryMarshal.AsBytes(geometry.VertexPositions));
            //System.IO.File.WriteAllBytes(@"C:\Programming\Main\BlasBuilder\BlasBuilder\indices.bin", System.Runtime.InteropServices.MemoryMarshal.AsBytes(geometry.TriIndices));

            // PreSplitting is useless if vertices can arbitrarily change at runtime
            bool doPresplitting = !blasDesc.IsRefittable;

            // Generate bounding boxes from (pre split) triangles.
            // If PreSplitting is enable we also create indices that
            // map a box index to the corresponding source triangle index
            BLAS.Fragments fragments = new BLAS.Fragments();
            (fragments.Bounds, fragments.OriginalTriIds) = doPresplitting ?
                PreSplitting.PreSplit(geometry, preSplittingSettings) :
                (BLAS.GetTriangleBounds(geometry), []);

            // Allocate upper bound of nodes
            GpuBlasNode[] nodes = new GpuBlasNode[BLAS.GetUpperBoundNodes(fragments.Length)];
            BLAS.BuildResult blas = new BLAS.BuildResult(nodes);

            // Build BLAS and resize nodes
            BLAS.BuildData buildData = BLAS.GetBuildData(fragments);
            int nodesUsed = BLAS.Build(ref blas, buildData, buildSettings);

            // Stopwatch sw = Stopwatch.StartNew();
            // var b = BLAS.GetBuildData(fragments);
            // BLAS.Build(ref blas, b, buildSettings);
            // Console.WriteLine(sw.ElapsedMilliseconds);

            Array.Resize(ref nodes, nodesUsed);
            blas.Nodes = nodes;

            // The BLAS holds permutated indices into the inital triangles. Let's get rid of this indirection
            GpuBlasTriangle[] blasTriangles = doPresplitting ?
                PreSplitting.GetUnindexedTriangles(blas, buildData, geometry) :
                BLAS.GetUnindexedTriangles(blas, buildData, geometry);

            // Statistics
            Interlocked.Add(ref preSplitNewTris, fragments.Length - geometry.TriangleCount);
            Interlocked.Add(ref newTrisDeduplicated, blasTriangles.Length - geometry.TriangleCount);

            int[] parentIds = blasDesc.IsRefittable ? BLAS.GetParentIndices(blas) : [];
            int[] leafIds = blasDesc.IsRefittable ? BLAS.GetLeafIndices(blas) : [];

            blasDesc.NodeCount = blas.Nodes.Length;
            blasDesc.RequiredStackSize = blas.RequiredStackSize;
            blasDesc.LeafIndicesCount = leafIds.Length;
            blasDesc.ParentIndicesCount = parentIds.Length;
            blasDesc.TriangleCount = blasTriangles.Length;

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
            blasDesc.NodeOffset = i > 0 ? BlasesDesc[i - 1].NodesEnd : 0;
            blasDesc.TriangleOffset = i > 0 ? BlasesDesc[i - 1].TrianglesEnd : 0;
            blasDesc.LeafIndicesOffset = i > 0 ? BlasesDesc[i - 1].LeafIndicesEnd : 0;
            blasDesc.ParentIndicesOffset = i > 0 ? BlasesDesc[i - 1].ParentIndicesEnd : 0;
        }
        GpuBlasDesc newEndBlasDesc = BlasesDesc[start + count - 1];

        blasParentIdsBuffer.DownloadElements(out int[] blasParentIds);
        blasLeafIndicesBuffer.DownloadElements(out int[] blasLeafIds);

        // Data in the middle resized, move the following data to new offsets so there are no holes
        {
            if (newEndBlasDesc.NodesEnd < prevEndBlasDesc.NodesEnd)
            {
                Helper.ArrayRemove(ref BlasNodes, newEndBlasDesc.NodesEnd, prevEndBlasDesc.NodesEnd - newEndBlasDesc.NodesEnd);
            }
            else
            {
                Helper.ArrayShiftElementsResize(ref BlasNodes, prevEndBlasDesc.NodesEnd, newEndBlasDesc.NodesEnd);
            }

            if (newEndBlasDesc.TrianglesEnd < prevEndBlasDesc.TrianglesEnd)
            {
                Helper.ArrayRemove(ref BlasTriangles, newEndBlasDesc.TrianglesEnd, prevEndBlasDesc.TrianglesEnd - newEndBlasDesc.TrianglesEnd);
            }
            else
            {
                Helper.ArrayShiftElementsResize(ref BlasTriangles, prevEndBlasDesc.TrianglesEnd, newEndBlasDesc.TrianglesEnd);
            }

            if (newEndBlasDesc.LeafIndicesEnd < prevEndBlasDesc.LeafIndicesEnd)
            {
                Helper.ArrayRemove(ref blasLeafIds, newEndBlasDesc.LeafIndicesEnd, prevEndBlasDesc.LeafIndicesEnd - newEndBlasDesc.LeafIndicesEnd);
            }
            else
            {
                Helper.ArrayShiftElementsResize(ref blasLeafIds, prevEndBlasDesc.LeafIndicesEnd, newEndBlasDesc.LeafIndicesEnd);
            }

            if (newEndBlasDesc.ParentIndicesEnd < prevEndBlasDesc.ParentIndicesEnd)
            {
                Helper.ArrayRemove(ref blasParentIds, newEndBlasDesc.ParentIndicesEnd, prevEndBlasDesc.ParentIndicesEnd - newEndBlasDesc.ParentIndicesEnd);
            }
            else
            {
                Helper.ArrayShiftElementsResize(ref blasParentIds, prevEndBlasDesc.ParentIndicesEnd, newEndBlasDesc.ParentIndicesEnd);
            }
        }

        // Copy new data into global arrays
        for (int i = 0; i < count; i++)
        {
            ref readonly BlasBuildPhaseData blasData = ref newBlasesData[i];
            ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[start + i];

            Array.Copy(blasData.Nodes, 0, BlasNodes, blasDesc.NodeOffset, blasDesc.NodeCount);
            Array.Copy(blasData.Triangles, 0, BlasTriangles, blasDesc.TriangleOffset, blasDesc.TriangleCount);
            Array.Copy(blasData.LeafIds, 0, blasLeafIds, blasDesc.LeafIndicesOffset, blasDesc.LeafIndicesCount);
            Array.Copy(blasData.ParentIds, 0, blasParentIds, blasDesc.ParentIndicesOffset, blasDesc.ParentIndicesCount);
        }

        UpdateBlasStackSize();

        BBG.Buffer.Recreate(ref blasDescBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc);
        BBG.Buffer.Recreate(ref blasInstanceBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasInstances);
        BBG.Buffer.Recreate(ref blasTriangleIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasTriangles);
        BBG.Buffer.Recreate(ref blasNodesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasNodes);
        BBG.Buffer.Recreate(ref blasLeafIndicesBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasLeafIds);
        BBG.Buffer.Recreate(ref blasParentIdsBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, blasParentIds);
        BBG.Buffer.Recreate(ref blasRefitLockBuffer, BBG.Buffer.MemLocation.DeviceLocal, BBG.Buffer.MemAccess.AutoSync, BlasesDesc.Max(it => it.NodeCount));

        Logger.Log(Logger.LogLevel.Info, $"Created {count} BLAS'es in {swBuilding.ElapsedMilliseconds}ms");

        if (preSplitNewTris > 0)
        {
            Logger.Log(Logger.LogLevel.Info, $"PreSplitting added {preSplitNewTris} => {newTrisDeduplicated} deduplicated triangles");
        }

        if (true)
        {
            double totalSAH = 0.0f;
            for (int i = start; i < start + count; i++)
            {
                totalSAH += BLAS.ComputeGlobalSAH(GetBlas(i), buildSettings);
            }
            Logger.Log(Logger.LogLevel.Info, $"Added SAH of all new BLAS'es = {totalSAH}");
            //Logger.Log(Logger.LogLevel.Info, $"Added EPO of all new BLAS'es = {BLAS.ComputeGlobalEPO(GetBlas(0), GetBlasGeometry(BlasesDesc[0]), buildSettings)}");
        }
    }

    public void GpuBlasesRefit(int start, int count)
    {
        for (int i = start; i < start + count; i++)
        {
            GpuBlasDesc blasDesc = BlasesDesc[i];

            blasRefitLockBuffer.Fill(0);
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
        BLAS.Refit(blas, GetBlasGeometry(blasDesc));
    }

    public BLAS.BuildResult GetBlas(int index)
    {
        return GetBlas(BlasesDesc[index]);
    }

    public BLAS.BuildResult GetBlas(in GpuBlasDesc blasDesc)
    {
        BLAS.BuildResult blas = new BLAS.BuildResult();
        blas.Nodes = new Span<GpuBlasNode>(BlasNodes, blasDesc.NodeOffset, blasDesc.NodeCount);
        blas.RequiredStackSize = blasDesc.RequiredStackSize;

        return blas;
    }

    public BLAS.Geometry GetBlasGeometry(in GpuBlasDesc blasDesc)
    {
        BLAS.Geometry geometry = new BLAS.Geometry();
        geometry.VertexPositions = vertexPositions;
        geometry.TriIndices = new Span<GpuBlasTriangle>(BlasTriangles, blasDesc.TriangleOffset, blasDesc.TriangleCount);
        return geometry;
    }

    public Range GetBlasesNodeRange(Range blases)
    {
        Range range = new Range();
        range.Start = BlasesDesc[blases.Start].NodeOffset;
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
        range.Start = BlasesDesc[blases.Start].TriangleOffset;
        range.End = BlasesDesc[blases.End - 1].TrianglesEnd;

        return range;
    }

    private void GetBlasAndGeometry(int blasInstanceId, out BLAS.BuildResult blas, out BLAS.Geometry geometry, out int triangleOffset, out Matrix4 invWorldTransform)
    {
        GpuBlasInstance blasInstance = BlasInstances[blasInstanceId];
        ref readonly GpuBlasDesc blasDesc = ref BlasesDesc[blasInstance.BlasId];
        ref readonly GpuMeshTransform meshTransform = ref meshTransforms[blasInstance.MeshTransformId];

        blas = GetBlas(blasDesc);
        geometry = GetBlasGeometry(blasDesc);

        invWorldTransform = meshTransform.InvModelMatrix;
        triangleOffset = blasDesc.TriangleOffset;
    }

    private void UpdateBlasStackSize()
    {
        int max = 0;
        for (int i = 0; i < BlasesDesc.Length; i++)
        {
            max = Math.Max(max, BlasesDesc[i].RequiredStackSize);
        }
        BlasStackSize = max;
    }

    public void Dispose()
    {
        blasDescBuffer.Dispose();
        blasInstanceBuffer.Dispose();
        refitBlasProgram.Dispose();
        tlasBuffer.Dispose();
        blasRefitLockBuffer.Dispose();
        blasParentIdsBuffer.Dispose();
        blasLeafIndicesBuffer.Dispose();
        blasTriangleIndicesBuffer.Dispose();
        blasNodesBuffer.Dispose();
    }
}
