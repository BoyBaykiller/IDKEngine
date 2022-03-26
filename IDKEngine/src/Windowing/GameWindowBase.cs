using System;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    /// <summary>
    /// Represents window with OpenGL context and helpul game functionality
    /// </summary>
    abstract unsafe class GameWindowBase
    {
        private string _title;
        public string Title
        {
            get => _title;

            set
            {
                _title = value;
                GLFW.SetWindowTitle(window, _title);
            }

        }

        private bool _isVSync;
        public bool IsVSync
        {
            get => _isVSync;

            set
            {
                _isVSync = value;
                GLFW.SwapInterval(_isVSync ? 1 : 0);
            }
        }

        private Vector2i _size;
        public Vector2i Size
        {
            get => _size;

            set
            {
                _size = value;
                GLFW.SetWindowSize(window, _size.X, _size.Y);
            }
        }

        private Vector2i _position;
        public Vector2i Position
        {
            get => _position;

            set
            {
                _position = value;
                GLFW.SetWindowPos(window, _position.X, _position.Y);
            }
        }

        private bool _isFullscreen;
        public bool IsFullscreen
        {
            get => _isFullscreen;

            set
            {
                _isFullscreen = value;
                if (_isFullscreen)
                {
                    GLFW.GetWindowPos(window, out cachedWindowPos.X, out cachedWindowPos.Y);
                    GLFW.GetWindowSize(window, out cachedWindowSize.X, out cachedWindowSize.Y);
                    GLFW.SetWindowMonitor(window, Monitor, 0, 0, VideoMode->Width, VideoMode->Height, VideoMode->RefreshRate);
                }
                else
                {
                    GLFW.SetWindowMonitor(window, null, cachedWindowPos.X, cachedWindowPos.Y, cachedWindowSize.X, cachedWindowSize.Y, 0);
                }
                IsVSync = _isVSync;
            }
        }

        private float _time;
        public float Time => _time;

        private bool _isFocused = true;
        public bool IsFocused => _isFocused;

        public readonly VideoMode* VideoMode;
        public readonly Monitor* Monitor;
        public double UpdatePeriod = 1.0 / 60.0;
        public readonly Keyboard KeyboardState;
        public readonly Mouse MouseState;

        private Vector2i cachedWindowPos;
        private Vector2i cachedWindowSize;

        private readonly bool glfwInitialized = false;
        private readonly Window* window;

        /// <summary>
        /// Creates a window with OpenGL context
        /// </summary>
        public GameWindowBase(int width, int heigth, string title)
        {
            if (!glfwInitialized)
            {
                GLFW.Init();
#if DEBUG
                GLFW.WindowHint(WindowHintBool.OpenGLDebugContext, true);
#else
                GLFW.WindowHint(WindowHintBool.ContextNoError, true);
#endif
                GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
                GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
                GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 6);
                glfwInitialized = true;
            }
            _title = title;
            _size.X = width;
            _size.Y = heigth;

            window = GLFW.CreateWindow(_size.X, _size.Y, _title, null, null);

            framebufferSizeDelegate = FramebufferSizeCallback;
            GLFW.SetFramebufferSizeCallback(window, framebufferSizeDelegate);

            windowFocusDelegate = WindowFocusCallback;
            GLFW.SetWindowFocusCallback(window, windowFocusDelegate);

            windowPosDelegate = WindowPosCallback;
            GLFW.SetWindowPosCallback(window, windowPosDelegate);

            Monitor = GLFW.GetPrimaryMonitor();
            VideoMode = GLFW.GetVideoMode(Monitor);
            KeyboardState = new Keyboard(window);
            MouseState = new Mouse(window);
            Position = new Vector2i(VideoMode->Width / 2 - _size.X / 2, VideoMode->Height / 2 - _size.Y / 2);
            updateTimer = new Stopwatch();


            GLFW.MakeContextCurrent(window);
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());
        }

        /// <summary>
        /// Starts the applications game loop
        /// </summary>
        /// <param name="ups">Limits the number of times <see cref="OnUpdate(float)"/> is dispatched per second. Unlimited if 0</param>
        /// <param name="fps">Limits the number of times <see cref="OnRender(float)"/> is dispatched per second. Unlimited if 0</param>
        public void Start()
        {
            OnStart();

            updateTimer.Start();
            double lastTime = 0.0;
            GLFW.SetTime(0.0);
            while (!GLFW.WindowShouldClose(window))
            {
                double currentTime = GLFW.GetTime();
                double runTime = currentTime - lastTime;
                
                GLFW.PollEvents();
                if (IsFocused)
                {
                    DispatchUpdateFrame();
                    OnRender((float)runTime);

                    GLFW.SwapBuffers(window);

                    lastTime = currentTime;
                    _time += (float)runTime;
                }
            }

            OnEnd();
            GLFW.DestroyWindow(window);
        }

        private readonly Stopwatch updateTimer;
        private double updateEpsilon;
        private bool isRunningSlowly;
        // Source: https://github.com/opentk/opentk/blob/558132bd2cc41eed704f6e6acd1e3fe5830df5ad/src/OpenTK.Windowing.Desktop/GameWindow.cs#L258
        private void DispatchUpdateFrame()
        {
            double isRunningSlowlyRetries = 4;
            double elapsed = updateTimer.Elapsed.TotalSeconds;

            while (elapsed > 0.0 && elapsed + updateEpsilon >= UpdatePeriod)
            {
                updateTimer.Restart();
                KeyboardState.Update();
                MouseState.Update();
                OnUpdate((float)elapsed);

                updateEpsilon += elapsed - UpdatePeriod;

                isRunningSlowly = updateEpsilon >= UpdatePeriod;

                if (isRunningSlowly && --isRunningSlowlyRetries == 0)
                    break;

                elapsed = updateTimer.Elapsed.TotalSeconds;
            }
        }

        /// <summary>
        /// Initiates the first step of SetWindowShouldClose -> OnEnd -> DestroyWindow
        /// </summary>
        public void ShouldClose()
        {
            GLFW.SetWindowShouldClose(window, true);
        }

        protected abstract void OnRender(float dT);
        protected abstract void OnUpdate(float dT);
        protected abstract void OnStart();
        protected abstract void OnEnd();
        protected abstract void OnResize();
        protected abstract void OnFocusChanged();


        private readonly GLFWCallbacks.FramebufferSizeCallback framebufferSizeDelegate;
        private void FramebufferSizeCallback(Window* window, int width, int height)
        {
            // Don't trigger resize when window toggled or minimized
            if ((width > 0 && height > 0) && (_size.X != width || _size.Y != height))
            {
                _size.X = width;
                _size.Y = height;
                OnResize();
            }
        }

        private readonly GLFWCallbacks.WindowFocusCallback windowFocusDelegate;
        private void WindowFocusCallback(Window* window, bool focused)
        {
            _isFocused = focused;
            OnFocusChanged();
        }

        private readonly GLFWCallbacks.WindowPosCallback windowPosDelegate;
        private void WindowPosCallback(Window* window, int x, int y)
        {
            _position.X = x;
            _position.Y = y;
            MouseState.Update();
        }
    }
}
