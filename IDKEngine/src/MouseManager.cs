using OpenTK;
using OpenTK.Input;

namespace IDKEngine
{
    public static class MouseManager
    {
        private static MouseState lastMouseState;
        private static MouseState thisMouseState;
        private static MouseState thisMouseCursorState;
        public static void Update()
        {
            lastMouseState = thisMouseState;
            thisMouseState = Mouse.GetState();
            thisMouseCursorState = Mouse.GetCursorState();
        }


        public static int WindowPositionX => thisMouseCursorState.X;
        public static int WindowPositionY => thisMouseCursorState.Y;
        public static int PositionX => thisMouseState.X;
        public static int PositionY => thisMouseState.Y;
        public static Vector2 DeltaPosition => new Vector2(thisMouseState.X - lastMouseState.X, thisMouseState.Y - lastMouseState.Y);
        public static float DeltaScrollX => thisMouseState.Scroll.X - lastMouseState.Scroll.X;
        public static float DeltaScrollY => thisMouseState.Scroll.Y - lastMouseState.Scroll.Y;

        public static ButtonState LeftButton => thisMouseState.LeftButton;
        public static ButtonState RightButton => thisMouseState.RightButton;
        public static ButtonState MiddleButton => thisMouseState.MiddleButton;

        /// <summary>
        /// 
        /// </summary>
        /// <returns>True if mouseButton is down this update but not last one</returns>
        public static bool IsButtonTouched(MouseButton mouseButton) => thisMouseState.IsButtonDown(mouseButton) && lastMouseState.IsButtonUp(mouseButton);

        /// <summary>
        /// 
        /// </summary>
        /// <returns>True if mouseButton is down</returns>
        public static bool IsButtonDown(MouseButton mouseButton) => thisMouseState.IsButtonDown(mouseButton);

        /// <summary>
        /// 
        /// </summary>
        /// <returns>True if mouseButton is up this update but not last one</returns>
        public static bool IsButtonUp(MouseButton mouseButton) => thisMouseState.IsButtonUp(mouseButton) && lastMouseState.IsButtonDown(mouseButton);
    }
}
