using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    class BVH
    {
        public const uint BITS_FOR_MISS_LINK = 10u; // also adjust in shader

        public uint TreeDepth = 3;
        public readonly BufferObject BVHBuffer;
        public readonly BufferObject BVHVertexBuffer;
        public ModelSystem ModelSystem;
        public unsafe BVH(ModelSystem modelSystem)
        {
            if (TreeDepth == 0) return;

            uint nodesPerMesh = (1u << (int)TreeDepth) - 1u;
            List<GLSLBLASVertex> bvhVertices = new List<GLSLBLASVertex>(modelSystem.Vertices.Length);
            List<GLSLBLASVertex> expandedVertices = new List<GLSLBLASVertex>(modelSystem.Vertices.Length);
            GLSLNode[] nodes = new GLSLNode[nodesPerMesh * modelSystem.Meshes.Length];

            

            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                int start = expandedVertices.Count;
                for (int j = modelSystem.DrawCommands[i].FirstIndex; j < modelSystem.DrawCommands[i].FirstIndex + modelSystem.DrawCommands[i].Count; j++)
                {
                    uint indici = (uint)modelSystem.DrawCommands[i].BaseVertex + modelSystem.Indices[j];
                    GLSLBLASVertex vertex = *(GLSLBLASVertex*)((GLSLVertex*)modelSystem.Vertices.ToPtr() + indici);
                    min = Vector3.ComponentMin(min, vertex.Position);
                    max = Vector3.ComponentMax(max, vertex.Position);

                    expandedVertices.Add(vertex);
                }
                int end = expandedVertices.Count;

                modelSystem.Meshes[i].BaseIndex = (int)(nodesPerMesh * i);

                GLSLNode root = new GLSLNode();
                root.Min = min;
                root.Max = max;
                nodes[modelSystem.Meshes[i].BaseIndex + 0] = root;
                if (TreeDepth == 1)
                {
                    MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseIndex + 0], start, end);
                    SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 0], nodesPerMesh);
                    continue;
                }
                else
                {
                    SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 0], nodesPerMesh);
                }

                Tuple<GLSLNode, GLSLNode> childs = ConstructChildNodesBounds(root);
                nodes[modelSystem.Meshes[i].BaseIndex + 1] = childs.Item1;
                nodes[modelSystem.Meshes[i].BaseIndex + 4] = childs.Item2;
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 1], 4u);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 4], nodesPerMesh);

                childs = ConstructChildNodesBounds(nodes[modelSystem.Meshes[i].BaseIndex + 1]);
                nodes[modelSystem.Meshes[i].BaseIndex + 2] = childs.Item1;
                nodes[modelSystem.Meshes[i].BaseIndex + 3] = childs.Item2;
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseIndex + 2], start, end);
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseIndex + 3], start, end);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 2], 3u);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 3], 4u);

                childs = ConstructChildNodesBounds(nodes[modelSystem.Meshes[i].BaseIndex + 4]);
                nodes[modelSystem.Meshes[i].BaseIndex + 5] = childs.Item1;
                nodes[modelSystem.Meshes[i].BaseIndex + 6] = childs.Item2;
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseIndex + 5], start, end);
                MakeLeaf(ref nodes[modelSystem.Meshes[i].BaseIndex + 6], start, end);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 5], 6u);
                SetMissLink(ref nodes[modelSystem.Meshes[i].BaseIndex + 6], nodesPerMesh);
            }

            modelSystem.MeshBuffer.SubData(0, modelSystem.Meshes.Length * sizeof(GLSLMesh), modelSystem.Meshes);

            BVHBuffer = new BufferObject();
            BVHBuffer.ImmutableAllocate(Vector4.SizeInBytes + nodes.Length * sizeof(GLSLNode), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.SubData(Vector2.SizeInBytes, 2 * sizeof(uint), new uint[] { TreeDepth, BITS_FOR_MISS_LINK });
            BVHBuffer.SubData(Vector4.SizeInBytes, nodes.Length * sizeof(GLSLNode), nodes);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);

            BVHVertexBuffer = new BufferObject();
            BVHVertexBuffer.ImmutableAllocate(bvhVertices.Count * sizeof(GLSLBLASVertex), bvhVertices.ToArray(), BufferStorageFlags.DynamicStorageBit);
            BVHVertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, BVHVertexBuffer.Size);

            ModelSystem = modelSystem;

            void MakeLeaf(ref GLSLNode node, int start, int end)
            {
                Debug.Assert(bvhVertices.Count < MathF.Pow(2, 31)); // only 31 bits because one is used as a marker for isLeaf
                node.IsLeafAndVerticesStart = (uint)bvhVertices.Count;

                Vector3 center = (node.Min + node.Max) * 0.5f;
                Vector3 size = (node.Max - node.Min) * 0.5f;
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
        }

        private static uint GetDistanceSibling(int level, uint allNodesCount)
        {
            return allNodesCount / (1u << level);
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
