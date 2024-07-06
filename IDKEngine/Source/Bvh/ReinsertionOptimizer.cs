using System;
using System.Collections.Generic;
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
            public float NodePercentage = 0.05f;

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

            private struct Reinsertion : MyComparer.IComparisons<Reinsertion>
            {
                public int In;
                public int Out;
                public float AreaDecrease;

                public static bool operator >(in Reinsertion lhs, in Reinsertion rhs)
                {
                    return lhs.AreaDecrease > rhs.AreaDecrease;
                }

                public static bool operator <(in Reinsertion lhs, in Reinsertion rhs)
                {
                    return lhs.AreaDecrease < rhs.AreaDecrease;
                }
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
                for (int i = 0; i < settings.Iterations; i++)
                {
                    Candidate[] candidates = FindCandidates((int)(blas.Nodes.Length * settings.NodePercentage));

                    //BitArray touchedNodes = new BitArray(blas.Nodes.Length);
                    //List<Reinsertion> reinsertions = new List<Reinsertion>(candidates.Length);

                    //for (int j = 0; j < candidates.Length; j++)
                    //{
                    //    reinsertions.Add(FindReinsertion(candidates[j].NodeId));
                    //}

                    //reinsertions.RemoveAll(it => it.AreaDecrease <= 0.0f);
                    //reinsertions.Sort(MyComparer.GreaterThan);

                    //for (int j = 0; j < reinsertions.Count; j++)
                    //{
                    //    Reinsertion reinsertion = reinsertions[j];

                    //    int[] nodeConflicts = GetConflicts(reinsertion);
                    //    for (int k = 0; k < nodeConflicts.Length; k++)
                    //    {
                    //        if (touchedNodes[nodeConflicts[k]])
                    //        {
                    //            continue;
                    //        }
                    //    }
                    //    for (int k = 0; k < nodeConflicts.Length; k++)
                    //    {
                    //        touchedNodes[nodeConflicts[k]] = true;
                    //    }

                    //    ReinsertNode(reinsertion.In, reinsertion.Out);
                    //}
                    //continue;

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

            private Candidate[] FindCandidates(int num)
            {
                if (num == 0) return Array.Empty<Candidate>();
                int searchCount = Math.Min(blas.Nodes.Length - 1, num);

                //Candidate[] candidates = new Candidate[blas.Nodes.Length - 1];
                //for (int i = 1; i < candidates.Length + 1; i++)
                //{
                //    Candidate candidate = new Candidate();
                //    candidate.Cost = blas.Nodes[i].HalfArea();
                //    candidate.NodeId = i;
                //    candidates[i - 1] = candidate;
                //}
                //Array.Sort(candidates, MyComparer.GreaterThan);
                //Array.Resize(ref candidates, searchCount);

                Candidate[] candidates = new Candidate[searchCount];
                for (int i = 1; i < searchCount + 1; i++)
                {
                    Candidate candidate = new Candidate();
                    candidate.Cost = blas.Nodes[i].HalfArea();
                    candidate.NodeId = i;
                    candidates[i - 1] = candidate;
                }

                Array.Sort(candidates, MyComparer.LessThan);

                for (int i = searchCount + 1; i < blas.Nodes.Length; i++)
                {
                    float cost = blas.Nodes[i].HalfArea();
                    float lowestCost = candidates[0].Cost;
                    if (cost > lowestCost)
                    {
                        Candidate newCandidate = new Candidate();
                        newCandidate.Cost = cost;
                        newCandidate.NodeId = i;
                        Helper.MaintainSortedArray(candidates, newCandidate, MyComparer.LessThan);
                    }
                }

                return candidates;
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

                        node.Min = mergedBox.Min;
                        node.Max = mergedBox.Max;
                    }
                    if (nodeId == 0)
                    {
                        break;
                    }
                    nodeId = parentIds[nodeId];
                }
            }

            private int[] GetConflicts(in Reinsertion reinsertion)
            {
                int[] conflicts = new int[6];
                conflicts[0] = reinsertion.In;
                conflicts[1] = GetSiblingId(reinsertion.In);
                conflicts[2] = parentIds[reinsertion.In];

                conflicts[3] = reinsertion.Out;
                conflicts[4] = parentIds[reinsertion.Out];

                // TODO: Why does https://github.com/madmann91/bvh/blob/3490634ae822e5081e41f09498fcce03bc1419e3/src/bvh/v2/reinsertion_optimizer.h#L227 not have this conflict? It's listed in the paper
                conflicts[5] = parentIds[parentIds[reinsertion.In]]; 

                return conflicts;
            }

            /// <summary>
            /// Reorders the tree to match the output of a typical top-down builder and makes sure the primitives of two leafs
            /// form a continuous range in memory. However the right child may store the range beginning.
            /// </summary>
            private void RestoreTreeQualities()
            {
                IndicesTriplet[] newTriIndices = new IndicesTriplet[blas.TriangleCount];
                GpuBlasNode[] newNodes = new GpuBlasNode[blas.Nodes.Length];
                uint triCounter = 0;
                uint nodesCounter = 0;
                newNodes[nodesCounter++] = blas.Nodes[0];

                Span<ValueTuple<uint, uint>> stack = stackalloc ValueTuple<uint, uint>[32];
                int stackPtr = 0;
                stack[stackPtr++] = (blas.Nodes[0].TriStartOrChild, 0);

                while (stackPtr > 0)
                {
                    (uint stackTop, uint newParent) = stack[--stackPtr];
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

                    newNodes[newParent].TriStartOrChild = nodesCounter;

                    if (!leftChild.IsLeaf)
                    {
                        stack[stackPtr++] = (leftChild.TriStartOrChild, nodesCounter);
                    }
                    newNodes[nodesCounter++] = leftChild;

                    if (!rightChild.IsLeaf)
                    {
                        stack[stackPtr++] = (rightChild.TriStartOrChild, nodesCounter);
                    }
                    newNodes[nodesCounter++] = rightChild;
                }

                blas.Nodes = newNodes;
                blas.TriangleIndices = newTriIndices;
                blas.MaxTreeDepth = ComputeTreeDepth(newNodes);
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
