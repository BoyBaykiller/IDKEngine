using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render;
using IDKEngine.Render.Objects;

namespace IDKEngine
{
    struct GLSLNode
    {
        public Vector3 Min;
        public int VerticesStart;
        public Vector3 Max;
        public int VerticesEnd;
	}

    class BVH
    {
        public ModelSystem ModelSystem;
        public BufferObject BVHBuffer;
        public BufferObject VertexBuffer;
        private readonly List<GLSLVertex> bvhVertices;
        public unsafe BVH(ModelSystem modelSystem)
        {
            const uint TREE_DEPTH = 1;
			if (TREE_DEPTH == 0) return;
			ModelSystem = modelSystem;

            /// Expand elementBuffer + vertexBuffer to single vertexBuffer
            List<GLSLVertex> expandedVertices = new List<GLSLVertex>(modelSystem.Vertices.Length);
            List<GLSLNode> nodes = new List<GLSLNode>();
            bvhVertices = new List<GLSLVertex>(expandedVertices.Count);
            
			for (int i = 0; i < modelSystem.Meshes.Length; i++)
            {
				Vector3 min = new Vector3(float.MaxValue);
				Vector3 max = new Vector3(float.MinValue);
				int start = expandedVertices.Count;
				for (int j = modelSystem.DrawCommands[i].FirstIndex; j < modelSystem.DrawCommands[i].FirstIndex + modelSystem.DrawCommands[i].Count; j++) // j is index into indices buffer which we use to index the vertices
                {
					GLSLVertex vertex = modelSystem.Vertices[modelSystem.DrawCommands[i].BaseVertex + modelSystem.Indices[j]];
					min.X = MathF.Min(min.X, vertex.Position.X);
					min.Y = MathF.Min(min.Y, vertex.Position.Y);
					min.Z = MathF.Min(min.Z, vertex.Position.Z);

					max.X = MathF.Max(max.X, vertex.Position.X);
					max.Y = MathF.Max(max.Y, vertex.Position.Y);
					max.Z = MathF.Max(max.Z, vertex.Position.Z);

					expandedVertices.Add(vertex);
                }
				int end = expandedVertices.Count;


				modelSystem.Meshes[i].BaseNode = nodes.Count;

				GLSLNode root = new GLSLNode();
				root.Min = min;
				root.Max = max;
				bool isLeaf = TREE_DEPTH == 1;
				// TODO: Implement stackless bvh
				if (isLeaf)
                {
					PopulateLeaf(start, end, ref root);
                }
				else
                {
					root.VerticesEnd = -1;
                }
				nodes.Add(root);
			}
            modelSystem.MeshBuffer.SubData(0, modelSystem.Meshes.Length * sizeof(GLSLMesh), modelSystem.Meshes);


			VertexBuffer = new BufferObject();
            VertexBuffer.ImmutableAllocate(bvhVertices.Count * sizeof(GLSLVertex), bvhVertices.ToArray(), BufferStorageFlags.DynamicStorageBit);

            BVHBuffer = new BufferObject();
            BVHBuffer.ImmutableAllocate(nodes.Count * sizeof(GLSLNode), nodes.ToArray(), BufferStorageFlags.DynamicStorageBit);
            BVHBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 1, 0, BVHBuffer.Size);
			VertexBuffer.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer, 5, 0, VertexBuffer.Size);

			void PopulateLeaf(int start, int end, ref GLSLNode node)
            {
				node.VerticesStart = bvhVertices.Count;

				Vector3 center = (node.Min + node.Max) * 0.5f;
				Vector3 size = node.Max - node.Min;
				for (int i = start; i < end; i += 3)
				{
					if (TriangleVSBox(expandedVertices[i + 0].Position, expandedVertices[i + 1].Position, expandedVertices[i + 2].Position, center, size))
					{
						bvhVertices.Add(expandedVertices[i + 0]);
						bvhVertices.Add(expandedVertices[i + 1]);
						bvhVertices.Add(expandedVertices[i + 2]);
					}
				}
				node.VerticesEnd = bvhVertices.Count;
			}
		}

		private static float fmin(float a, float b, float c)
		{
			return MathF.Min(a, MathF.Min(b, c));
		}

		private static float fmax(float a, float b, float c)
		{
			return MathF.Max(a, MathF.Max(b, c));
		}

		public static int GetNumNodesOnLevel(int level) => (int)MathF.Pow(2, level);
		public static int GetIndex(int level, int node) => GetNumNodesOnLevel(level) + node - 1;
		public static int GetLevel(int index) => (int)MathF.Log2(index + 1);
		public static int GetIndexChild0(int index) => 2 * index + 1;
		public static int GetIndexParent(int index) => (index - 1) / 2;

		private static Tuple<GLSLNode, GLSLNode> ConstructChildNodesBounds(in GLSLNode parent)
		{
			GLSLNode child0 = new GLSLNode();
			GLSLNode child1 = new GLSLNode();

			child0.Min = parent.Min;
			child0.Max = parent.Max;
			child1.Min = parent.Min;
			child1.Max = parent.Max;

			Vector3 parentNodeSize = parent.Max - parent.Min;
			if (parentNodeSize.X >= parentNodeSize.Y)
			{
				child0.Max.X -= parentNodeSize.X / 2.0f;
				child1.Min.X += parentNodeSize.X / 2.0f;
			}
			else if (parentNodeSize.Y >= parentNodeSize.Z)
			{
				child0.Max.Y -= parentNodeSize.Y / 2.0f;
				child1.Min.Y += parentNodeSize.Y / 2.0f;
			}
			else
			{
				child0.Max.Z -= parentNodeSize.Z / 2.0f;
				child1.Min.Z += parentNodeSize.Z / 2.0f;
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
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a01
			var a01 = new Vector3(0, -f1.Z, f1.Y);
			p0 = Vector3.Dot(v0, a01);
			p1 = Vector3.Dot(v1, a01);
			p2 = Vector3.Dot(v2, a01);
			r = boxExtents.Y * Math.Abs(f1.Z) + boxExtents.Z * Math.Abs(f1.Y);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a02
			var a02 = new Vector3(0, -f2.Z, f2.Y);
			p0 = Vector3.Dot(v0, a02);
			p1 = Vector3.Dot(v1, a02);
			p2 = Vector3.Dot(v2, a02);
			r = boxExtents.Y * Math.Abs(f2.Z) + boxExtents.Z * Math.Abs(f2.Y);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a10
			var a10 = new Vector3(f0.Z, 0, -f0.X);
			p0 = Vector3.Dot(v0, a10);
			p1 = Vector3.Dot(v1, a10);
			p2 = Vector3.Dot(v2, a10);
			r = boxExtents.X * Math.Abs(f0.Z) + boxExtents.Z * Math.Abs(f0.X);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a11
			var a11 = new Vector3(f1.Z, 0, -f1.X);
			p0 = Vector3.Dot(v0, a11);
			p1 = Vector3.Dot(v1, a11);
			p2 = Vector3.Dot(v2, a11);
			r = boxExtents.X * Math.Abs(f1.Z) + boxExtents.Z * Math.Abs(f1.X);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a12
			var a12 = new Vector3(f2.Z, 0, -f2.X);
			p0 = Vector3.Dot(v0, a12);
			p1 = Vector3.Dot(v1, a12);
			p2 = Vector3.Dot(v2, a12);
			r = boxExtents.X * Math.Abs(f2.Z) + boxExtents.Z * Math.Abs(f2.X);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a20
			var a20 = new Vector3(-f0.Y, f0.X, 0);
			p0 = Vector3.Dot(v0, a20);
			p1 = Vector3.Dot(v1, a20);
			p2 = Vector3.Dot(v2, a20);
			r = boxExtents.X * Math.Abs(f0.Y) + boxExtents.Y * Math.Abs(f0.X);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a21
			var a21 = new Vector3(-f1.Y, f1.X, 0);
			p0 = Vector3.Dot(v0, a21);
			p1 = Vector3.Dot(v1, a21);
			p2 = Vector3.Dot(v2, a21);
			r = boxExtents.X * Math.Abs(f1.Y) + boxExtents.Y * Math.Abs(f1.X);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			// Test axis a22
			var a22 = new Vector3(-f2.Y, f2.X, 0);
			p0 = Vector3.Dot(v0, a22);
			p1 = Vector3.Dot(v1, a22);
			p2 = Vector3.Dot(v2, a22);
			r = boxExtents.X * Math.Abs(f2.Y) + boxExtents.Y * Math.Abs(f2.X);
			if (Math.Max(-fmax(p0, p1, p2), fmin(p0, p1, p2)) > r)
			{
				return false;
			}

			#endregion

			#region Test the three axes corresponding to the face normals of AABB b (category 1)

			// Exit if...
			// ... [-extents.x, extents.x] and [min(v0.x,v1.x,v2.x), max(v0.x,v1.x,v2.x)] do not overlap
			if (fmax(v0.X, v1.X, v2.X) < -boxExtents.X || fmin(v0.X, v1.X, v2.X) > boxExtents.X)
			{
				return false;
			}

			// ... [-extents.y, extents.y] and [min(v0.y,v1.y,v2.y), max(v0.y,v1.y,v2.y)] do not overlap
			if (fmax(v0.Y, v1.Y, v2.Y) < -boxExtents.Y || fmin(v0.Y, v1.Y, v2.Y) > boxExtents.Y)
			{
				return false;
			}

			// ... [-extents.z, extents.z] and [min(v0.z,v1.z,v2.z), max(v0.z,v1.z,v2.z)] do not overlap
			if (fmax(v0.Z, v1.Z, v2.Z) < -boxExtents.Z || fmin(v0.Z, v1.Z, v2.Z) > boxExtents.Z)
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
		}
	}
}
