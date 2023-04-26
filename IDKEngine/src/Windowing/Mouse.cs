using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    unsafe class Mouse
    {
        private enum ScrollUpdateState : int
        {
            Unchanged,
            Changed,
        }

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

        private Vector2 _position;
        public Vector2 Position
        {
            get => _position;

            set
            {
                LastPosition = _position;
                _position = value;
                GLFW.SetCursorPos(window, _position.X, _position.Y);
            }
        }
        
        public Vector2 LastPosition { get; private set; }

        public double ScrollX { get; private set; }
        public double ScrollY { get; private set; }

        public InputState this[MouseButton button]
        {
            get => buttonStates[(int)button];
        }

        private readonly Window* window;
        private readonly InputState[] buttonStates;
        public Mouse(Window* window)
        {
            this.window = window;
            buttonStates = new InputState[8];

            windowScrollDelegate = WindowScrollCallback;
            GLFW.SetScrollCallback(window, windowScrollDelegate);
            
            GLFW.GetCursorPos(window, out double x, out double y);
            Position = new Vector2((float)x, (float)y);
        }

        private ScrollUpdateState scrollUpdateState = ScrollUpdateState.Unchanged;
        public unsafe void Update()
        {
            for (int i = 0; i < buttonStates.Length; i++)
            {
                InputAction action = GLFW.GetMouseButton(window, (MouseButton)i);
                if (action == InputAction.Press && buttonStates[i] == InputState.Released)
                {
                    buttonStates[i] = InputState.Touched;
                }
                else
                {
                    buttonStates[i] = (InputState)action;
                }
            }
            GLFW.GetCursorPos(window, out double x, out double y);
            Position = new Vector2((float)x, (float)y);

            if (scrollUpdateState == ScrollUpdateState.Unchanged)
            {
                ScrollX = 0.0;
                ScrollY = 0.0;
            }

            if (scrollUpdateState == ScrollUpdateState.Changed)
            {
                scrollUpdateState = ScrollUpdateState.Unchanged;
            }
        }

        private readonly GLFWCallbacks.ScrollCallback windowScrollDelegate;
        private void WindowScrollCallback(Window* window, double x, double y)
        {
            ScrollX += x;
            ScrollX += y;
            scrollUpdateState = ScrollUpdateState.Changed;
        }
    }
}
