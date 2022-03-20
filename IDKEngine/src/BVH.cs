using System;
using System.Diagnostics;
using System.Collections.Generic;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

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
					if (TriangleVSBox(expandedTraverseVertices[i + 0].Position, expandedTraverseVertices[i + 1].Position, expandedTraverseVertices[i + 2].Position, center, size))
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
			return ++index;
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
		private static bool TriangleVSBox(Vector3 a, Vector3 b, Vector3 c, Vector3 boxCenter, Vector3 boxExtents)
		{
			// From the book "Real-Time Collision Detection" by Christer Ericson, page 169
			// See also the published Errata at http://realtimecollisiondetection.net/books/rtcd/errata/

			// Translate triangle as conceptually moving AABB to origin
			var v0 = (a - boxCenter);
			var v1 = (b - boxCenter);
			var v2 = (c - boxCenter);

			// Compute edge vectors for triangle
			var f0 = (v1 - v0);
			var f1 = (v2 - v1);
			var f2 = (v0 - v2);

			#region Test axes a00..a22 (category 3)

			// Test axis a00
			var a00 = new Vector3(0, -f0.Z, f0.Y);
			var p0 = Vector3.Dot(v0, a00);
			var p1 = Vector3.Dot(v1, a00);
			var p2 = Vector3.Dot(v2, a00);
			var r = boxExtents.Y * Math.Abs(f0.Z) + boxExtents.Z * Math.Abs(f0.Y);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a01
			var a01 = new Vector3(0, -f1.Z, f1.Y);
			p0 = Vector3.Dot(v0, a01);
			p1 = Vector3.Dot(v1, a01);
			p2 = Vector3.Dot(v2, a01);
			r = boxExtents.Y * Math.Abs(f1.Z) + boxExtents.Z * Math.Abs(f1.Y);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a02
			var a02 = new Vector3(0, -f2.Z, f2.Y);
			p0 = Vector3.Dot(v0, a02);
			p1 = Vector3.Dot(v1, a02);
			p2 = Vector3.Dot(v2, a02);
			r = boxExtents.Y * Math.Abs(f2.Z) + boxExtents.Z * Math.Abs(f2.Y);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a10
			var a10 = new Vector3(f0.Z, 0, -f0.X);
			p0 = Vector3.Dot(v0, a10);
			p1 = Vector3.Dot(v1, a10);
			p2 = Vector3.Dot(v2, a10);
			r = boxExtents.X * Math.Abs(f0.Z) + boxExtents.Z * Math.Abs(f0.X);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a11
			var a11 = new Vector3(f1.Z, 0, -f1.X);
			p0 = Vector3.Dot(v0, a11);
			p1 = Vector3.Dot(v1, a11);
			p2 = Vector3.Dot(v2, a11);
			r = boxExtents.X * Math.Abs(f1.Z) + boxExtents.Z * Math.Abs(f1.X);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a12
			var a12 = new Vector3(f2.Z, 0, -f2.X);
			p0 = Vector3.Dot(v0, a12);
			p1 = Vector3.Dot(v1, a12);
			p2 = Vector3.Dot(v2, a12);
			r = boxExtents.X * Math.Abs(f2.Z) + boxExtents.Z * Math.Abs(f2.X);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a20
			var a20 = new Vector3(-f0.Y, f0.X, 0);
			p0 = Vector3.Dot(v0, a20);
			p1 = Vector3.Dot(v1, a20);
			p2 = Vector3.Dot(v2, a20);
			r = boxExtents.X * Math.Abs(f0.Y) + boxExtents.Y * Math.Abs(f0.X);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a21
			var a21 = new Vector3(-f1.Y, f1.X, 0);
			p0 = Vector3.Dot(v0, a21);
			p1 = Vector3.Dot(v1, a21);
			p2 = Vector3.Dot(v2, a21);
			r = boxExtents.X * Math.Abs(f1.Y) + boxExtents.Y * Math.Abs(f1.X);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a22
			var a22 = new Vector3(-f2.Y, f2.X, 0);
			p0 = Vector3.Dot(v0, a22);
			p1 = Vector3.Dot(v1, a22);
			p2 = Vector3.Dot(v2, a22);
			r = boxExtents.X * Math.Abs(f2.Y) + boxExtents.Y * Math.Abs(f2.X);
			if (Math.Max(-Fmax(p0, p1, p2), Fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			#endregion

			#region Test the three axes corresponding to the face normals of AABB b (category 1)

			// Exit if...
			// ... [-extents.x, extents.x] and [min(v0.x,v1.x,v2.x), max(v0.x,v1.x,v2.x)] do not overlap
			if (Fmax(v0.X, v1.X, v2.X) < -boxExtents.X || Fmin(v0.X, v1.X, v2.X) > boxExtents.X)
			{
				return false;
			}

			// ... [-extents.y, extents.y] and [min(v0.y,v1.y,v2.y), max(v0.y,v1.y,v2.y)] do not overlap
			if (Fmax(v0.Y, v1.Y, v2.Y) < -boxExtents.Y || Fmin(v0.Y, v1.Y, v2.Y) > boxExtents.Y)
			{
				return false;
			}

			// ... [-extents.z, extents.z] and [min(v0.z,v1.z,v2.z), max(v0.z,v1.z,v2.z)] do not overlap
			if (Fmax(v0.Z, v1.Z, v2.Z) < -boxExtents.Z || Fmin(v0.Z, v1.Z, v2.Z) > boxExtents.Z)
			{
				return false;
			}

			#endregion

			#region Test separating axis corresponding to triangle face normal (category 2)

			var planeNormal = Vector3.Cross(f0, f1);
			var planeDistance = Vector3.Dot(planeNormal, v0);

			// Compute the projection interval radius of b onto L(t) = b.c + t * p.n
			r = boxExtents.X * Math.Abs(planeNormal.X) + boxExtents.Y * Math.Abs(planeNormal.Y) + boxExtents.Z * Math.Abs(planeNormal.Z);

			// Intersection occurs when plane distance falls within [-r,+r] interval
			if (planeDistance > r)
			{
				return false;
			}

			#endregion

			return true;

			static float Fmin(float a, float b, float c)
			{
				return MathF.Min(a, MathF.Min(b, c));
			}
			static float Fmax(float a, float b, float c)
			{
				return MathF.Max(a, MathF.Max(b, c));
			}
		}
	}
}
