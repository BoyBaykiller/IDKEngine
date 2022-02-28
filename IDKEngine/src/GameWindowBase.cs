using System.Diagnostics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    /// <summary>
    /// Represents window which may have an OpenGL context
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

        private bool _vSync = false;
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
            InitializeGlBindings();
            GLFW.SetWindowSizeCallback(window, WindowSizeCallback);
            GLFW.SetWindowCloseCallback(window, WindowCloseCallback);
            GLFW.SetWindowFocusCallback(window, WindowFocusCallback);
        }

        private readonly Stopwatch renderTimer = new Stopwatch();
        private readonly Stopwatch updateTimer = new Stopwatch();
        /// <summary>
        /// Starts the applications game loop
        /// </summary>
        /// <param name="ups">Limit for the number of times <see cref="OnUpdate(float)"/> is dispatched. Unlimited if 0</param>
        /// <param name="fps">Limit for the number of times <see cref="OnRender(float)"/> is dispatched. Unlimited if 0</param>
        public void Start(int ups, int fps)
        {
            float renderThreshold = fps == 0 ? 0 : (1.0f / fps * 1000.0f);
            float updateThreshold = ups == 0 ? 0 : (1.0f / ups * 1000.0f);

            IsVSync = false;
            OnStart();
            renderTimer.Start();
            updateTimer.Start();
            while (!GLFW.WindowShouldClose(window))
            {
                GLFW.PollEvents();

                if (updateTimer.Elapsed.TotalMilliseconds >= updateThreshold)
                {
                    OnUpdate((float)updateTimer.Elapsed.TotalMilliseconds);
                    updateTimer.Restart();
                }

                if (renderTimer.Elapsed.TotalMilliseconds >= renderThreshold)
                {
                    OnRender((float)renderTimer.Elapsed.TotalMilliseconds);
                    renderTimer.Restart();
                }

                GLFW.SwapBuffers(window);
            }

            OnEnd();
        }

        protected abstract void OnRender(float dT);
        protected abstract void OnUpdate(float dT);
        protected abstract void OnStart();
        protected abstract void OnEnd();
        protected abstract void OnResize(int width, int height);
        protected abstract void OnFocusChanged();

        private void WindowCloseCallback(Window* window)
        {
            GLFW.DestroyWindow(window);
        }

        private void WindowSizeCallback(Window* window, int width, int height)
        {
            OnResize(width, height);
        }

        private void WindowFocusCallback(Window* window, bool focused)
        {
            _isFocused = focused;
            OnFocusChanged();
        }


        private static void InitializeGlBindings()
        {
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());
        }
    }
}
