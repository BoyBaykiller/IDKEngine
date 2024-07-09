using System;
using System.Collections.Generic;
using OpenTK.Mathematics;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.GpuTypes;
using IDKEngine.Source.Utils;

namespace IDKEngine.Bvh
{
    public partial class BLAS
    {
        public struct OptimizationSettings
        {
            public int Iterations = 3;
            public float CandidatesPercentage = 0.05f;

            public OptimizationSettings()
            {
            }
        }

        /// <summary>
        /// Implementation of https://meistdan.github.io/publications/prbvh/paper.pdf
        /// </summary>
        private class ReinsertionOptimizer
        {
            public static void Optimize(BLAS blas, in OptimizationSettings settings)
            {
                new ReinsertionOptimizer(blas).Optimize(settings);
            }

            private struct Reinsertion
            {
                public int In;
                public int Out;
                public float AreaDecrease;
            }

            private struct Candidate : MyComparer.IComparisons<Candidate>
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

            private readonly BLAS blas;
            private readonly int[] parentIds;
            private ReinsertionOptimizer(BLAS blas)
            {
                this.blas = blas;
                parentIds = GetParentIndices(blas.Nodes);
            }

            private void Optimize(in OptimizationSettings settings)
            {
                Candidate[] candidatesMem = GetCandidatesMem();
                for (int i = 0; i < settings.Iterations; i++)
                {
                    Span<Candidate> candidates = PopulateCandidates(candidatesMem, settings.CandidatesPercentage);

                    for (int j = 0; j < candidates.Length; j++)
                    {
                        ref readonly Candidate candidate = ref candidates[j];
                        Reinsertion reinsertion = FindReinsertion(candidate.NodeId);

                        if (reinsertion.AreaDecrease > 0.0f)
                        {
                            ReinsertNode(reinsertion.In, reinsertion.Out);
                        }
                    }
                }

                RestoreTreeQualities();
            }

            private void ReinsertNode(int inId, int outId)
            {
                // See Figure.2 https://meistdan.github.io/publications/prbvh/paper.pdf

                int siblingId = GetSiblingId(inId);
                int parentId = parentIds[inId];
                GpuBlasNode siblingNode = blas.Nodes[siblingId];
                GpuBlasNode outNode = blas.Nodes[outId];

                // Parent of 'in' becomes sibling of 'in'
                blas.Nodes[parentId] = siblingNode;

                // Sibling of 'in' becomes 'out'
                blas.Nodes[siblingId] = outNode;

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
                blas.Nodes[outId].TriStartOrChild = (uint)GetLeftSiblingId(inId);
                blas.Nodes[outId].TriCount = 0; // mark as internal ndoe

                parentIds[inId] = outId;
                parentIds[siblingId] = outId;

                RefitFrom(parentId); // Refit old parent of 'in'
                RefitFrom(outId); // Refit new parent of 'in'   
            }

            private Candidate[] GetCandidatesMem()
            {
                return new Candidate[blas.Nodes.Length - 1];
            }

            private Span<Candidate> PopulateCandidates(Candidate[] candidates, float percentage)
            {
                int count = Math.Min(blas.Nodes.Length - 1, (int)(blas.Nodes.Length * percentage));
                if (count == 0) return Array.Empty<Candidate>();

                for (int i = 1; i < candidates.Length + 1; i++)
                {
                    Candidate candidate = new Candidate();
                    candidate.Cost = blas.Nodes[i].HalfArea();
                    candidate.NodeId = i;
                    candidates[i - 1] = candidate;
                }

                Helper.PartialSort<Candidate>(candidates, 0, candidates.Length, 0, count, MyComparer.GreaterThan);

                return new Span<Candidate>(candidates, 0, count);

                //Candidate[] candidates = new Candidate[searchCount];
                //for (int i = 1; i < searchCount + 1; i++)
                //{
                //    Candidate candidate = new Candidate();
                //    candidate.Cost = blas.Nodes[i].HalfArea();
                //    candidate.NodeId = i;
                //    candidates[i - 1] = candidate;
                //}

                //Array.Sort(candidates, MyComparer.GreaterThan);

                //for (int i = searchCount + 1; i < blas.Nodes.Length; i++)
                //{
                //    float cost = blas.Nodes[i].HalfArea();
                //    float lowestCost = candidates[candidates.Length - 1].Cost;
                //    if (cost > lowestCost)
                //    {
                //        Candidate newCandidate = new Candidate();
                //        newCandidate.Cost = cost;
                //        newCandidate.NodeId = i;
                //        Helper.MaintainDescendingArray(candidates, newCandidate, MyComparer.GreaterThan);
                //    }
                //}

                //return candidates;
            }

            private Reinsertion FindReinsertion(int nodeId)
            {
                // Source: https://github.com/madmann91/bvh/blob/3490634ae822e5081e41f09498fcce03bc1419e3/src/bvh/v2/reinsertion_optimizer.h#L107
               
                Reinsertion bestReinsertion = new Reinsertion();
                bestReinsertion.In = nodeId;

                int parentId = parentIds[nodeId];
                int pivotId = parentId;
                int siblingId = GetSiblingId(nodeId);

                ref readonly GpuBlasNode inputNode = ref blas.Nodes[nodeId];
                float nodeArea = inputNode.HalfArea();
                float areaDecrease = blas.Nodes[parentId].HalfArea();

                Box pivotBox = Conversions.ToBox(blas.Nodes[siblingId]);

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

                        ref readonly GpuBlasNode outNode = ref blas.Nodes[stackTop.Item2];

                        Box mergedBox = Conversions.ToBox(inputNode);
                        mergedBox.GrowToFit(outNode.Min);
                        mergedBox.GrowToFit(outNode.Max);

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
                            stack.Push((childArea, (int)outNode.TriStartOrChild + 0));
                            stack.Push((childArea, (int)outNode.TriStartOrChild + 1));
                        }

                        if (pivotId != parentId)
                        {
                            pivotBox.GrowToFit(Conversions.ToBox(blas.Nodes[siblingId]));
                            areaDecrease += blas.Nodes[pivotId].HalfArea() - pivotBox.HalfArea();
                        }
                    }

                    siblingId = GetSiblingId(pivotId);
                    pivotId = parentIds[pivotId];

                } while (pivotId != 0);

                if (bestReinsertion.Out == GetSiblingId(bestReinsertion.In) ||
                    bestReinsertion.Out == parentIds[bestReinsertion.In])
                {
                    bestReinsertion = new Reinsertion();
                }

                return bestReinsertion;
            }

            private void RefitFrom(int nodeId)
            {
                while (true)
                {
                    ref GpuBlasNode node = ref blas.Nodes[nodeId];
                    if (!node.IsLeaf)
                    {
                        ref readonly GpuBlasNode leftChild = ref blas.Nodes[node.TriStartOrChild];
                        ref readonly GpuBlasNode rightChild = ref blas.Nodes[node.TriStartOrChild + 1];

                        Box mergedBox = Conversions.ToBox(leftChild);
                        mergedBox.GrowToFit(rightChild.Min);
                        mergedBox.GrowToFit(rightChild.Max);

                        node.SetBounds(mergedBox);
                    }
                    if (nodeId == 0)
                    {
                        break;
                    }
                    nodeId = parentIds[nodeId];
                }
            }

            /// <summary>
            /// Makes sure the root always always points to index 1 as its left child and
            /// that primitives of two leafs always form a continuous range in memory.
            /// </summary>
            private void RestoreTreeQualities()
            {
                // Always make node 0 point to 1, as this is expected by traversal and
                // always make node 1 point to 3, as this is good memory access
                if (blas.Nodes[0].TriStartOrChild != 1)
                {
                    SwapChildren(0, parentIds[1]);
                }
                if (!blas.Nodes[1].IsLeaf && blas.Nodes[1].TriStartOrChild != 3)
                {
                    SwapChildren(1, parentIds[3]);
                }

                IndicesTriplet[] newTriIndices = new IndicesTriplet[blas.TriangleCount];
                uint triCounter = 0;

                Span<uint> stack = stackalloc uint[32];
                int stackPtr = 0;
                stack[stackPtr++] = 1;

                while (stackPtr > 0)
                {
                    uint stackTop = stack[--stackPtr];

                    ref GpuBlasNode leftChild = ref blas.Nodes[stackTop];
                    ref GpuBlasNode rightChild = ref blas.Nodes[stackTop + 1];

                    if (leftChild.IsLeaf)
                    {
                        Array.Copy(blas.TriangleIndices, leftChild.TriStartOrChild, newTriIndices, triCounter, leftChild.TriCount);
                        leftChild.TriStartOrChild = triCounter;
                        triCounter += leftChild.TriCount;
                    }
                    if (rightChild.IsLeaf)
                    {
                        Array.Copy(blas.TriangleIndices, rightChild.TriStartOrChild, newTriIndices, triCounter, rightChild.TriCount);
                        rightChild.TriStartOrChild = triCounter;
                        triCounter += rightChild.TriCount;
                    }

                    if (!leftChild.IsLeaf)
                    {
                        stack[stackPtr++] = leftChild.TriStartOrChild;
                    }

                    if (!rightChild.IsLeaf)
                    {
                        stack[stackPtr++] = rightChild.TriStartOrChild;
                    }
                }

                blas.TriangleIndices = newTriIndices;
                blas.MaxTreeDepth = ComputeTreeDepth(blas.Nodes);
            }

            private void SwapChildren(int inParent, int outParent)
            {
                uint inLeftChildId = blas.Nodes[inParent].TriStartOrChild;
                uint inRightChildId = blas.Nodes[inParent].TriStartOrChild + 1;

                uint outLeftChildId = blas.Nodes[outParent].TriStartOrChild;
                uint outRightChildId = blas.Nodes[outParent].TriStartOrChild + 1;

                blas.Nodes[inParent].TriStartOrChild = outLeftChildId;
                blas.Nodes[outParent].TriStartOrChild = inLeftChildId;

                MathHelper.Swap(ref blas.Nodes[inLeftChildId], ref blas.Nodes[outLeftChildId]);
                MathHelper.Swap(ref blas.Nodes[inRightChildId], ref blas.Nodes[outRightChildId]);

                MathHelper.Swap(ref parentIds[inLeftChildId], ref parentIds[outLeftChildId]);
                MathHelper.Swap(ref parentIds[inRightChildId], ref parentIds[outRightChildId]);
            }

            private static int[] GetParentIndices(ReadOnlySpan<GpuBlasNode> nodes)
            {
                int[] parents = new int[nodes.Length];
                parents[0] = 0;
                for (int i = 0; i < nodes.Length; i++)
                {
                    ref readonly GpuBlasNode node = ref nodes[i];
                    if (!node.IsLeaf)
                    {
                        parents[node.TriStartOrChild + 0] = i;
                        parents[node.TriStartOrChild + 1] = i;
                    }
                }

                return parents;
            }
        }
    }
}
