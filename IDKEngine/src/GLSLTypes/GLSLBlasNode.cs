using OpenTK.Mathematics;

namespace IDKEngine
{
	struct GLSLBlasNode
	{
		public Vector3 Min;
		public uint VerticesStart;
		public Vector3 Max;
		public uint VertexCount;
		private readonly Vector3 _pad0;
		public uint MissLink;
	}
}
