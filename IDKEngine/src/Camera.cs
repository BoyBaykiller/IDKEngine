using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Camera
    {
        public struct State
        {
            public Vector3 Position;
            public Vector3 UpVector;
            public Vector3 Velocity;
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
        }

        public Vector3 ViewDir { get; private set; }

        public State CamState;
        public float Speed;
        public float Sensitivity;
        public Camera(Vector3 position, Vector3 up, float lookX = -90.0f, float lookY = 0.0f, float mouseSensitivity = 0.1f, float speed = 1.0f)
        {
            CamState.Position = position;
            CamState.UpVector = up;
            CamState.LookX = lookX;
            CamState.LookY = lookY;

            ViewDir = GetViewDirFromAngles(CamState.LookX, CamState.LookY);

            Speed = speed;
            Sensitivity = mouseSensitivity;
        }

        public void ProcessInputs(Keyboard keyboard, Mouse mouse, float dT)
        {
            Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

            CamState.LookX += mouseDelta.X * Sensitivity;
            CamState.LookY -= mouseDelta.Y * Sensitivity;

            ViewDir = GetViewDirFromAngles(CamState.LookX, CamState.LookY);

            Vector3 acceleration = Vector3.Zero;
            if (keyboard[Keys.W] == InputState.Pressed)
            {
                acceleration += ViewDir;
            }

            if (keyboard[Keys.S] == InputState.Pressed)
            {
                acceleration -= ViewDir;
            }

            if (keyboard[Keys.D] == InputState.Pressed)
            {
                acceleration += Vector3.Cross(ViewDir, CamState.UpVector).Normalized();
            }

            if (keyboard[Keys.A] == InputState.Pressed)
            {
                acceleration -= Vector3.Cross(ViewDir, CamState.UpVector).Normalized();
            }

            acceleration *= 144.0f;

            CamState.Velocity *= MathF.Exp(MathF.Log10(0.95f) * 144.0f * dT);
            CamState.Position += dT * CamState.Velocity * Speed + 0.5f * acceleration * dT * dT;
            CamState.Velocity += (keyboard[Keys.LeftShift] == InputState.Pressed ? acceleration * 5.0f : (keyboard[Keys.LeftControl] == InputState.Pressed ? acceleration * 0.25f : acceleration)) * dT;

            if (Vector3.Dot(CamState.Velocity, CamState.Velocity) < 0.01f)
            {
                CamState.Velocity = Vector3.Zero;
            }
        }

        public static Vector3 GetViewDirFromAngles(float lookX, float lookY)
        {
            Vector3 viewDir;
            viewDir.X = MathF.Cos(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));
            viewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(lookY));
            viewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));

            return viewDir;
        }

        public Matrix4 GenerateViewMatrix()
        {
            return GenerateViewMatrix(CamState.Position, ViewDir, CamState.UpVector);
        }

        public static Matrix4 GenerateViewMatrix(in Vector3 position, in Vector3 viewDir, in Vector3 upVector)
        {
            return Matrix4.LookAt(position, position + viewDir, upVector);
        }
    }
}
