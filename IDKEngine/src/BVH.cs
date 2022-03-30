using IDKEngine.Render;
using IDKEngine.Render.Objects;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IDKEngine
{
    class BVH
    {
        public const uint BITS_FOR_MISS_LINK = 10u; // also adjust in PathTracing/compute.glsl

        public uint TreeDepth = 3;
        public readonly BufferObject BVHBuffer;
        public readonly BufferObject BVHVertexBuffer;
        public ModelSystem ModelSystem;
        public unsafe BVH(ModelSystem modelSystem)
        {
            if (TreeDepth == 0) return;

            uint nodesPerMesh = (1u << (int)TreeDepth) - 1u;
            List<GLSLBVHVertex> bvhVertices = new List<GLSLBVHVertex>(modelSystem.Vertices.Length);
            List<GLSLBVHVertex> expandedVertices = new List<GLSLBVHVertex>(modelSystem.Vertices.Length);
            GLSLNode[] nodes = new GLSLNode[nodesPerMesh * modelSystem.Meshes.Length];

            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                int start = expandedVertices.Count;
                for (int j = modelSystem.DrawCommands[i].FirstIndex; j < modelSystem.DrawCommands[i].FirstIndex + modelSystem.DrawCommands[i].Count; j++)
                {
                    uint indici = (uint)modelSystem.DrawCommands[i].BaseVertex + modelSystem.Indices[j];
                    GLSLBVHVertex vertex = new GLSLBVHVertex();
                    vertex.Position = modelSystem.Vertices[indici].Position;
                    vertex.TexCoord = modelSystem.Vertices[indici].TexCoord;
                    vertex.Normal = modelSystem.Vertices[indici].Normal;
                    vertex.Tangent = modelSystem.Vertices[indici].Tangent;

                    min.X = MathF.Min(min.X, vertex.Position.X);
                    min.Y = MathF.Min(min.Y, vertex.Position.Y);
                    min.Z = MathF.Min(min.Z, vertex.Position.Z);

                    max.X = MathF.Max(max.X, vertex.Position.X);
                    max.Y = MathF.Max(max.Y, vertex.Position.Y);
                    max.Z = MathF.Max(max.Z, vertex.Position.Z);

                    expandedVertices.Add(vertex);
                }
                int end = expandedVertices.Count;

                modelSystem.Meshes[i].BaseNode = (int)(nodesPerMesh * i);

                GLSLNode root = new GLSLNode();
                root.Min = min;
                root.Max = max;
                nodes[modelSystem.Meshes[i].BaseNode + 0] = root;
                if (TreeDepth == 1)
                {
                    MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseNode + 0], start, end);
                    SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 0], nodesPerMesh);
                    continue;
                }
                else
                {
                    SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 0], nodesPerMesh);
                }

                Tuple<GLSLNode, GLSLNode> childs = ConstructChildNodesBounds(root);
                nodes[modelSystem.Meshes[i].BaseNode + 1] = childs.Item1;
                nodes[modelSystem.Meshes[i].BaseNode + 4] = childs.Item2;
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 1], 4u);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 4], nodesPerMesh);

                childs = ConstructChildNodesBounds(nodes[modelSystem.Meshes[i].BaseNode + 1]);
                nodes[modelSystem.Meshes[i].BaseNode + 2] = childs.Item1;
                nodes[modelSystem.Meshes[i].BaseNode + 3] = childs.Item2;
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseNode + 2], start, end);
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseNode + 3], start, end);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 2], 3u);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 3], 4u);

                childs = ConstructChildNodesBounds(nodes[modelSystem.Meshes[i].BaseNode + 4]);
                nodes[modelSystem.Meshes[i].BaseNode + 5] = childs.Item1;
                nodes[modelSystem.Meshes[i].BaseNode + 6] = childs.Item2;
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseNode + 5], start, end);
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseNode + 6], start, end);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 5], 6u);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseNode + 6], nodesPerMesh);
            }
            modelSystem.MeshBuffer.SubData(0, modelSystem.Meshes.Length * sizeof(GLSLMesh), modelSystem.Meshes);

            BVHBuffer = new BufferObject();
            BVHBuffer.ImmutableAllocate(Vector4.SizeInBytes + nodes.Length * sizeof(GLSLNode), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.SubData(Vector2.SizeInBytes, 2 * sizeof(uint), new uint[] { TreeDepth, BITS_FOR_MISS_LINK });
            BVHBuffer.SubData(Vector4.SizeInBytes, nodes.Length * sizeof(GLSLNode), nodes);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);

            BVHVertexBuffer = new BufferObject();
            BVHVertexBuffer.ImmutableAllocate(bvhVertices.Count * sizeof(GLSLBVHVertex), bvhVertices.ToArray(), BufferStorageFlags.DynamicStorageBit);
            BVHVertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, BVHVertexBuffer.Size);

            ModelSystem = modelSystem;

            void MakeLeaf(ref GLSLNode node, int start, int end)
            {
                Debug.Assert(bvhVertices.Count < MathF.Pow(2, 31)); // only 31 bits because one is used as a marker for isLeaf
                node.IsLeafAndVerticesStart = (uint)bvhVertices.Count;

                Vector3 center = (node.Min + node.Max) * 0.5f;
                Vector3 size = node.Max - node.Min;
                for (int i = start; i < end; i += 3)
                {
                    if (MyMath.TriangleVSBox(expandedVertices[i + 0].Position, expandedVertices[i + 1].Position, expandedVertices[i + 2].Position, center, size))
                    {
                        bvhVertices.Add(expandedVertices[i + 0]);
                        bvhVertices.Add(expandedVertices[i + 1]);
                        bvhVertices.Add(expandedVertices[i + 2]);
                    }
                }
                uint count = (uint)bvhVertices.Count - node.IsLeafAndVerticesStart;
                Debug.Assert(count < (1u << (32 - (int)BITS_FOR_MISS_LINK)));

                node.MissLinkAndVerticesCount = count;
                MyMath.BitsInsert(ref node.IsLeafAndVerticesStart, 1, 31, 1);
            }

            Console.WriteLine(GetNearSiblingRight(1, 1, 31)); // 1 -> 16
            //Console.WriteLine(GetAdjacentSibling(1, 1, 5)); // 1 -> 16
            
            Console.WriteLine("=========");

            Console.WriteLine(GetNearSiblingRight(2, 2, 31)); // 2 -> 9
            Console.WriteLine(GetAdjacentSibling(9, 2, 5)); // 9 -> 17
            Console.WriteLine(GetNearSiblingRight(17, 2, 31)); // 17 -> 24

            Console.WriteLine("=========");

            Console.WriteLine(GetNearSiblingRight(3, 3, 31)); // 3 -> 6
            Console.WriteLine(GetAdjacentSibling(6, 2, 4)); // 6 -> 10
            Console.WriteLine(GetNearSiblingRight(10, 3, 31)); // 10 -> 13
            Console.WriteLine(GetAdjacentSibling(13, 3, 5)); // 13 -> 18
            Console.WriteLine(GetNearSiblingRight(18, 3, 31)); // 18 -> 21
            Console.WriteLine(GetAdjacentSibling(21, 2, 4)); // 21 -> 25
            Console.WriteLine(GetNearSiblingRight(25, 3, 31)); // 25 -> 28

            Console.WriteLine("=========");
            for (int i = 0; i < TreeDepth; i++)
            {
                uint index = (uint)i;
                for (int j = 1; j < NodesOnLevel(i); j++)
                {
                    if (j % 2 == 0)
                    {
                        // FIX: Find correct arguments lol
                        uint newIndex = GetAdjacentSibling(index, 1, 4);
                    }
                    else
                    {
                        uint newIndex = GetNearSiblingRight(index, i, nodesPerMesh);
                    }
                }
            }
        }

        private static uint GetRightChildIndex(uint parent, int treeDepth, int level)
        {
            return parent + (1u << (treeDepth - level));
        }
        private static uint GetNearSiblingRight(uint index, int level, uint allNodesCount)
        {
            return index + (allNodesCount / (1u << level));
        }
        private static uint GetAdjacentSibling(uint index, uint level, uint treeDepth)
        {
            uint result = treeDepth - 1u;
            for (uint i = treeDepth - 2; i >= level; i--)
            {
                result = result * 2u - i;
            }
            return index + result;
        }
        private static uint NodesOnLevel(int level)
        {
            return 1u << level;
        }

        private static void SetMissLink(ref GLSLNode node, uint missLink)
        {
            Debug.Assert(missLink < (1u << (int)BITS_FOR_MISS_LINK));
            MyMath.BitsInsert(ref node.MissLinkAndVerticesCount, missLink, 32 - (int)BITS_FOR_MISS_LINK, (int)BITS_FOR_MISS_LINK);
        }
        private static Tuple<GLSLNode, GLSLNode> ConstructChildNodesBounds(in GLSLNode parent)
        {
            GLSLNode child0 = new GLSLNode();
            GLSLNode child1 = new GLSLNode();

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

            return new Tuple<GLSLNode, GLSLNode>(child0, child1);
        }
    }
}
