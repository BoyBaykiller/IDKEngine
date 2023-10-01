using OpenTK.Mathematics;

namespace IDKEngine
{
	struct GpuBlasNode
	{
		public Vector3 Min;
		public uint TriStartOrLeftChild;
		public Vector3 Max;
		public uint TriCount;
	}
}
