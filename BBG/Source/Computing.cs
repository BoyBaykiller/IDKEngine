using OpenTK.Graphics.OpenGL;

namespace BBOpenGL;

public static partial class BBG
{
    public static class Computing
    {
        public static void Compute(string computePassName, Action funcCompute)
        {
            Debugging.PushDebugGroup(computePassName);

            funcCompute();

            Debugging.PopDebugGroup();
        }

        public static void Dispatch(int x, int y, int z)
        {
            if (x == 0 || y == 0 || z == 0) return;
            GL.DispatchCompute((uint)x, (uint)y, (uint)z);
        }

        public static void DispatchIndirect(Buffer commandBuffer, nint offset = 0)
        {
            GL.BindBuffer(BufferTarget.DispatchIndirectBuffer, commandBuffer.ID);
            GL.DispatchComputeIndirect(offset);
        }
    }
}
