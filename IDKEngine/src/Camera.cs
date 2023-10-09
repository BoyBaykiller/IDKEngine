using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Camera
    {
        public Vector3 ViewDir => GetViewDirFromAngles(LookX, LookY);
        public Vector3 PrevPosition { get; private set; }

        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 ThisFrameAcceleration;

        public Vector3 UpVector;

        public float LookX;
        public float _lookY;
        public float LookY
        {
            get => _lookY;

            set
            {
                _lookY = Math.Min(value, 89.999f);
                _lookY = Math.Max(_lookY, -89.99f);
            }
        }

        public float KeyboardAccelerationSpeed;
        public float MouseSensitivity;

        public bool HasGravity = false;
        public float GravityDownForce;
        public Camera(Vector3 position, Vector3 up, float lookX = -90.0f, float lookY = 0.0f)
        {
            Position = position;
            PrevPosition = position;
            UpVector = up;
            LookX = lookX;
            LookY = lookY;

            MouseSensitivity = 0.05f;
            KeyboardAccelerationSpeed = 30.0f;
            GravityDownForce = 70.0f;
        }

        public void ProcessInputs(Keyboard keyboard, Mouse mouse)
        {
            Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

            LookX += mouseDelta.X * MouseSensitivity;
            LookY -= mouseDelta.Y * MouseSensitivity;
            
            if (keyboard[Keys.W] == InputState.Pressed)
            {
                ThisFrameAcceleration += ViewDir * KeyboardAccelerationSpeed;
            }
            if (keyboard[Keys.S] == InputState.Pressed)
            {
                ThisFrameAcceleration -= ViewDir * KeyboardAccelerationSpeed;
            }
            if (keyboard[Keys.D] == InputState.Pressed)
            {
                ThisFrameAcceleration += Vector3.Cross(ViewDir, UpVector).Normalized() * KeyboardAccelerationSpeed;
            }
            if (keyboard[Keys.A] == InputState.Pressed)
            {
                ThisFrameAcceleration -= Vector3.Cross(ViewDir, UpVector).Normalized() * KeyboardAccelerationSpeed;
            }

            const float optionalBoost = 5.0f;
            if (keyboard[Keys.LeftShift] == InputState.Pressed)
            {
                ThisFrameAcceleration *= optionalBoost;
            }
            if (keyboard[Keys.LeftControl] == InputState.Pressed)
            {
                ThisFrameAcceleration *= (1.0f / optionalBoost);
            }

            if (keyboard[Keys.Space] == InputState.Pressed)
            {
                ThisFrameAcceleration.Y += KeyboardAccelerationSpeed * optionalBoost;
            }
        }

        public void AdvanceSimulation(float dT)
        {
            if (HasGravity)
            {
                ThisFrameAcceleration.Y += -GravityDownForce;
            }

            PrevPosition = Position;
            Position += dT * Velocity + 0.5f * ThisFrameAcceleration * dT * dT;
            Velocity += ThisFrameAcceleration * dT;

            if (Velocity.Length < 9.0f * dT)
            {
                Velocity = new Vector3(0.0f);
            }


            const float dragConstant = 0.95f;
            float drag = MathF.Log10(dragConstant) * 144.0f;
            Velocity *= MathF.Exp(drag * dT); // https://stackoverflow.com/questions/61812575/which-formula-to-use-for-drag-simulation-each-frame

            ThisFrameAcceleration = new Vector3(0.0f);
        }

        public static Vector3 GetViewDirFromAngles(float lookX, float lookY)
        {
            Vector3 viewDir;
            viewDir.X = MathF.Cos(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));
            viewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(lookY));
            viewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));

            return viewDir;
        }

        public static Matrix4 GenerateViewMatrix(in Vector3 position, in Vector3 viewDir, in Vector3 upVector)
        {
            return Matrix4.LookAt(position, position + viewDir, upVector);
        }
    }
}
