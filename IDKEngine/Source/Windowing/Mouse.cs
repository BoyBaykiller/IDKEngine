using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine.Windowing
{
    unsafe class Mouse
    {
        private enum ScrollState : int
        {
            Unchanged,
            Changed,
        }

        public enum InputState : int
        {
            Released,
            Pressed,
            Repeating,
            Touched
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

        public Keyboard.InputState this[MouseButton button]
        {
            get => buttonStates[(int)button];
        }

        private readonly Window* window;
        private readonly Keyboard.InputState[] buttonStates;
        public Mouse(Window* window)
        {
            this.window = window;
            buttonStates = new Keyboard.InputState[8];

            windowScrollFuncPtr = WindowScrollCallback;
            GLFW.SetScrollCallback(window, windowScrollFuncPtr);
            
            GLFW.GetCursorPos(window, out double x, out double y);
            Position = new Vector2((float)x, (float)y);
        }

        private ScrollState scrollUpdateState = ScrollState.Unchanged;
        public void Update()
        {
            for (int i = 0; i < buttonStates.Length; i++)
            {
                InputAction action = GLFW.GetMouseButton(window, (MouseButton)i);
                if (action == InputAction.Press && buttonStates[i] == Keyboard.InputState.Released)
                {
                    buttonStates[i] = Keyboard.InputState.Touched;
                }
                else
                {
                    buttonStates[i] = (Keyboard.InputState)action;
                }
            }
            GLFW.GetCursorPos(window, out double x, out double y);
            Position = new Vector2((float)x, (float)y);

            if (scrollUpdateState == ScrollState.Unchanged)
            {
                ScrollX = 0.0;
                ScrollY = 0.0;
            }

            if (scrollUpdateState == ScrollState.Changed)
            {
                scrollUpdateState = ScrollState.Unchanged;
            }
        }

        private readonly GLFWCallbacks.ScrollCallback windowScrollFuncPtr;
        private void WindowScrollCallback(Window* window, double x, double y)
        {
            ScrollX += x;
            ScrollX += y;
            scrollUpdateState = ScrollState.Changed;
        }
    }
}
