using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    unsafe class Mouse
    {
        public Vector2 Position { get; private set; }
        public Vector2 LastPosition { get; private set; }

        public InputState this[MouseButton button]
        {
            get => buttonStates[(int)button];
        }

        private readonly InputState[] buttonStates;

        private readonly Window* window;
        public Mouse(Window* window)
        {
            this.window = window;
            buttonStates = new InputState[8];
        }

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
            
            LastPosition = Position;
            Position = new Vector2((float)x, (float)y);
        }
    }
}
