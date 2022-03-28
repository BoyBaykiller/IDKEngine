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
        public readonly BufferObject TraverseVertexBuffer;
        public ModelSystem ModelSystem;
        public unsafe BVH(ModelSystem modelSystem)
        {
            if (TreeDepth == 0) return;

            uint nodesPerMesh = (uint)MathF.Pow(2u, TreeDepth) - 1;
            List<GLSLTraverseVertex> expandedTraverseVertices = new List<GLSLTraverseVertex>(modelSystem.Vertices.Length);
            List<GLSLTraverseVertex> alignedTraverseVertices = new List<GLSLTraverseVertex>(expandedTraverseVertices.Count);
            GLSLBVHVertex[] bvhVertecis = new GLSLBVHVertex[modelSystem.Vertices.Length];
            GLSLNode[] nodes = new GLSLNode[nodesPerMesh * modelSystem.Meshes.Length];

            for (int i = 0; i < modelSystem.Vertices.Length; i++)
            {
                bvhVertecis[i].TexCoord = modelSystem.Vertices[i].TexCoord;
                bvhVertecis[i].Normal = modelSystem.Vertices[i].Normal;
                bvhVertecis[i].Tangent = modelSystem.Vertices[i].Tangent;
            }

            for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                int start = expandedTraverseVertices.Count;
                for (int j = modelSystem.DrawCommands[i].FirstIndex; j < modelSystem.DrawCommands[i].FirstIndex + modelSystem.DrawCommands[i].Count; j++)
                {
                    GLSLTraverseVertex vertex = new GLSLTraverseVertex();
                    uint indici = (uint)modelSystem.DrawCommands[i].BaseVertex + modelSystem.Indices[j];
                    vertex.Position = modelSystem.Vertices[indici].Position;
                    vertex.BVHVertexIndex = indici;

                    min.X = MathF.Min(min.X, vertex.Position.X);
                    min.Y = MathF.Min(min.Y, vertex.Position.Y);
                    min.Z = MathF.Min(min.Z, vertex.Position.Z);

                    max.X = MathF.Max(max.X, vertex.Position.X);
                    max.Y = MathF.Max(max.Y, vertex.Position.Y);
                    max.Z = MathF.Max(max.Z, vertex.Position.Z);

                    expandedTraverseVertices.Add(vertex);
                }
                int end = expandedTraverseVertices.Count;

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
            BVHVertexBuffer.ImmutableAllocate(bvhVertecis.Length * sizeof(GLSLBVHVertex), bvhVertecis, BufferStorageFlags.DynamicStorageBit);
            BVHVertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 3, 0, BVHVertexBuffer.Size);

            TraverseVertexBuffer = new BufferObject();
            TraverseVertexBuffer.ImmutableAllocate(alignedTraverseVertices.Count * sizeof(GLSLTraverseVertex), alignedTraverseVertices.ToArray(), BufferStorageFlags.DynamicStorageBit);
            TraverseVertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 4, 0, TraverseVertexBuffer.Size);

            ModelSystem = modelSystem;

            void MakeLeaf(ref GLSLNode node, int start, int end)
            {
                Debug.Assert(alignedTraverseVertices.Count < MathF.Pow(2, 31)); // only 31 bits because one is used as a marker for isLeaf
                node.IsLeafAndVerticesStart = (uint)alignedTraverseVertices.Count;

                Vector3 center = (node.Min + node.Max) * 0.5f;
                Vector3 size = node.Max - node.Min;
                for (int i = start; i < end; i += 3)
                {
                    if (MyMath.TriangleVSBox(expandedTraverseVertices[i + 0].Position, expandedTraverseVertices[i + 1].Position, expandedTraverseVertices[i + 2].Position, center, size))
                    {
                        alignedTraverseVertices.Add(expandedTraverseVertices[i + 0]);
                        alignedTraverseVertices.Add(expandedTraverseVertices[i + 1]);
                        alignedTraverseVertices.Add(expandedTraverseVertices[i + 2]);
                    }
                }
                uint count = (uint)alignedTraverseVertices.Count - node.IsLeafAndVerticesStart;
                Debug.Assert(count < MathF.Pow(2, 32 - (int)BITS_FOR_MISS_LINK));

                node.MissLinkAndVerticesCount = count;
                MyMath.BitsInsert(ref node.IsLeafAndVerticesStart, 1, 31, 1);
            }
        }

        private static uint GetRightChildIndex(uint parent, uint treeDepth, uint level)
        {
            return parent + (uint)MathF.Pow(2u, treeDepth - level);
        }
        private static uint GetLeftChildIndex(uint index)
        {
            return index + 1;
        }
        private static uint GetAdjacentLeafDistance(uint level, uint treeDepth)
        {
            uint result = treeDepth - 1u;
            for (uint i = treeDepth - 2; i >= level; i--)
            {
                result = result * 2u - i;
            }
            return result;
        }

        private static void SetMissLink(ref GLSLNode node, uint missLink)
        {
            Debug.Assert(missLink < MathF.Pow(2, BITS_FOR_MISS_LINK));
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
