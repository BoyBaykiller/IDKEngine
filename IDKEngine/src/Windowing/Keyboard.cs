using System;
using System.Collections.Generic;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine.Windowing
{
    unsafe class Keyboard
    {
        public enum InputState : byte
        {
            Released = 0,
            Pressed = 1,
            Repeating = 2,
            Touched = 3
        }

        public InputState this[Keys key]
        {
            get => keyStates[key];
        }

        
        public static readonly Keys[] KeyValues = Enum.GetValues<Keys>();

        // Keys aren't layed out sequentialy so I decided to use a dictionary instead of InputState[]
        private readonly Dictionary<Keys, InputState> keyStates;

        private readonly Window* window;
        public Keyboard(Window* window)
        {
            this.window = window;

            keyStates = new Dictionary<Keys, InputState>();

            for (int i = 0; i < KeyValues.Length; i++)
            {
                if (!keyStates.ContainsKey(KeyValues[i]))
                    keyStates.Add(KeyValues[i], InputState.Released);
            }
        }

        public void Update()
        {
            for (int i = 0; i < KeyValues.Length; i++)
            {
                InputAction action = GLFW.GetKey(window, KeyValues[i]);
                if (action == InputAction.Press && keyStates[KeyValues[i]] == InputState.Released)
                {
                    keyStates[KeyValues[i]] = InputState.Touched;
                }
                else
                {
                    keyStates[KeyValues[i]] = (InputState)action;
                }
            }
        }
    }
}
