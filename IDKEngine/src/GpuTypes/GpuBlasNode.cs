using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
	public struct GpuBlasNode
	{
		public Vector3 Min;
		public uint TriStartOrLeftChild;
		public Vector3 Max;
		public uint TriCount;

		public bool IsLeaf()
		{
			return TriCount > 0;
		}
	}
}
