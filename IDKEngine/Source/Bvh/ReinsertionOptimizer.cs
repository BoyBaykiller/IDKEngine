using System;
using System.Collections.Generic;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;

namespace IDKEngine.Bvh;

/// <summary>
/// Implementation of "Parallel Reinsertion for Bounding Volume Hierarchy Optimization"
/// https://meistdan.github.io/publications/prbvh/paper.pdf
/// </summary>
public ref struct ReinsertionOptimizer
{
    public record struct Settings
    {
        public int Iterations = 3;
        public float CandidatesPercentage = 0.05f;

        public Settings()
        {
        }
    }

    private record struct Reinsertion
    {
        public int In;
        public int Out;
        public float AreaDecrease;
    }

    private record struct Candidate : MyComparer.IComparisons<Candidate>
    {
        public int NodeId;
        public float Cost;

        public static bool operator >(in Candidate lhs, in Candidate rhs)
        {
            return lhs.Cost > rhs.Cost;
        }

        public static bool operator <(in Candidate lhs, in Candidate rhs)
        {
            return lhs.Cost < rhs.Cost;
        }
    }

    public static void Optimize(ref BLAS.BuildResult blas, Span<int> parentIds, in Settings settings)
    {
        if (blas.Nodes.Length <= 3)
        {
            return;
        }

        new ReinsertionOptimizer(blas.Nodes, parentIds).Optimize(settings);
        blas.MaxTreeDepth = BLAS.ComputeTreeDepth(blas);
    }

    private readonly Span<GpuBlasNode> nodes;
    private readonly Span<int> parentIds;
    
    private ReinsertionOptimizer(Span<GpuBlasNode> nodes, Span<int> parentIds)
    {
        this.nodes = nodes;
        this.parentIds = parentIds;
    }

    private readonly void Optimize(in Settings settings)
    {
        Candidate[] candidates = GetCandidatesMem();
        for (int i = 0; i < settings.Iterations; i++)
        {
            int count = GetImportantCandidates(candidates, settings.CandidatesPercentage);

            for (int j = 0; j < count; j++)
            {
                Reinsertion reinsertion = FindReinsertion(candidates[j].NodeId);

                if (reinsertion.AreaDecrease > 0.0f)
                {
                    ReinsertNode(reinsertion.In, reinsertion.Out);
                }
            }
        }

        {
            // Always make node 0 point to 1, as this is expected by traversal
            // Always make node 1 point to 3, as this is good memory access
            if (nodes[0].TriStartOrChild != 1)
            {
                SwapChildrenInMem(0, parentIds[1]);
            }
            if (!nodes[1].IsLeaf && nodes[1].TriStartOrChild != 3)
            {
                SwapChildrenInMem(1, parentIds[3]);
            }
        }
    }

    private readonly int GetImportantCandidates(Span<Candidate> candidates, float percentage)
    {
        int count = Math.Min(nodes.Length - 1, (int)(nodes.Length * percentage));
        if (count == 0) return count;

        for (int i = 1; i < candidates.Length + 1; i++)
        {
            ref Candidate candidate = ref candidates[i - 1];
            float cost = nodes[i].HalfArea();
            candidate.Cost = cost;
            candidate.NodeId = i;
        }

        Random rng = new Random(42);
        Algorithms.PartialSort(candidates, count, rng, MyComparer.GreaterThan);

        return count;
    }

    private readonly void ReinsertNode(int inId, int outId)
    {
        // See Figure.2 https://meistdan.github.io/publications/prbvh/paper.pdf

        int siblingId = BLAS.GetSiblingId(inId);
        int parentId = parentIds[inId];
        GpuBlasNode siblingNode = nodes[siblingId];
        GpuBlasNode outNode = nodes[outId];

        // Parent of 'in' becomes sibling of 'in'
        nodes[parentId] = siblingNode;

        // Sibling of 'in' becomes 'out'
        nodes[siblingId] = outNode;

        // 'siblingNode' and 'outNode' have moved. Notify their children of the new parentId
        if (!siblingNode.IsLeaf)
        {
            parentIds[siblingNode.TriStartOrChild + 0] = parentId;
            parentIds[siblingNode.TriStartOrChild + 1] = parentId;
        }
        if (!outNode.IsLeaf)
        {
            parentIds[outNode.TriStartOrChild + 0] = siblingId;
            parentIds[outNode.TriStartOrChild + 1] = siblingId;
        }

        // Link 'out' to it's new children 'in' & 'sibling' and update their parentId
        nodes[outId].TriStartOrChild = BLAS.GetLeftSiblingId(inId);
        nodes[outId].TriCount = 0; // mark as internal ndoe

        parentIds[inId] = outId;
        parentIds[siblingId] = outId;

        BLAS.RefitFromNode(parentId, nodes, parentIds); // Refit old parent of 'in'
        BLAS.RefitFromNode(outId, nodes, parentIds); // Refit new parent of 'in'   
    }

    private readonly Reinsertion FindReinsertion(int nodeId)
    {
        // Source: https://github.com/madmann91/bvh/blob/3490634ae822e5081e41f09498fcce03bc1419e3/src/bvh/v2/reinsertion_optimizer.h#L107

        Reinsertion bestReinsertion = new Reinsertion();
        bestReinsertion.In = nodeId;

        int parentId = parentIds[nodeId];
        int pivotId = parentId;
        int siblingId = BLAS.GetSiblingId(nodeId);

        ref readonly GpuBlasNode inputNode = ref nodes[nodeId];
        float nodeArea = inputNode.HalfArea();
        float areaDecrease = nodes[parentId].HalfArea();

        Box pivotBox = Conversions.ToBox(nodes[siblingId]);

        // areaDecrease, nodeId
        Stack<ValueTuple<float, int>> stack = new Stack<ValueTuple<float, int>>();
        do
        {
            stack.Push((areaDecrease, siblingId));
            while (stack.Count > 0)
            {
                ValueTuple<float, int> stackTop = stack.Pop();
                if (stackTop.Item1 - nodeArea <= bestReinsertion.AreaDecrease)
                {
                    continue;
                }

                ref readonly GpuBlasNode outNode = ref nodes[stackTop.Item2];

                Box mergedBox = Box.From(Conversions.ToBox(inputNode), Conversions.ToBox(outNode));

                float mergedArea = mergedBox.HalfArea();
                float thisAreaDecrease = stackTop.Item1 - mergedArea;
                if (thisAreaDecrease > bestReinsertion.AreaDecrease)
                {
                    bestReinsertion.Out = stackTop.Item2;
                    bestReinsertion.AreaDecrease = thisAreaDecrease;
                }

                if (!outNode.IsLeaf)
                {
                    float childArea = thisAreaDecrease + outNode.HalfArea();
                    stack.Push((childArea, outNode.TriStartOrChild + 0));
                    stack.Push((childArea, outNode.TriStartOrChild + 1));
                }

                if (pivotId != parentId)
                {
                    pivotBox.GrowToFit(Conversions.ToBox(nodes[siblingId]));
                    areaDecrease += nodes[pivotId].HalfArea() - pivotBox.HalfArea();
                }
            }

            siblingId = BLAS.GetSiblingId(pivotId);
            pivotId = parentIds[pivotId];

        } while (pivotId != -1);

        if (bestReinsertion.Out == BLAS.GetSiblingId(bestReinsertion.In) ||
            bestReinsertion.Out == parentIds[bestReinsertion.In])
        {
            bestReinsertion = new Reinsertion();
        }

        return bestReinsertion;
    }

    private readonly Candidate[] GetCandidatesMem()
    {
        return new Candidate[nodes.Length - 1];
    }

    private readonly void SwapChildrenInMem(int inParent, int outParent)
    {
        int inLeftChildId = nodes[inParent].TriStartOrChild;
        int inRightChildId = nodes[inParent].TriStartOrChild + 1;

        int outLeftChildId = nodes[outParent].TriStartOrChild;
        int outRightChildId = nodes[outParent].TriStartOrChild + 1;

        Algorithms.Swap(ref nodes[inLeftChildId], ref nodes[outLeftChildId]);
        Algorithms.Swap(ref nodes[inRightChildId], ref nodes[outRightChildId]);

        nodes[inParent].TriStartOrChild = outLeftChildId;

        if (inLeftChildId == outParent)
        {
            outParent = outLeftChildId;
        }
        if (inRightChildId == outParent)
        {
            outParent = outRightChildId;
        }
        nodes[outParent].TriStartOrChild = inLeftChildId;

        UpdateChildParentIds(nodes, parentIds, inParent);
        UpdateChildParentIds(nodes, parentIds, outParent);
        UpdateChildParentIds(nodes, parentIds, inLeftChildId);
        UpdateChildParentIds(nodes, parentIds, inRightChildId);
        UpdateChildParentIds(nodes, parentIds, outLeftChildId);
        UpdateChildParentIds(nodes, parentIds, outRightChildId);

        static void UpdateChildParentIds(Span<GpuBlasNode> nodes, Span<int> parentIds, int parentNodeId)
        {
            ref readonly GpuBlasNode parent = ref nodes[parentNodeId];
            if (!parent.IsLeaf)
            {
                parentIds[parent.TriStartOrChild + 0] = parentNodeId;
                parentIds[parent.TriStartOrChild + 1] = parentNodeId;
            }
        }
    }
}
