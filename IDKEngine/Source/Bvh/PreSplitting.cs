using System;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh;

/// <summary>
/// Improved implementation of "Early-Split-Clipping" from "Fast Parallel Construction of High-Quality Bounding Volume Hierarchies"
/// https://research.nvidia.com/sites/default/files/pubs/2013-07_Fast-Parallel-Construction/karras2013hpg_paper.pdf
/// </summary>
public static class PreSplitting
{
    public record struct Settings
    {
        public float SplitFactor = 0.3f;

        public Settings()
        {
        }
    }

    public static ValueTuple<Box[], int[]> PreSplit(BLAS.Geometry geometry, Settings settings)
    {
        // Source:
        // * https://github.com/madmann91/bvh/blob/2fd0db62022993963a7343669275647cb073e19a/include/bvh/heuristic_primitive_splitter.hpp
        // * https://research.nvidia.com/sites/default/files/pubs/2013-07_Fast-Parallel-Construction/karras2013hpg_paper.pdf

        float totalPriority = 0.0f;
        for (int i = 0; i < geometry.TriangleCount; i++)
        {
            Triangle triangle = geometry.GetTriangle(i);
            totalPriority += Priority(triangle);
        }

        int counter = 0;
        for (int i = 0; i < geometry.TriangleCount; i++)
        {
            Triangle triangle = geometry.GetTriangle(i);
            float priority = Priority(triangle);
            int splitCount = GetSplitCount(priority, totalPriority, geometry.TriangleCount);

            counter += splitCount;
        }

        Box[] bounds = new Box[counter];
        int[] originalTriIds = new int[counter];

        counter = 0;

        Box globalBox = BLAS.ComputeBoundingBox(0, geometry.TriangleCount, geometry);
        Vector3 globalSize = globalBox.Size();

        Span<ValueTuple<Box, int>> stack = stackalloc ValueTuple<Box, int>[64];
        for (int i = 0; i < geometry.TriangleCount; i++)
        {
            Triangle triangle = geometry.GetTriangle(i);

            float priority = Priority(triangle);
            int splitCount = GetSplitCount(priority, totalPriority, geometry.TriangleCount);

            int stackPtr = 0;
            stack[stackPtr++] = (Box.From(triangle), splitCount);
            while (stackPtr > 0)
            {
                (Box parentBox, int splitsLeft) = stack[--stackPtr];

                if (splitsLeft == 1)
                {
                    bounds[counter] = parentBox;
                    originalTriIds[counter] = i;
                    counter++;
                    continue;
                }

                int splitAxis = parentBox.LargestAxis();
                float largestExtent = parentBox.LargestExtent();

                float nodeSize = GetNodeSize(largestExtent, globalSize[splitAxis]);
                if (nodeSize >= largestExtent - 0.0001f)
                {
                    nodeSize *= 0.5f;
                }

                // Snap mid position to nearest split plane (still inside parentBox)
                float midPos = (parentBox.Min[splitAxis] + parentBox.Max[splitAxis]) * 0.5f;
                float index = MathF.Round((midPos - globalBox.Min[splitAxis]) / nodeSize);
                float splitPos = globalBox.Min[splitAxis] + index * nodeSize;

                (Box lBox, Box rBox) = triangle.Split(splitAxis, splitPos);
                lBox.ClipAgainst(parentBox);
                rBox.ClipAgainst(parentBox);

                float leftExtent = lBox.LargestExtent();
                float rightExtent = rBox.LargestExtent();

                int leftCount = (int)MathF.Round(splitsLeft * (leftExtent / (leftExtent + rightExtent)));
                leftCount = Math.Clamp(leftCount, 1, splitsLeft - 1);

                int rightCount = splitsLeft - leftCount;

                stack[stackPtr++] = (rBox, rightCount);
                stack[stackPtr++] = (lBox, leftCount);
            }
        }

        return (bounds, originalTriIds);

        int GetSplitCount(float priority, float totalPriority, int triangleCount)
        {
            float shareOfTris = priority / totalPriority * triangleCount;
            int splitCount = 1 + (int)(shareOfTris * settings.SplitFactor);

            return splitCount;
        }

        static float Priority(Triangle triangle)
        {
            Box triBox = Box.From(triangle);

            // Extent^2 to concentrate more splits on large triangles
            float extentPrio = triBox.LargestExtent() * triBox.LargestExtent();
            float emptyAreaPrio = triBox.Area() - triangle.Area;

            // Cbrt to more evenly distribute among triangles
            return MathF.Cbrt(extentPrio * emptyAreaPrio);
        }

        static float GetNodeSize(float extent, float globalSize)
        {
            // See slide 64 in https://www.highperformancegraphics.org/wp-content/uploads/2013/Karras-BVH.pdf
            // Split planes are defined by recursive spatial median splits of the scene box.
            // Here we find the largest node size that is still smaller than the extent
            // For alpha between 0.5 and 1.0. level => -1, size => 0.5
            // For alpha between 0.25 and 0.5. level => -2, size => 0.25
            // For alpha between 0.125 and 0.25. level => -3, size => 0.125
            // 
            // In code:
            // int level = (int)MathF.Floor(MathF.Log2(alpha));
            // float size = MathF.Pow(2.0f, level);
            // 
            // In the paper:
            // i == level
            // 2 ^ i == size

            // Transform into [0.0, 1.0]
            float alpha = extent / globalSize;

            // This computes 2^(floor(log2(alpha)))
            uint exponentBits = Unsafe.BitCast<float, uint>(alpha) & (255u << 23);

            // Transform back into global space
            float size = Unsafe.BitCast<uint, float>(exponentBits) * globalSize;

            return size;
        }
    }

    /// <summary>
    /// When PreSplitting was done prior to building the BLAS duplicate triangle references in a leaf(-pair) may happen.
    /// Here we deduplicate them, possibly resulting in leaf-pair triangle ranges like:
    /// [lStart, lEnd), [rStart, rEnd), where the "straddling triangles" in range [rStart, lEnd) are shared between the left and right node.
    /// Otherwise this is equivalent to <see cref="BLAS.GetUnindexedTriangles(BLAS.BuildResult, BLAS.BuildData, BLAS.Geometry)"/>
    /// </summary>
    /// <returns></returns>
    public static GpuBlasTriangle[] GetUnindexedTriangles(BLAS.BuildResult blas, BLAS.BuildData buildData, BLAS.Geometry geometry)
    {
        const bool straddlingOpt = true;
        const bool leafOpt = true;

        GpuBlasTriangle[] triangles = new GpuBlasTriangle[buildData.Fragments.Length];

        int globalTriCounter = 0;

        int stackPtr = 0;
        Span<int> stack = stackalloc int[128];
        stack[stackPtr++] = 2;
        while (stackPtr > 0)
        {
            int stackTop = stack[--stackPtr];

            ref GpuBlasNode leftNode = ref blas.Nodes[stackTop];
            ref GpuBlasNode rightNode = ref blas.Nodes[stackTop + 1];

            if (leftNode.IsLeaf && rightNode.IsLeaf)
            {
                Span<int> leftUniqueTriIds = GetUniqueTriIds(leftNode, buildData);
                Span<int> rightUniqueTriIds = GetUniqueTriIds(rightNode, buildData);

                int onlyLeftTriCount = 0;
                int backwardsCounter = 0;
                for (int i = 0; i < leftUniqueTriIds.Length; i++)
                {
                    int leftTriId = leftUniqueTriIds[i];
                    bool isStraddling = straddlingOpt && rightUniqueTriIds.Contains(leftTriId);

                    if (isStraddling)
                    {
                        triangles[globalTriCounter + leftUniqueTriIds.Length - backwardsCounter++ - 1] = geometry.TriIndices[leftTriId];
                    }
                    else
                    {
                        triangles[globalTriCounter + onlyLeftTriCount++] = geometry.TriIndices[leftTriId];
                    }
                }

                int onlyRightTriCount = 0;
                for (int i = 0; i < rightUniqueTriIds.Length; i++)
                {
                    int rightTriId = rightUniqueTriIds[i];
                    bool isStraddling = straddlingOpt && leftUniqueTriIds.Contains(rightTriId);
                    if (!isStraddling)
                    {
                        triangles[globalTriCounter + leftUniqueTriIds.Length + onlyRightTriCount++] = geometry.TriIndices[rightTriId];
                    }
                }

                leftNode.TriStartOrChild = globalTriCounter;
                leftNode.TriCount = leftUniqueTriIds.Length;

                rightNode.TriStartOrChild = globalTriCounter + onlyLeftTriCount;
                rightNode.TriCount = rightUniqueTriIds.Length;

                int leafsTriCount = rightNode.TriEnd - leftNode.TriStartOrChild;
                globalTriCounter += leafsTriCount;
            }
            else if (leftNode.IsLeaf || rightNode.IsLeaf)
            {
                ref GpuBlasNode theLeafNode = ref (leftNode.IsLeaf ? ref leftNode : ref rightNode);

                Span<int> uniqueTriIds = GetUniqueTriIds(theLeafNode, buildData);
                for (int i = 0; i < uniqueTriIds.Length; i++)
                {
                    triangles[globalTriCounter + i] = geometry.TriIndices[uniqueTriIds[i]];
                }

                theLeafNode.TriStartOrChild = globalTriCounter;
                theLeafNode.TriCount = uniqueTriIds.Length;
                globalTriCounter += uniqueTriIds.Length;
            }

            if (!rightNode.IsLeaf) stack[stackPtr++] = rightNode.TriStartOrChild;
            if (!leftNode.IsLeaf) stack[stackPtr++] = leftNode.TriStartOrChild;
        }

        Array.Resize(ref triangles, globalTriCounter);

        return triangles;

        static Span<int> GetUniqueTriIds(GpuBlasNode leafNode, BLAS.BuildData buildData)
        {
            Span<int> triIds = new int[leafNode.TriCount];
            for (int i = 0; i < leafNode.TriCount; i++)
            {
                triIds[i] = buildData.Fragments.OriginalTriIds[buildData.PermutatedFragmentIds[leafNode.TriStartOrChild + i]];
            }

            int uniqueTriCount = triIds.Length;
            
            if (leafOpt)
            {
                MemoryExtensions.Sort(triIds);
                uniqueTriCount = Algorithms.SortedFilterDuplicates(triIds);
            }

            Span<int> uniqueTriIds = triIds.Slice(0, uniqueTriCount);

            return uniqueTriIds;
        }
    }
}
