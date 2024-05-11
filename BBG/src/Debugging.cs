using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public static class Debugging
        {
            private const uint DEBUG_GROUP_MESSAGE_ID = 0;

            public delegate void FuncOpenGLDebugCallback(DebugSource source, DebugType type, DebugSeverity severity, uint messageID, string message);
            public static event FuncOpenGLDebugCallback? OpenGLDebugCallback;

            private static bool _enableDebugCallback;
            public static bool EnableDebugCallback
            {
                get => _enableDebugCallback;

                set
                {
                    _enableDebugCallback = value;
                    if (_enableDebugCallback)
                    {
                        GL.Enable(EnableCap.DebugOutput);
                        GL.Enable(EnableCap.DebugOutputSynchronous);
                        GL.DebugMessageCallback(GLDebugCallback, IntPtr.Zero);
                    }
                    else
                    {
                        GL.Disable(EnableCap.DebugOutput);
                    }
                }
            }

            // False by default because of AMD driver bug: https://gist.github.com/BoyBaykiller/4918880dd86a9f544c3254479b6d6190
            public static bool EnableDebugGroups = false;

            internal static void PushDebugGroup(string message)
            {
                if (EnableDebugGroups)
                {
                    GL.PushDebugGroup(DebugSource.DebugSourceApplication, DEBUG_GROUP_MESSAGE_ID, message.Length, message);
                }
            }

            internal static void PopDebugGroup()
            {
                if (EnableDebugGroups)
                {
                    GL.PopDebugGroup();
                }
            }

            private static void GLDebugCallback(DebugSource source, DebugType type, uint id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
            {
                if (source == DebugSource.DebugSourceApplication && id == DEBUG_GROUP_MESSAGE_ID)
                {
                    return;
                }

                string text = Marshal.PtrToStringAnsi(message, length);
                OpenGLDebugCallback?.Invoke(source, type, severity, id, text);
            }
        }
    }
}
