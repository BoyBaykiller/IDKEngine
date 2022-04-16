using System;
using System.Diagnostics;
using System.Collections.Generic;
using OpenTK.Mathematics;
using IDKEngine.Render;

namespace IDKEngine
{
    class BLAS
    {
        public const int TREE_DEPTH = 8;
        public const uint NODES_PER_MESH = (1u << TREE_DEPTH) - 1u;
        public const int BITS_FOR_MISS_LINK = TREE_DEPTH; // ceil(log2(NODES_PER_MESH))
        public const int BITS_FOR_VERTICES_START = 31;

        public readonly GLSLNode[] Nodes;
        public readonly GLSLBLASVertex[] Vertices;
        public unsafe BLAS(ModelSystem modelSystem)
        {
            GLSLNode[] nodes = new GLSLNode[NODES_PER_MESH * modelSystem.Meshes.Length];

            List<GLSLBLASVertex> vertices = new List<GLSLBLASVertex>(modelSystem.Vertices.Length);
            List<GLSLBLASVertex> bvhVertices = new List<GLSLBLASVertex>(modelSystem.Vertices.Length);

            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                int start = vertices.Count;
                for (int j = modelSystem.DrawCommands[i].FirstIndex; j < modelSystem.DrawCommands[i].FirstIndex + modelSystem.DrawCommands[i].Count; j++)
                {
                    uint indici = (uint)modelSystem.DrawCommands[i].BaseVertex + modelSystem.Indices[j];
                    GLSLBLASVertex vertex = *(GLSLBLASVertex*)((GLSLVertex*)modelSystem.Vertices.ToPtr() + indici);
                    vertices.Add(vertex);
                    
                    min = Vector3.ComponentMin(min, vertex.Position);
                    max = Vector3.ComponentMax(max, vertex.Position);
                }
                int end = vertices.Count;

                int baseIndex = (int)(NODES_PER_MESH * i);
                modelSystem.Meshes[i].BaseIndex = baseIndex;

                GLSLNode root = new GLSLNode();
                root.Min = min;
                root.Max = max;
                nodes[baseIndex] = root;
                for (int level = 0; level < TREE_DEPTH; level++)
                {
                    int n = 0;
                    uint localIndex = (uint)level;
                    uint distance = GetDistanceSibling(level, NODES_PER_MESH);

                    for (int horNode = 0; horNode < GetNodesOnLevel(level); horNode++)
                    {
                        // Set miss link of this parent
                        if (horNode == GetNodesOnLevel(level) - 1)
                            SetMissLink(ref nodes[baseIndex + localIndex], NODES_PER_MESH);
                        else if (horNode % 2 == 0)
                            SetMissLink(ref nodes[baseIndex + localIndex], localIndex + distance);
                        else
                            SetMissLink(ref nodes[baseIndex + localIndex], localIndex + GetDistanceInterNode(distance, n) - 1u);

                        if (level < TREE_DEPTH - 1)
                        {
                            ConstructChildBounds(nodes[baseIndex + localIndex], out GLSLNode child0, out GLSLNode child1);

                            if (level == TREE_DEPTH - 2)
                            {
                                MakeLeaf(ref child0, start, end);
                                MakeLeaf(ref child1, start, end);
                            }

                            nodes[baseIndex + localIndex + 1] = child0;
                            nodes[baseIndex + GetRightChildIndex((int)localIndex, TREE_DEPTH, level)] = child1;
                        }
                        localIndex += horNode % 2 != 0 ? GetDistanceInterNode(distance, n++) : distance;
                    }
                }
            }
            modelSystem.MeshBuffer.SubData(0, modelSystem.Meshes.Length * sizeof(GLSLMesh), modelSystem.Meshes);

            Nodes = nodes;
            Vertices = bvhVertices.ToArray();

            void MakeLeaf(ref GLSLNode node, int start, int end)
            {
                Debug.Assert((uint)bvhVertices.Count < (1u << BITS_FOR_VERTICES_START));
                node.IsLeafAndVerticesStart = (uint)bvhVertices.Count;

                Vector3 center = (node.Min + node.Max) * 0.5f;
                Vector3 halfSize = (node.Max - node.Min) * 0.5f;
                for (int i = start; i < end; i += 3)
                {
                    if (MyMath.TriangleVSBox(vertices[i + 0].Position, vertices[i + 1].Position, vertices[i + 2].Position, center, halfSize))
                    {
                        bvhVertices.Add(vertices[i + 0]);
                        bvhVertices.Add(vertices[i + 1]);
                        bvhVertices.Add(vertices[i + 2]);
                    }
                }
                uint count = (uint)bvhVertices.Count - node.IsLeafAndVerticesStart;
                
                Debug.Assert(count < (1u << (32 - BITS_FOR_MISS_LINK)));
                MyMath.BitsInsert(ref node.MissLinkAndVerticesCount, count, 0, 32 - BITS_FOR_MISS_LINK);

                MyMath.BitsInsert(ref node.IsLeafAndVerticesStart, 1, BITS_FOR_VERTICES_START, 1);
            }
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
            if (parentNodeSize.X > parentNodeSize.Y)
            {
                if (parentNodeSize.X > parentNodeSize.Z)
                {
                    child0.Max.X -= parentNodeSize.X / 2.0f;
                    child1.Min.X += parentNodeSize.X / 2.0f;
                }
                else
                {
                    child0.Max.Z -= parentNodeSize.Z / 2.0f;
                    child1.Min.Z += parentNodeSize.Z / 2.0f;
                }
            }
            else
            {
                child0.Max.Y -= parentNodeSize.Y / 2.0f;
                child1.Min.Y += parentNodeSize.Y / 2.0f;
            }
        }

        private static int GetRightChildIndex(int parent, int treeDepth, int level)
        {
            return parent + (1 << (treeDepth - (level + 1)));
        }
        private static void SetMissLink(ref GLSLNode node, uint missLink)
        {
            Debug.Assert(missLink < (1u << BITS_FOR_MISS_LINK));
            MyMath.BitsInsert(ref node.MissLinkAndVerticesCount, missLink, 32 - BITS_FOR_MISS_LINK, BITS_FOR_MISS_LINK);
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
