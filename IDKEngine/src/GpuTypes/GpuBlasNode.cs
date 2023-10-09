using OpenTK.Mathematics;

namespace IDKEngine
{
	public struct GpuBlasNode
	{
		public Vector3 Min;
		public uint TriStartOrLeftChild;
		public Vector3 Max;
		public uint TriCount;
	}
}
