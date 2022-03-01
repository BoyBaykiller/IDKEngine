using System.Diagnostics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    /// <summary>
    /// Represents window with OpenGL context and extra helpul functionality
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

        private bool _vSync;
        public bool IsVSync
        {
            get => _vSync;

            set
            {
                _vSync = value;
                GLFW.SwapInterval(_vSync ? 1 : 0);
            }
        }

        private int _width;
        public int Width
        {
            get => _width;

            set
            {
                _width = value;
                GLFW.SetWindowSize(window, Width, Height);
            }
        }

        private int _height;
        public int Height
        {
            get => _height;

            set
            {
                _height = value;
                GLFW.SetWindowSize(window, Width, Height);
            }
        }

        private bool _isFocused = true;
        public bool IsFocused => _isFocused;

        private CursorModeValue _cursorMode;
        public CursorModeValue CursorMode
        {
            get => _cursorMode;

            set
            {
                _cursorMode = value;
                GLFW.SetInputMode(window, CursorStateAttribute.Cursor, _cursorMode);
            }
        }

        private WindowState _windowState;
        public WindowState WindowState
        {
            get => _windowState;

            set
            {  
                // TODO: Investigate more into wtf is happining here

                bool num = _windowState != WindowState.Fullscreen && _windowState != WindowState.Minimized && (value == WindowState.Fullscreen || value == WindowState.Minimized);
                if (_windowState == WindowState.Fullscreen && value != WindowState.Fullscreen)
                {
                    GLFW.SetWindowMonitor(window, null, 1920 / 2, 1080 / 2, 832, 832, 0);
                }

                _windowState = value;
                switch (_windowState)
                {
                    case WindowState.Normal:
                        GLFW.RestoreWindow(window);
                        break;

                    case WindowState.Minimized:
                        GLFW.IconifyWindow(window);
                        break;

                    case WindowState.Maximized:
                        GLFW.MaximizeWindow(window);
                        break;
                    case WindowState.Fullscreen:
                        {
                            Monitor* monitor = GLFW.GetMonitors()[0];
                            VideoMode* videoMode = GLFW.GetVideoMode(monitor);
                            GLFW.SetWindowMonitor(window, monitor, 0, 0, videoMode->Width, videoMode->Height, videoMode->RefreshRate);
                            break;
                        }
                }
            }
        }

        public readonly Keyboard KeyboardState;
        public readonly Mouse MouseState;

        private bool glfwInitialized = false;
        private readonly Window* window;

        /// <summary>
        /// Creates a window with OpenGL context
        /// </summary>
        public GameWindowBase(int width, int heigth, string title)
        {
            if (!glfwInitialized)
            {
                GLFW.Init();

                GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
                GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
                GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 6);
                glfwInitialized = true;
            }

            _title = title;
            _width = width;
            _height = heigth;
            
            window = GLFW.CreateWindow(width, heigth, _title, null, null);
            GLFW.MakeContextCurrent(window);
            InitializeGLBindings();

            windowSizeDelegate = WindowSizeCallback;
            GLFW.SetWindowSizeCallback(window, windowSizeDelegate);

            windowFocusDelegate = WindowFocusCallback;
            GLFW.SetWindowFocusCallback(window, windowFocusDelegate);

            KeyboardState = new Keyboard(window);
            MouseState = new Mouse(window);
        }

        private readonly Stopwatch renderTimer = new Stopwatch();
        private readonly Stopwatch updateTimer = new Stopwatch();
        /// <summary>
        /// Starts the applications game loop
        /// </summary>
        /// <param name="ups">Limits the number of times <see cref="OnUpdate(float)"/> is dispatched per second. Unlimited if 0</param>
        /// <param name="fps">Limits the number of times <see cref="OnRender(float)"/> is dispatched per second. Unlimited if 0</param>
        public void Start(int ups, int fps)
        {
            float renderThreshold = fps == 0 ? 0 : (1.0f / fps);
            float updateThreshold = ups == 0 ? 0 : (1.0f / ups);

            OnStart();

            renderTimer.Start();
            updateTimer.Start();
            // TODO: Simply running timers and checking for threshold doesn't work that well. Investigate into proper update and render loop system
            while (!GLFW.WindowShouldClose(window))
            {
                GLFW.PollEvents();

                if (updateTimer.Elapsed.TotalMilliseconds / 1000.0f >= updateThreshold)
                {
                    KeyboardState.Update();
                    MouseState.Update();
                    OnUpdate((float)updateTimer.Elapsed.TotalMilliseconds / 1000.0f);
                    updateTimer.Restart();
                }

                if (renderTimer.Elapsed.TotalMilliseconds / 1000.0f >= renderThreshold)
                {
                    OnRender((float)renderTimer.Elapsed.TotalMilliseconds / 1000.0f);
                    renderTimer.Restart();
                }

                GLFW.SwapBuffers(window);
            }

            OnEnd();
            GLFW.DestroyWindow(window);
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
        protected abstract void OnResize(int width, int height);
        protected abstract void OnFocusChanged();


        private readonly GLFWCallbacks.WindowSizeCallback windowSizeDelegate;
        private void WindowSizeCallback(Window* window, int width, int height)
        {
            _width = width;
            _height = height;
            OnResize(width, height);
        }

        private readonly GLFWCallbacks.WindowFocusCallback windowFocusDelegate;
        private void WindowFocusCallback(Window* window, bool focused)
        {
            _isFocused = focused;
            OnFocusChanged();
        }

        private static void InitializeGLBindings()
        {
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());
        }
    }
}
