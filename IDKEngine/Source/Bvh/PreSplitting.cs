using System;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    /// <summary>
    /// Improved implementation of "Early-Split-Clipping" from "Fast Parallel Construction of High-Quality Bounding Volume Hierarchies"
    /// https://research.nvidia.com/sites/default/files/pubs/2013-07_Fast-Parallel-Construction/karras2013hpg_paper.pdf
    /// </summary>
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

            int counter = 0;
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle triangle = geometry.GetTriangle(i);
                float priority = Priority(Box.From(triangle), triangle);
                int splitCount = GetSplitCount(priority, totalPriority, geometry.TriangleCount);

                counter += splitCount;
            }

            Box[] bounds = new Box[counter];
            int[] originalTriIds = new int[counter];

            counter = 0;

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
                        counter++;
                        continue;
                    }

                    int splitAxis = box.LargestAxis();
                    float largestExtent = box.Size()[splitAxis];

                    float alpha = largestExtent / globalSize[splitAxis];
                    float cellSize = GetCellSize(alpha) * globalSize[splitAxis];
                    if (cellSize >= largestExtent - 0.0001f)
                    {
                        cellSize *= 0.5f;
                    }

                    float midPos = (box.Min[splitAxis] + box.Max[splitAxis]) * 0.5f;

                    // This is computing [x = j * 2 ^ i, where i,j∈Z] as shown in the paper
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

                    int leftCount = (int)MathF.Round(splitsLeft * (leftExtent / (leftExtent + rightExtent)));
                    leftCount = Math.Clamp(leftCount, 1, splitsLeft - 1);

                    int rightCount = splitsLeft - leftCount;

                    stack[stackPtr++] = (rBox, rightCount);
                    stack[stackPtr++] = (lBox, leftCount);
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

            static float GetCellSize(float alpha)
            {
                // Erase all except the exponent bits.
                // This has the same effect as 2^(floor(log2(alpha)))
                int floatBits = Unsafe.BitCast<float, int>(alpha);
                floatBits &= 255 << 23;

                return Unsafe.BitCast<int, float>(floatBits);
            }

            return (bounds, originalTriIds);
        }

        /// <summary>
        /// When Pre-Splitting was done prior to building the BLAS duplicate triangle references in a leaf(-pair) may happen.
        /// Here we deduplicate them, possibly resulting in leaf-pair triangle ranges like:
        /// [lStart, lEnd), [rStart, rEnd), where the "straddling triangles" in range [rStart, lEnd) is shared between the left and right node.
        /// Otherwise this is equivalent to <see cref="BLAS.GetUnindexedTriangles(in BLAS.BuildData, in BLAS.Geometry)"/>
        /// </summary>
        /// <returns></returns>
        public static unsafe GpuIndicesTriplet[] GetUnindexedTriangles(in BLAS.BuildResult blas, in BLAS.BuildData buildData, in BLAS.Geometry geometry)
        {
            GpuIndicesTriplet[] triangles = new GpuIndicesTriplet[buildData.Fragments.Count];

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
                    Span<int> leftUniqueTriIds = GetUniqueTriIds(leftChild, buildData);
                    Span<int> rightUniqueTriIds = GetUniqueTriIds(rightChild, buildData);
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

                    Span<int> uniqueTriIds = GetUniqueTriIds(theLeafNode, buildData);
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

            static Span<int> GetUniqueTriIds(in GpuBlasNode leafNode, in BLAS.BuildData buildData)
            {
                Span<int> triIds = new int[leafNode.TriCount];
                for (int i = 0; i < leafNode.TriCount; i++)
                {
                    triIds[i] = buildData.Fragments.OriginalTriIds[buildData.PermutatedFragmentIds[leafNode.TriStartOrChild + i]];
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
