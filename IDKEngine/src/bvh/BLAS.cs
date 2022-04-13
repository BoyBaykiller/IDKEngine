using System;
using System.Diagnostics;
using System.Collections.Generic;
using OpenTK.Mathematics;
using IDKEngine.Render;

namespace IDKEngine
{
    class BLAS
    {
        public const int BITS_FOR_MISS_LINK = 10; // also adjust in shader
        public const int BITS_FOR_VERTICES_START = 31;

        public readonly GLSLNode[] Nodes;
        public readonly GLSLBLASVertex[] Vertices;
        public readonly int TreeDepth;
        public unsafe BLAS(ModelSystem modelSystem)
        {
            const int treeDepth = 5;
            if (treeDepth <= 1)
            {
                throw new ArgumentOutOfRangeException("Tree Depth must be at least 2");
            }

            TreeDepth = treeDepth;
            uint nodesPerMesh = (1u << treeDepth) - 1u;

            GLSLNode[] nodes = new GLSLNode[nodesPerMesh * modelSystem.Meshes.Length];

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

                int baseIndex = (int)(nodesPerMesh * i);
                modelSystem.Meshes[i].BaseIndex = baseIndex;

                GLSLNode root = new GLSLNode();
                root.Min = min;
                root.Max = max;
                SetMissLink(ref root, nodesPerMesh);
                nodes[baseIndex] = root;

                Build(root, baseIndex, start, end);

                // TODO: Set the misslinks programmatically so that it works for every tree depth
                SetMissLink(ref nodes[baseIndex + 1], 16u);
                SetMissLink(ref nodes[baseIndex + 2], 9u);
                SetMissLink(ref nodes[baseIndex + 3], 6u);
                SetMissLink(ref nodes[baseIndex + 4], 5u);
                SetMissLink(ref nodes[baseIndex + 5], 6u);
                SetMissLink(ref nodes[baseIndex + 6], 9u);
                SetMissLink(ref nodes[baseIndex + 7], 8u);
                SetMissLink(ref nodes[baseIndex + 8], 9u);
                SetMissLink(ref nodes[baseIndex + 9], 16u);
                SetMissLink(ref nodes[baseIndex + 10], 13u);
                SetMissLink(ref nodes[baseIndex + 11], 12u);
                SetMissLink(ref nodes[baseIndex + 12], 13u);
                SetMissLink(ref nodes[baseIndex + 13], 17u);
                SetMissLink(ref nodes[baseIndex + 14], 15u);
                SetMissLink(ref nodes[baseIndex + 15], 16u);
                SetMissLink(ref nodes[baseIndex + 16], nodesPerMesh);
                SetMissLink(ref nodes[baseIndex + 17], 24u);
                SetMissLink(ref nodes[baseIndex + 18], 21u);
                SetMissLink(ref nodes[baseIndex + 19], 20u);
                SetMissLink(ref nodes[baseIndex + 20], 21u);
                SetMissLink(ref nodes[baseIndex + 21], 24u);
                SetMissLink(ref nodes[baseIndex + 22], 23u);
                SetMissLink(ref nodes[baseIndex + 23], 24u);
                SetMissLink(ref nodes[baseIndex + 24], nodesPerMesh);
                SetMissLink(ref nodes[baseIndex + 25], 28u);
                SetMissLink(ref nodes[baseIndex + 26], 27u);
                SetMissLink(ref nodes[baseIndex + 27], 28u);
                SetMissLink(ref nodes[baseIndex + 28], nodesPerMesh);
                SetMissLink(ref nodes[baseIndex + 29], 30u);
                SetMissLink(ref nodes[baseIndex + 30], nodesPerMesh);
            }
            modelSystem.MeshBuffer.SubData(0, modelSystem.Meshes.Length * sizeof(GLSLMesh), modelSystem.Meshes);

            Nodes = nodes;
            Vertices = bvhVertices.ToArray();

            void Build(in GLSLNode root, int baseIndex, int start, int end)
            {
                BuildTree(root);

                void BuildTree(in GLSLNode node, int index = 0, int level = 0)
                {
                    if (level == (treeDepth - 1))
                        return;
                    ConstructChildBounds(node, out GLSLNode child0, out GLSLNode child1);

                    int newIndex = index + 1;
                    if (level == (treeDepth - 2))
                    {
                        MakeLeaf(ref child0);
                    }
                    //SetMissLink(ref child0, ???);
                    nodes[baseIndex + newIndex] = child0;
                    BuildTree(child0, newIndex, level + 1);


                    newIndex = GetRightChildIndex(index, treeDepth, level);
                    if (level == (treeDepth - 2))
                    {
                        MakeLeaf(ref child1);
                    }
                    //SetMissLink(ref child1, ???);
                    nodes[baseIndex + newIndex] = child1;
                    BuildTree(child1, newIndex, level + 1);
                }

                void MakeLeaf(ref GLSLNode node)
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
                    node.MissLinkAndVerticesCount = count;

                    MyMath.BitsInsert(ref node.IsLeafAndVerticesStart, 1, BITS_FOR_VERTICES_START, 1);
                }
            }

            void ConstructChildBounds(in GLSLNode parent, out GLSLNode child0, out GLSLNode child1)
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
        }

        private static void SetMissLink(ref GLSLNode node, uint missLink)
        {
            Debug.Assert(missLink < (1u << BITS_FOR_MISS_LINK));
            MyMath.BitsInsert(ref node.MissLinkAndVerticesCount, missLink, 32 - BITS_FOR_MISS_LINK, BITS_FOR_MISS_LINK);
        }

        private static int GetRightChildIndex(int parent, int treeDepth, int level)
        {
            return parent + (1 << (treeDepth - (level + 1)));
        }

        private static uint GetDistanceSibling(int level, uint allNodesCount)
        {
            return allNodesCount / (1u << level);
        }
    }
}
