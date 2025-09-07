using System;
using System.Runtime.CompilerServices;
using OpenTK.Mathematics;
using IDKEngine.Shapes;

namespace IDKEngine.Bvh
{
    /// <summary>
    /// Improved implementation of "Early-Split-Clipping" from "Fast Parallel Construction of High-Quality Bounding Volume Hierarchies"
    /// https://research.nvidia.com/sites/default/files/pubs/2013-07_Fast-Parallel-Construction/karras2013hpg_paper.pdf
    /// </summary>
    public class PreSplitting
    {
        public record struct Settings
        {
            public readonly bool Enabled => SplitFactor > 0.0f;

            public float SplitFactor = 0.3f;

            public Settings()
            {
            }
        }

        public record struct PrepareData
        {
            public int FragmentCount;
            public float TotalPriority;
        }

        public static PrepareData Prepare(in BLAS.Geometry geometry, in Settings settings)
        {
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
                int splitCount = GetSplitCount(priority, totalPriority, geometry.TriangleCount, settings.SplitFactor);

                counter += splitCount;
            }

            PrepareData info;
            info.TotalPriority = totalPriority;
            info.FragmentCount = counter;

            return info;
        }

        public static void PreSplit(PrepareData prepareData, Span<Box> bounds, Span<int> originalTriIds, in Settings settings, in BLAS.Geometry geometry)
        {
            // Source:
            // * https://github.com/madmann91/bvh/blob/2fd0db62022993963a7343669275647cb073e19a/include/bvh/heuristic_primitive_splitter.hpp
            // * https://research.nvidia.com/sites/default/files/pubs/2013-07_Fast-Parallel-Construction/karras2013hpg_paper.pdf

            Box globalBox = BLAS.ComputeBoundingBox(0, geometry.TriangleCount, geometry);
            Vector3 globalSize = globalBox.Size();

            int counter = 0;

            Span<ValueTuple<Box, int>> stack = stackalloc ValueTuple<Box, int>[64];
            for (int i = 0; i < geometry.TriangleCount; i++)
            {
                Triangle triangle = geometry.GetTriangle(i);
                Box triBox = Box.From(triangle);

                float priority = Priority(triBox, triangle);
                int splitCount = GetSplitCount(priority, prepareData.TotalPriority, geometry.TriangleCount, settings.SplitFactor);

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

                    stack[stackPtr++] = (lBox, leftCount);
                    stack[stackPtr++] = (rBox, rightCount);
                }
            }
        }

        private static int GetSplitCount(float priority, float totalPriority, int triangleCount, float splitFactor)
        {
            float shareOfTris = priority / totalPriority * triangleCount;
            int splitCount = 1 + (int)(shareOfTris * splitFactor);

            return splitCount;
        }

        private static float Priority(Box triBox, Triangle triangle)
        {
            return MathF.Cbrt(MathF.Pow(triBox.LargestExtent(), 2.0f) * (triBox.Area() - triangle.Area));
        }

        private static float GetCellSize(float alpha)
        {
            // Erase all except the exponent bits.
            // This has the same effect as 2^(floor(log2(alpha)))
            int floatBits = Unsafe.BitCast<float, int>(alpha);
            floatBits &= 255 << 23;

            return Unsafe.BitCast<int, float>(floatBits);
        }
    }
}
