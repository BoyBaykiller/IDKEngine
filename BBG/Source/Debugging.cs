using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public static class Debugging
        {
            // False by default because of AMD driver bug: https://gist.github.com/BoyBaykiller/4918880dd86a9f544c3254479b6d6190
            public static bool EnableDebugGroups = false;

            public enum DebugSource : uint
            {
                DontCare = OpenTK.Graphics.OpenGL.DebugSource.DontCare,
                Api = OpenTK.Graphics.OpenGL.DebugSource.DebugSourceApi,
                WindowSystem = OpenTK.Graphics.OpenGL.DebugSource.DebugSourceWindowSystem,
                ShaderCompiler = OpenTK.Graphics.OpenGL.DebugSource.DebugSourceShaderCompiler,
                ThirdParty = OpenTK.Graphics.OpenGL.DebugSource.DebugSourceThirdParty,
                Other = OpenTK.Graphics.OpenGL.DebugSource.DebugSourceOther,
            }

            public enum DebugType : uint
            {
                DontCare = OpenTK.Graphics.OpenGL.DebugType.DontCare,
                Error = OpenTK.Graphics.OpenGL.DebugType.DebugTypeError,
                DeprecatedBehavior = OpenTK.Graphics.OpenGL.DebugType.DebugTypeDeprecatedBehavior,
                UndefinedBehavior = OpenTK.Graphics.OpenGL.DebugType.DebugTypeUndefinedBehavior,
                Portability = OpenTK.Graphics.OpenGL.DebugType.DebugTypePortability,
                Performance = OpenTK.Graphics.OpenGL.DebugType.DebugTypePerformance,
                Other = OpenTK.Graphics.OpenGL.DebugType.DebugTypeOther,
            }

            public enum DebugSeverity : uint
            {
                DontCare = OpenTK.Graphics.OpenGL.DebugSeverity.DontCare,
                Notification = OpenTK.Graphics.OpenGL.DebugSeverity.DebugSeverityNotification,
                High = OpenTK.Graphics.OpenGL.DebugSeverity.DebugSeverityHigh,
                Medium = OpenTK.Graphics.OpenGL.DebugSeverity.DebugSeverityMedium,
                Low = OpenTK.Graphics.OpenGL.DebugSeverity.DebugSeverityLow,
            }

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

            public static void PushDebugGroup(string message)
            {
                if (EnableDebugGroups)
                {
                    GL.PushDebugGroup(OpenTK.Graphics.OpenGL.DebugSource.DebugSourceApplication, DEBUG_GROUP_MESSAGE_ID, message.Length, message);
                }
            }

            public static void PopDebugGroup()
            {
                if (EnableDebugGroups)
                {
                    GL.PopDebugGroup();
                }
            }

            private static void GLDebugCallback(
                OpenTK.Graphics.OpenGL.DebugSource source,
                OpenTK.Graphics.OpenGL.DebugType type,
                uint id,
                OpenTK.Graphics.OpenGL.DebugSeverity severity,
                int length,
                IntPtr message,
                IntPtr userParam)
            {
                if (source == OpenTK.Graphics.OpenGL.DebugSource.DebugSourceApplication && id == DEBUG_GROUP_MESSAGE_ID)
                {
                    return;
                }

                string text = Marshal.PtrToStringAnsi(message, length);
                OpenGLDebugCallback?.Invoke((DebugSource)source, (DebugType)type, (DebugSeverity)severity, id, text);
            }
        }
    }
}
