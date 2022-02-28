using System;
using System.Collections.Generic;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Keyboard
    {
        public InputState this[Keys key]
        {
            get => keyStates[key];
        }

        
        private static readonly Keys[] keyValues = Enum.GetValues<Keys>();

        // Keys aren't layed out sequentialy so I decided to use a dictionary instead of InputState[]
        private readonly Dictionary<Keys, InputState> keyStates;

        public Keyboard()
        {
            keyStates = new Dictionary<Keys, InputState>(keyValues.Length);

            for (int i = 0; i < keyValues.Length - 2; i++)
            {
                keyStates.Add(keyValues[i], InputState.Released);
            }
        }

        public unsafe void Update(Window* window)
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
