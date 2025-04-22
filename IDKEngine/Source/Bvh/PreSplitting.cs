using System;
using System.Collections.Generic;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public static class PreSplitting
    {
        public static ValueTuple<Box[], int[]> PreSplit(in BLAS.Geometry geometry)
        {
            const float alpha = 0.01f; // 1.0 => no splits; 0.0 => infinite splits
            float globalArea = BLAS.ComputeBoundingBox(0, geometry.TriangleCount, geometry).HalfArea();

            List<Box> bounds = new List<Box>(geometry.TriangleCount);
            List<int> originalTriIds = new List<int>(geometry.TriangleCount);

            Span<Box> stack = stackalloc Box[64];

            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle triangle = geometry.GetTriangle(i);

                int stackPtr = 0;
                stack[stackPtr++] = Box.From(triangle);
                while (stackPtr > 0)
                {
                    Box box = stack[--stackPtr];

                    float percentGlobalArea = box.HalfArea() / globalArea;
                    bool doSplit = percentGlobalArea > alpha;

                    if (doSplit)
                    {
                        int axis = box.LargestAxis();
                        float pos = (box.Min[axis] + box.Max[axis]) * 0.5f;

                        (Box lBox, Box rBox) = triangle.Split(axis, pos);

                        lBox.ShrinkToFit(box);
                        rBox.ShrinkToFit(box);

                        stack[stackPtr++] = lBox;
                        stack[stackPtr++] = rBox;
                    }
                    else
                    {
                        bounds.Add(box);
                        originalTriIds.Add(i);
                    }
                }
            }

            return (bounds.ToArray(), originalTriIds.ToArray());
        }

        /// <summary>
        /// This is equivalent to <see cref="BLAS.GetUnindexedTriangles(in BLAS.BuildData, in BLAS.Geometry)"/>
        /// except that it also removes duplicate triangle references which may happen as a consequence of pre-splitting
        /// </summary>
        /// <returns></returns>
        public static GpuIndicesTriplet[] GetUnindexedTriangles(in BLAS.BuildResult blas, in BLAS.BuildData buildData, in BLAS.Geometry geometry, int[] originalTriIds)
        {
            GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[buildData.TriangleCount];

            int globalCounter = 0;
            for (int i = 0; i < blas.UnpaddedNodesCount; i++)
            {
                ref GpuBlasNode node = ref blas.Nodes[i];
                if (node.IsLeaf)
                {
                    Span<int> triangleIndices = buildData.TriangleIndices.Slice(node.TriStartOrChild, node.TriCount);
                    MemoryExtensions.Sort(triangleIndices, (int a, int b) =>
                    {
                        if (originalTriIds[a] > originalTriIds[b]) return 1;
                        if (originalTriIds[a] < originalTriIds[b]) return -1;
                        return 0;
                    });

                    int lastNewTriId = -1;
                    int localCounter = 0;
                    for (int k = 0; k < triangleIndices.Length; k++)
                    {
                        int triId = originalTriIds[triangleIndices[k]];
                        if (lastNewTriId != triId)
                        {
                            triangles[globalCounter + localCounter++] = geometry.Triangles[triId];
                            lastNewTriId = triId;
                        }
                    }
                    node.TriStartOrChild = globalCounter;
                    node.TriCount = localCounter;
                    globalCounter += node.TriCount;
                }
            }
            Array.Resize(ref triangles, globalCounter);

            return triangles;
        }
    }
}
