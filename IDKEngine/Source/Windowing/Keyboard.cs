using System;
using System.Collections.Generic;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine.Windowing
{
    unsafe class Keyboard
    {
        public enum InputState : int
        {
            Released,
            Pressed,
            Repeating,
            Touched
        }

        public InputState this[Keys key]
        {
            get => keyStates[key];
        }

        
        private static readonly Keys[] keyValues = Enum.GetValues<Keys>();

        // Keys aren't layed out sequentialy so I decided to use a dictionary instead of InputState[]
        private readonly Dictionary<Keys, InputState> keyStates;

        private readonly Window* window;
        public Keyboard(Window* window)
        {
            this.window = window;

            keyStates = new Dictionary<Keys, InputState>();

            for (int i = 0; i < keyValues.Length; i++)
            {
                keyStates.TryAdd(keyValues[i], InputState.Released);
            }
        }

        public void Update()
        {
            for (int i = 0; i < keyValues.Length; i++)
            {
                InputAction action = GLFW.GetKey(window, keyValues[i]);
                if (action == InputAction.Press && keyStates[keyValues[i]] == InputState.Released)
                {
                    keyStates[keyValues[i]] = InputState.Touched;
                }
                else
                {
                    keyStates[keyValues[i]] = (InputState)action;
                }
            }
        }
    }
}
