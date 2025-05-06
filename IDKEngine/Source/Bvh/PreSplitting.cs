using System;
using System.Linq;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    public static class PreSplitting
    {
        public record struct Settings
        {
            public float SplitFactor = 0.4f;

            public Settings()
            {
            }
        }

        public static ValueTuple<Box[], int[]> PreSplit(in BLAS.Geometry geometry, Settings settings)
        {
            // Source:
            // * https://github.com/madmann91/bvh/blob/2fd0db62022993963a7343669275647cb073e19a/include/bvh/heuristic_primitive_splitter.hpp
            // * https://research.nvidia.com/sites/default/files/pubs/2013-07_Fast-Parallel-Construction/karras2013hpg_paper.pdf

            Box globalBox = BLAS.ComputeBoundingBox(0, geometry.TriangleCount, geometry);
            Vector3 globalSize = globalBox.Size();

            float totalPriority = 0.0f;
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle triangle = geometry.GetTriangle(i);
                totalPriority += Priority(Box.From(triangle), triangle);
            }

            int referenceCount = 0;
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle triangle = geometry.GetTriangle(i);
                float priority = Priority(Box.From(triangle), triangle);
                int splitCount = GetSplitCount(priority, totalPriority, geometry.TriangleCount);

                referenceCount += splitCount;
            }

            Box[] bounds = new Box[referenceCount];
            int[] originalTriIds = new int[referenceCount];
            Vector3[] centers = new Vector3[referenceCount];

            int counter = 0;

            Span<ValueTuple<Box, int>> stack = stackalloc ValueTuple<Box, int>[64];
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle triangle = geometry.GetTriangle(i);
                Box triBox = Box.From(triangle);

                float priority = Priority(triBox, triangle);
                int splitCount = GetSplitCount(priority, totalPriority, geometry.TriangleCount);

                int stackPtr = 0;
                stack[stackPtr++] = (triBox, splitCount);
                while (stackPtr > 0)
                {
                    (Box box, int splitsLeft) = stack[--stackPtr];

                    if (splitsLeft == 1)
                    {
                        bounds[counter] = box;
                        originalTriIds[counter] = i;
                        centers[counter] = box.Center(); 
                        counter++;
                        continue;
                    }

                    int splitAxis = box.LargestAxis();
                    float largestExtent = box.Size()[splitAxis];

                    float depth = Math.Min(-1.0f, MathF.Floor(MathF.Log2(largestExtent / globalSize[splitAxis])));
                    float cellSize = float.Exp2(depth) * globalSize[splitAxis];
                    if (cellSize + 0.0001f >= largestExtent)
                    {
                        cellSize *= 0.5f;
                    }

                    float midPos = (box.Min[splitAxis] + box.Max[splitAxis]) * 0.5f;
                    float splitPos = globalBox.Min[splitAxis] + MathF.Round((midPos - globalBox.Min[splitAxis]) / cellSize) * cellSize;
                    if (splitPos < box.Min[splitAxis] || splitPos > box.Max[splitAxis])
                    {
                        splitPos = midPos;
                    }

                    (Box lBox, Box rBox) = triangle.Split(splitAxis, splitPos);
                    lBox.ShrinkToFit(box);
                    rBox.ShrinkToFit(box);

                    float leftExtent = lBox.LargestExtent();
                    float rightExtent = rBox.LargestExtent();
                    
                    int leftCount = (int)(splitsLeft * (leftExtent / (leftExtent + rightExtent)));
                    leftCount = Math.Max(leftCount, 1);
                    leftCount = Math.Max(1, leftCount);

                    int rightCount = splitsLeft - leftCount;

                    stack[stackPtr++] = (lBox, leftCount);
                    stack[stackPtr++] = (rBox, rightCount);
                }
            }

            int GetSplitCount(float priority, float totalPriority, int triangleCount)
            {
                float shareOfTris = priority / totalPriority * triangleCount;
                int splitCount = 1 + (int)(shareOfTris * settings.SplitFactor);

                return splitCount;
            }

            static float Priority(Box triBox, Triangle triangle)
            {
                return MathF.Cbrt(triBox.LargestExtent() * (triBox.Area() - triangle.Area));
            }

            return (bounds, originalTriIds);
        }

        /// <summary>
        /// This is equivalent to <see cref="BLAS.GetUnindexedTriangles(in BLAS.BuildData, in BLAS.Geometry)"/>
        /// except that it also removes duplicate triangle references in leaf(-pairs) which may happen as a consequence of pre-splitting.
        /// This means for a leaf-pair their triangle ranges [lStart, lEnd), [rStart, rEnd) can intersect. These are the "straddling triangles"
        /// </summary>
        /// <returns></returns>
        public static unsafe GpuIndicesTriplet[] GetUnindexedTriangles(in BLAS.BuildResult blas, in BLAS.BuildData buildData, in BLAS.Geometry geometry, ReadOnlySpan<int> originalTriIds)
        {
            GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[buildData.ReferenceCount];

            int globalTriCounter = 0;

            int stackPtr = 0;
            Span<int> stack = stackalloc int[64];
            stack[stackPtr++] = 1;
            while (stackPtr > 0)
            {
                int stackTop = stack[--stackPtr];

                ref GpuBlasNode leftChild = ref blas.Nodes[stackTop];
                ref GpuBlasNode rightChild = ref blas.Nodes[stackTop + 1];

                if (leftChild.IsLeaf && rightChild.IsLeaf)
                {
                    Span<int> leftUniqueTriIds = GetUniqueTriIds(leftChild, originalTriIds, buildData);
                    Span<int> rightUniqueTriIds = GetUniqueTriIds(rightChild, originalTriIds, buildData);
                    Span<int> straddlingTriIds = GetStraddlingTriIds(leftUniqueTriIds, rightUniqueTriIds);

                    int onlyLeftTriCount = 0;
                    int backwardsCounter = 0;
                    for (int i = 0; i < leftUniqueTriIds.Length; i++)
                    {
                        int triId = leftUniqueTriIds[i];
                        if (straddlingTriIds.Contains(triId))
                        {
                            triangles[globalTriCounter + (leftUniqueTriIds.Length - backwardsCounter++ - 1)] = geometry.Triangles[triId];
                        }
                        else
                        {
                            triangles[globalTriCounter + onlyLeftTriCount++] = geometry.Triangles[triId];
                        }
                    }

                    int onlyRightTriCount = 0;
                    for (int i = 0; i < rightUniqueTriIds.Length; i++)
                    {
                        int triId = rightUniqueTriIds[i];
                        if (!straddlingTriIds.Contains(triId))
                        {
                            triangles[globalTriCounter + leftUniqueTriIds.Length + onlyRightTriCount++] = geometry.Triangles[triId];
                        }
                    }

                    leftChild.TriStartOrChild = globalTriCounter;
                    leftChild.TriCount = leftUniqueTriIds.Length;

                    rightChild.TriStartOrChild = globalTriCounter + onlyLeftTriCount;
                    rightChild.TriCount = rightUniqueTriIds.Length;

                    int leafsTriCount = rightChild.TriEnd - leftChild.TriStartOrChild;
                    globalTriCounter += leafsTriCount;
                }
                else if (leftChild.IsLeaf || rightChild.IsLeaf)
                {
                    ref GpuBlasNode theLeafNode = ref (leftChild.IsLeaf ? ref leftChild : ref rightChild);

                    Span<int> uniqueTriIds = GetUniqueTriIds(theLeafNode, originalTriIds, buildData);
                    for (int i = 0; i < uniqueTriIds.Length; i++)
                    {
                        triangles[globalTriCounter + i] = geometry.Triangles[uniqueTriIds[i]];
                    }

                    theLeafNode.TriStartOrChild = globalTriCounter;
                    theLeafNode.TriCount = uniqueTriIds.Length;
                    globalTriCounter += uniqueTriIds.Length;
                }

                if (!rightChild.IsLeaf)
                {
                    stack[stackPtr++] = rightChild.TriStartOrChild;
                }
                if (!leftChild.IsLeaf)
                {
                    stack[stackPtr++] = leftChild.TriStartOrChild;
                }
            }

            static Span<int> GetUniqueTriIds(in GpuBlasNode leafNode, ReadOnlySpan<int> originalTriIds, in BLAS.BuildData buildData)
            {
                Span<int> triIds = new int[leafNode.TriCount];
                for (int i = 0; i < leafNode.TriCount; i++)
                {
                    triIds[i] = originalTriIds[buildData.PermutatedTriangleIds[leafNode.TriStartOrChild + i]];
                }

                MemoryExtensions.Sort(triIds);

                int uniqueTriCount = Algorithms.SortedFilterDuplicates(triIds);
                Span<int> uniqueTriIds = triIds.Slice(0, uniqueTriCount);

                return uniqueTriIds;
            }

            static Span<int> GetStraddlingTriIds(Span<int> leftTriIds, Span<int> rightTriIds)
            {
                List<int> straddlingTris = new List<int>((leftTriIds.Length + rightTriIds.Length) / 4);
                for (int i = 0; i < leftTriIds.Length; i++)
                {
                    int triId = leftTriIds[i];

                    if (rightTriIds.Contains(triId))
                    {
                        straddlingTris.Add(triId);
                    }
                }

                return straddlingTris;
            }

            Array.Resize(ref triangles, globalTriCounter);

            return triangles;
        }
    }
}
