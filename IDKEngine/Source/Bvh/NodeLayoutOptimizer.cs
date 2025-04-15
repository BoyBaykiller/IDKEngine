using System;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh
{
    struct NodeLayoutOptimizer
    {
        public static void Optimize(BLAS.BuildResult blas)
        {
            if (blas.UnpaddedNodesCount <= 3) return;

            int pairCount = (blas.UnpaddedNodesCount - 3) / 2;
            float[] pairAreas = new float[pairCount];
            int[] unsortedIndices = new int[pairCount];

            for (int i = 3; i < blas.UnpaddedNodesCount; i += 2)
            {
                ref readonly GpuBlasNode leftNode = ref blas.Nodes[i + 0];
                ref readonly GpuBlasNode rightNode = ref blas.Nodes[i + 1];

                Box mergedBox = Conversions.ToBox(leftNode);
                mergedBox.GrowToFit(Conversions.ToBox(rightNode));

                int pairId = (i - 3) / 2;
                pairAreas[pairId] = mergedBox.HalfArea();
                unsortedIndices[pairId] = pairId;
            }

            int[] sortedIndices = new int[pairCount];
            Algorithms.RadixSort(unsortedIndices, sortedIndices, (int pairId) =>
            {
                return Algorithms.FloatToKey(pairAreas[pairId]);
            });

            Span<GpuBlasNode> newNodes = new GpuBlasNode[blas.UnpaddedNodesCount];
            newNodes[0] = blas.Nodes[0];
            newNodes[1] = blas.Nodes[1];
            newNodes[2] = blas.Nodes[2];

            for (int i = 0; i < pairCount; i++)
            {
                int pairId = sortedIndices[pairCount - i - 1];
                int srcId = pairId * 2 + 3;
                int dstId = i * 2 + 3;

                newNodes[dstId + 0] = blas.Nodes[srcId + 0];
                newNodes[dstId + 1] = blas.Nodes[srcId + 1];
                unsortedIndices[pairId] = dstId;
            }

            for (int i = 1; i < newNodes.Length; i++)
            {
                ref readonly GpuBlasNode node = ref newNodes[i];
                if (node.IsLeaf)
                {
                    continue;
                }

                int pairId = (newNodes[i].TriStartOrChild - 3) / 2;
                newNodes[i].TriStartOrChild = unsortedIndices[pairId];
            }

            newNodes.CopyTo(blas.Nodes);
        }
    }
}
