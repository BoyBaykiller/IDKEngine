using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Mouse
    {
        public Vector2 RawPosition { get; private set; }

        public InputState this[MouseButton button]
        {
            get => buttonStates[(int)button];
        }

        private readonly InputState[] buttonStates;

        public Mouse()
        {
            buttonStates = new InputState[8];
        }

        public unsafe void Update(Window* window)
        {
            for (int i = 0; i < buttonStates.Length; i++)
            {
                InputAction action = GLFW.GetMouseButton(window, (MouseButton)i);
                buttonStates[i] = (InputState)action;

                if (action == InputAction.Press && buttonStates[i] == InputState.Released)
                {
                    buttonStates[i] = InputState.Touched;
                }
                else
                {
                    buttonStates[i] = (InputState)action;
                }
            }
            double x, y;
            GLFW.GetCursorPosRaw(window, &x, &y);

            RawPosition = new Vector2((float)x, (float)y);
        }
    }
}
