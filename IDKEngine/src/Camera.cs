using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Camera
    {
        public struct State
        {
            public Vector3 CamPosition;
            public Vector3 CamUp;
            public float LookX;
            public float LookY;
        }

        public Vector3 Position { get; private set; }
        public Vector3 ViewDir { get; private set; }
        public Vector3 Up { get; private set; }
        public Vector3 Velocity;
        public float Speed;
        public float Sensitivity;
        public Matrix4 ViewMatrix { get; private set; }
        public Camera(Vector3 position, Vector3 up, float lookX = -90.0f, float lookY = 0.0f, float mouseSensitivity = 0.1f, float speed = 1.0f)
        {
            LookX = lookX;
            LookY = lookY;

            ViewDir = GetViewDirFromAngles(LookX, LookY);
            ViewMatrix = GenerateViewMatrix(position, ViewDir, up);

            Position = position;
            Up = up;
            Speed = speed;
            Sensitivity = mouseSensitivity;
        }

        public float LookX { get; private set; }
        public float LookY { get; private set; }
        public void ProcessInputs(Keyboard keyboard, Mouse mouse, float dT)
        {
            Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

            LookX += mouseDelta.X * Sensitivity;
            LookY -= mouseDelta.Y * Sensitivity;

            if (LookY >= 90) LookY = 89.999f;
            if (LookY <= -90) LookY = -89.999f;

            ViewDir = GetViewDirFromAngles(LookX, LookY);

            Vector3 acceleration = Vector3.Zero;
            if (keyboard[Keys.W] == InputState.Pressed)
                acceleration += ViewDir;

            if (keyboard[Keys.S] == InputState.Pressed)
                acceleration -= ViewDir;

            if (keyboard[Keys.D] == InputState.Pressed)
                acceleration += Vector3.Cross(ViewDir, Up).Normalized();

            if (keyboard[Keys.A] == InputState.Pressed)
                acceleration -= Vector3.Cross(ViewDir, Up).Normalized();

            acceleration *= 144.0f;

            Velocity *= MathF.Exp(MathF.Log10(0.95f) * 144.0f * dT);
            Position += dT * Velocity * Speed + 0.5f * acceleration * dT * dT;
            Velocity += (keyboard[Keys.LeftShift] == InputState.Pressed ? acceleration * 5.0f : (keyboard[Keys.LeftControl] == InputState.Pressed ? acceleration * 0.25f : acceleration)) * dT;

            if (Vector3.Dot(Velocity, Velocity) < 0.01f)
                Velocity = Vector3.Zero;

            ViewMatrix = GenerateViewMatrix(Position, ViewDir, Up);
        }

        public void SetState(in State state)
        {
            Position = state.CamPosition;
            Up = state.CamUp;
            LookX = state.LookX;
            LookY = state.LookY;

            ViewDir = GetViewDirFromAngles(LookX, LookY);
            ViewMatrix = GenerateViewMatrix(Position, ViewDir, Up);
        }

        public static Vector3 GetViewDirFromAngles(float lookX, float lookY)
        {
            Vector3 viewDir;
            viewDir.X = MathF.Cos(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));
            viewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(lookY));
            viewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));

            return viewDir;
        }

        public static Matrix4 GenerateViewMatrix(in Vector3 position, in Vector3 viewDir, in Vector3 up)
        {
            return Matrix4.LookAt(position, position + viewDir, up);
        }
    }
}
