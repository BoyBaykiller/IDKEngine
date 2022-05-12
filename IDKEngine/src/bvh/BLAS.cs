using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenTK.Mathematics;
using IDKEngine.Render;

namespace IDKEngine
{
    class BLAS
    {
        public const int BITS_FOR_VERTICES_START = 31;

        public readonly int MaxTreeDepth;
        public readonly GLSLNode[][] Nodes;
        public readonly GLSLVertex[][] Vertices;
        public unsafe BLAS(ModelSystem modelSystem, int maxTreeDepth, uint trianglesPerLevelHint = 45u)
        {
            MaxTreeDepth = maxTreeDepth;

            List<GLSLVertex> vertices = new List<GLSLVertex>(modelSystem.Vertices.Length);
            GLSLNode[] rootNodes = new GLSLNode[modelSystem.Meshes.Length];
            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                rootNodes[i].Min = new Vector3(float.MaxValue);
                rootNodes[i].Max = new Vector3(float.MinValue);
                rootNodes[i].IsLeafAndVerticesStart = (uint)vertices.Count;
                for (int j = modelSystem.DrawCommands[i].FirstIndex; j < modelSystem.DrawCommands[i].FirstIndex + modelSystem.DrawCommands[i].Count; j++)
                {
                    uint indici = (uint)modelSystem.DrawCommands[i].BaseVertex + modelSystem.Indices[j];
                    GLSLVertex vertex = modelSystem.Vertices[indici];
                    vertices.Add(vertex);

                    rootNodes[i].Min = Vector3.ComponentMin(rootNodes[i].Min, vertex.Position);
                    rootNodes[i].Max = Vector3.ComponentMax(rootNodes[i].Max, vertex.Position);
                }
                rootNodes[i].MissLinkAndVerticesCount = (uint)vertices.Count - rootNodes[i].IsLeafAndVerticesStart;
            }


            Nodes = new GLSLNode[modelSystem.Meshes.Length][];
            Vertices = new GLSLVertex[modelSystem.Meshes.Length][];
            ParallelLoopResult parallelLoopResult = Parallel.For(0, modelSystem.Meshes.Length, i =>
            {
                GLSLNode root = rootNodes[i];

                int treeDepth = (int)Math.Max(Math.Min(root.MissLinkAndVerticesCount / trianglesPerLevelHint, MaxTreeDepth), 2u);
                uint nodesForMesh = (1u << treeDepth) - 1u;
                modelSystem.Meshes[i].BLASDepth = treeDepth;
                int bitsForMissLink = treeDepth; // ceil(log2(NODES_PER_MESH))
                int verticesStart = (int)root.IsLeafAndVerticesStart;
                int verticesEnd = (int)(root.IsLeafAndVerticesStart + root.MissLinkAndVerticesCount);

                List<GLSLVertex> localBVHVertices = new List<GLSLVertex>((int)(root.MissLinkAndVerticesCount * 1.5f));
                GLSLNode[] localNodes = new GLSLNode[nodesForMesh];
                localNodes[0] = root;
                
                for (int level = 0; level < treeDepth; level++)
                {
                    uint localIndex = (uint)level;
                    uint distance = GetDistanceSibling(level, nodesForMesh);

                    for (int horNode = 0; horNode < GetNodesOnLevel(level); horNode++)
                    {
                        if (horNode == GetNodesOnLevel(level) - 1)
                            SetMissLink(ref localNodes[localIndex], nodesForMesh, bitsForMissLink);
                        else if (horNode % 2 == 0)
                            SetMissLink(ref localNodes[localIndex], localIndex + distance, bitsForMissLink);
                        else
                            SetMissLink(ref localNodes[localIndex], localIndex + GetDistanceInterNode(distance, horNode / 2) - 1u, bitsForMissLink);

                        if (level < treeDepth - 1)
                        {
                            ConstructChildBounds(localNodes[localIndex], out GLSLNode child0, out GLSLNode child1);
                            if (level == treeDepth - 2)
                            {
                                MakeLeaf(ref child0);
                                MakeLeaf(ref child1);
                            }

                            localNodes[localIndex + 1] = child0;
                            localNodes[GetRightChildIndex((int)localIndex, treeDepth, level)] = child1;

                        }
                        localIndex += horNode % 2 == 0 ? distance : GetDistanceInterNode(distance, horNode / 2);
                    }
                }
                Nodes[i] = localNodes;
                Vertices[i] = localBVHVertices.ToArray();

                void MakeLeaf(ref GLSLNode node)
                {
                    Debug.Assert((uint)localBVHVertices.Count < (1u << BITS_FOR_VERTICES_START));
                    node.IsLeafAndVerticesStart = (uint)localBVHVertices.Count;

                    Vector3 center = (node.Min + node.Max) * 0.5f;
                    Vector3 halfSize = (node.Max - node.Min) * (0.5f + 0.000001f);
                    for (int i = verticesStart; i < verticesEnd; i += 3)
                    {
                        if (MyMath.TriangleVSBox(vertices[i + 0].Position, vertices[i + 1].Position, vertices[i + 2].Position, center, halfSize))
                        {
                            localBVHVertices.Add(vertices[i + 0]);
                            localBVHVertices.Add(vertices[i + 1]);
                            localBVHVertices.Add(vertices[i + 2]);
                        }
                    }
                    uint count = (uint)localBVHVertices.Count - node.IsLeafAndVerticesStart;

                    Debug.Assert(count < (1u << (32 - bitsForMissLink)));
                    MyMath.BitsInsert(ref node.MissLinkAndVerticesCount, count, 0);
                    MyMath.BitsInsert(ref node.IsLeafAndVerticesStart, 1, BITS_FOR_VERTICES_START);
                }
            });
            while (!parallelLoopResult.IsCompleted) ;

            int nodesOffset = 0, verticesOffset = 0;
            for (int i = 0; i < Nodes.Length; i++)
            {
                for (int j = 0; j < Nodes[i].Length; j++)
                {
                    if (MyMath.GetBits(Nodes[i][j].IsLeafAndVerticesStart, BITS_FOR_VERTICES_START, 1) == 1)
                    {
                        uint globalStart = (uint)verticesOffset + Nodes[i][j].IsLeafAndVerticesStart;
                        Nodes[i][j].IsLeafAndVerticesStart = globalStart;
                        MyMath.BitsInsert(ref Nodes[i][j].IsLeafAndVerticesStart, 1, BITS_FOR_VERTICES_START);
                    }
                }
                modelSystem.Meshes[i].NodeStart = nodesOffset;

                nodesOffset += Nodes[i].Length;
                verticesOffset += Vertices[i].Length;
            }

            modelSystem.UpdateMeshBuffer(0, modelSystem.Meshes.Length);
        }

        private static void ConstructChildBounds(in GLSLNode parent, out GLSLNode child0, out GLSLNode child1)
        {
            child0 = new GLSLNode();
            child1 = new GLSLNode();

            child0.Min = parent.Min;
            child0.Max = parent.Max;
            child1.Min = parent.Min;
            child1.Max = parent.Max;

            Vector3 parentNodeSize = parent.Max - parent.Min;
            int axis = 0;
            if (parentNodeSize.Y > parentNodeSize.X) axis = 1;
            if (parentNodeSize.Z > parentNodeSize[axis]) axis = 2; 

            child0.Max[axis] -= parentNodeSize[axis] * 0.5f;
            child1.Min[axis] += parentNodeSize[axis] * 0.5f;
        }

        private static int GetRightChildIndex(int parent, int treeDepth, int level)
        {
            return parent + (1 << (treeDepth - (level + 1)));
        }
        private static void SetMissLink(ref GLSLNode node, uint missLink, int bitsForMissLink)
        {
            Debug.Assert(missLink < (1u << bitsForMissLink));
            MyMath.BitsInsert(ref node.MissLinkAndVerticesCount, missLink, 32 - bitsForMissLink);
        }
        private static uint GetNodesOnLevel(int level)
        {
            return 1u << level;
        }
        private static uint GetDistanceInterNode(uint distanceSiblings, int n)
        {
            // Source: https://oeis.org/A090739

            // Source: https://oeis.org/A007814
            static uint A007814(int n)
            {
                if (n % 2 != 0)
                    return 0u;

                return 1u + A007814(n / 2);
            }

            return A007814(n + 1) + distanceSiblings + 1u;
        }
        private static uint GetDistanceSibling(int level, uint allNodesCount)
        {
            return allNodesCount / (1u << level);
        }
    }
}
