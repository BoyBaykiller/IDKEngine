using System;
using System.Diagnostics;
using OpenTK;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using OpenTK.Windowing.GraphicsLibraryFramework;
using BBLogger;

namespace IDKEngine.Windowing
{
    abstract unsafe class GameWindowBase : IDisposable, IBindingsContext
    {
        private string _title;
        public string WindowTitle
        {
            get => _title;

            set
            {
                _title = value;
                GLFW.SetWindowTitle(window, _title);
            }
        }

        private bool _isVSync;
        public bool WindowVSync
        {
            get => _isVSync;

            set
            {
                _isVSync = value;
                GLFW.SwapInterval(_isVSync ? 1 : 0);
            }
        }

        private bool _isFullscreen;
        public bool WindowFullscreen
        {
            get => _isFullscreen;

            set
            {
                _isFullscreen = value;
                if (_isFullscreen)
                {
                    GLFW.GetWindowPos(window, out cachedWindowPos.X, out cachedWindowPos.Y);
                    GLFW.GetWindowSize(window, out cachedWindowSize.X, out cachedWindowSize.Y);
                    GLFW.SetWindowMonitor(window, monitor, 0, 0, videoMode->Width, videoMode->Height, videoMode->RefreshRate);
                }
                else
                {
                    GLFW.SetWindowMonitor(window, null, cachedWindowPos.X, cachedWindowPos.Y, cachedWindowSize.X, cachedWindowSize.Y, 0);
                }
                WindowVSync = _isVSync;
            }
        }

        public float WindowTime { get; private set; }

        private Vector2i _framebufferSize;
        public Vector2i WindowFramebufferSize
        {
            get => _framebufferSize;

            set
            {
                GLFW.SetWindowSize(window, value.X, value.Y);
            }
        }

        private bool _isFocused = true;
        public bool WindowFocused => _isFocused;

        private Vector2i _position;
        public Vector2i WindowPosition
        {
            get => _position;

            set
            {
                GLFW.SetWindowPos(window, value.X, value.Y);
            }
        }

        public int WindowRefreshRate => videoMode->RefreshRate;

        public readonly Keyboard KeyboardState;
        public readonly Mouse MouseState;

        private Vector2i cachedWindowPos;
        private Vector2i cachedWindowSize;

        private static bool glfwInitialized;

        private readonly VideoMode* videoMode;
        private readonly Monitor* monitor;
        private readonly Window* window;

        /// <summary>
        /// Creates a window with OpenGL context
        /// </summary>
        public GameWindowBase(int width, int height, string title, int openglMajor, int openglMinor)
        {
            if (!glfwInitialized)
            {
                GLFW.Init();
                glfwInitialized = true;
            }

            GLFW.WindowHint(WindowHintBool.OpenGLDebugContext, true);
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, openglMajor);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, openglMinor);
            GLFW.WindowHint(WindowHintInt.Samples, 1);
            _title = title;
            _framebufferSize.X = width;
            _framebufferSize.Y = height;

            window = GLFW.CreateWindow(_framebufferSize.X, _framebufferSize.Y, _title, null, null);
            if (window == null)
            {
                Logger.Log(Logger.LogLevel.Fatal, $"Window creation failed. Make sure the primary GPU has OpenGL {openglMajor}.{openglMinor} support");
                Environment.Exit(0);
            }

            framebufferSizeFuncPtr = FramebufferSizeCallback;
            GLFW.SetFramebufferSizeCallback(window, framebufferSizeFuncPtr);

            windowFocusFuncPtr = WindowFocusCallback;
            GLFW.SetWindowFocusCallback(window, windowFocusFuncPtr);

            windowPosFuncPtr = WindowPosCallback;
            GLFW.SetWindowPosCallback(window, windowPosFuncPtr);

            windowCharFuncPtr = WindowCharCallback;
            GLFW.SetCharCallback(window, windowCharFuncPtr);

            windowDropCallback = WindowDropCallback;
            GLFW.SetDropCallback(window, windowDropCallback);

            if (GLFW.RawMouseMotionSupported())
            {
                GLFW.SetInputMode(window, RawMouseMotionAttribute.RawMouseMotion, true);
            }
            else
            {
                Logger.Log(Logger.LogLevel.Info, "Raw Mouse Motion is not supported");
            }

            monitor = GLFW.GetPrimaryMonitor();
            videoMode = GLFW.GetVideoMode(monitor);
            KeyboardState = new Keyboard(window);
            MouseState = new Mouse(window);
            WindowPosition = new Vector2i(videoMode->Width / 2 - _framebufferSize.X / 2, videoMode->Height / 2 - _framebufferSize.Y / 2);

            GLFW.MakeContextCurrent(window);
            OpenTK.Graphics.GLLoader.LoadBindings(this);

            {
                // Make window black (calling SwapBuffers here irritates diagnostic tools like renderdoc)
                Vector4 clearColor = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
                OpenTK.Graphics.OpenGL.GL.ClearNamedFramebufferf(0, OpenTK.Graphics.OpenGL.Buffer.Color, 0, ref clearColor.X);
                GLFW.SwapBuffers(window);
            }
        }

        public void Run()
        {
            OnStart();

            float maxDt = 1.0f / 5.0f;
            float frameTime = maxDt; // always run update on first iteration

            while (!GLFW.WindowShouldClose(window))
            {
                Stopwatch sw = Stopwatch.StartNew();

                float timeToSimulate = frameTime;
                while (timeToSimulate > 0.0f)
                {
                    float thisDt = Math.Min(timeToSimulate, maxDt);

                    KeyboardState.Update();
                    MouseState.Update();
                    OnUpdate(thisDt);

                    timeToSimulate -= thisDt;
                    WindowTime += thisDt;
                }


                OnRender(frameTime);
                frameTime = (float)sw.Elapsed.TotalSeconds;
            }
        }

        public static void PollEvents()
        {
            GLFW.PollEvents();
        }

        public void SwapBuffers()
        {
            GLFW.SwapBuffers(window);
        }

        public void ShouldClose()
        {
            GLFW.SetWindowShouldClose(window, true);
        }

        protected abstract void OnRender(float dT);
        protected abstract void OnUpdate(float dT);
        protected abstract void OnStart();
        protected abstract void OnWindowResize();
        protected abstract void OnKeyPress(uint key);
        protected abstract void OnFilesDrop(string[] paths);


        private readonly GLFWCallbacks.FramebufferSizeCallback framebufferSizeFuncPtr;
        private void FramebufferSizeCallback(Window* window, int width, int height)
        {
            if ((width > 0 && height > 0) && (_framebufferSize.X != width || _framebufferSize.Y != height))
            {
                _framebufferSize.X = width;
                _framebufferSize.Y = height;
                OnWindowResize();
            }
        }

        private readonly GLFWCallbacks.WindowFocusCallback windowFocusFuncPtr;
        private void WindowFocusCallback(Window* window, bool focused)
        {
            _isFocused = focused;
        }

        private readonly GLFWCallbacks.WindowPosCallback windowPosFuncPtr;
        private void WindowPosCallback(Window* window, int x, int y)
        {
            _position.X = x;
            _position.Y = y;
        }

        private readonly GLFWCallbacks.CharCallback windowCharFuncPtr;
        private void WindowCharCallback(Window* window, uint codepoint)
        {
            OnKeyPress(codepoint);
        }

        private readonly GLFWCallbacks.DropCallback windowDropCallback;
        private void WindowDropCallback(Window* window, int count, byte** paths)
        {
            string[] strings = new string[count];
            for (int i = 0; i < count; i++)
            {
                strings[i] = Marshal.PtrToStringAnsi((nint)paths[i]);
            }

            OnFilesDrop(strings);
        }

        public void Dispose()
        {
            GLFW.Terminate();
            glfwInitialized = false;
        }

        public nint GetProcAddress(string procName)
        {
            return GLFW.GetProcAddress(procName);
        }
    }
}
