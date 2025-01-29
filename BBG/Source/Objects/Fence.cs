using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public record struct Fence : IDisposable
        {
            public enum Status : uint
            {
                AlreadySignaled = SyncStatus.AlreadySignaled,
                TimeoutExpired = SyncStatus.TimeoutExpired,
                ConditionSatisfied = SyncStatus.ConditionSatisfied,
                WaitFailed = SyncStatus.WaitFailed,
            }

            private readonly GLSync glFence;

            public static Fence InsertIntoCommandStream()
            {
                Fence fence = new Fence(GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, SyncBehaviorFlags.None));
                return fence;
            }

            public Fence(GLSync glSync)
            {
                glFence = glSync;
            }

            public readonly bool TryWait(ulong timeout = 1_000_000_000) // 1s in nanoseconds
            {
                return TryWait(out Status _, timeout);
            }

            public readonly bool TryWait(out Status status, ulong timeout = 1_000_000_000) // 1s in nanoseconds
            {
                status = (Status)GL.ClientWaitSync(glFence, SyncObjectMask.SyncFlushCommandsBit, timeout);
                
                if (status == Status.TimeoutExpired || status == Status.WaitFailed)
                {
                    return false;
                }
                return true;
            }

            public readonly void Dispose()
            {
                GL.DeleteSync(glFence);
            }
        }
    }
}
