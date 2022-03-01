using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Camera
    {
        public Vector3 Position;
        public Vector3 ViewDir;
        public Vector3 Up;
        public Vector3 Velocity;
        public float Speed;
        public float Sensitivity;
        public Matrix4 View { get; private set; }
        public Camera(Vector3 position, Vector3 up, float lookX = -90.0f, float lookY = 0.0f, float mouseSensitivity = 0.1f, float speed = 1.0f)
        {
            LookX = lookX;
            LookY = lookY;

            ViewDir.X = MathF.Cos(MathHelper.DegreesToRadians(LookX)) * MathF.Cos(MathHelper.DegreesToRadians(LookY));
            ViewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(LookY));
            ViewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(LookX)) * MathF.Cos(MathHelper.DegreesToRadians(LookY));

            View = GenerateMatrix(position, ViewDir, up);
            Position = position;
            Up = up;
            Speed = speed;
            Sensitivity = mouseSensitivity;
        }


        public float LookX { get; private set; }
        public float LookY { get; private set; }
        public void ProcessInputs(Keyboard keyboard, Mouse mouse, float dT, out bool didMove)
        {
            didMove = false;

            Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

            LookX += mouseDelta.X * Sensitivity;
            LookY -= mouseDelta.Y * Sensitivity;
            if (mouseDelta.X != 0 || mouseDelta.Y != 0)
                didMove = true;

            if (LookY >= 90) LookY = 89.999f;
            if (LookY <= -90) LookY = -89.999f;

            ViewDir.X = MathF.Cos(MathHelper.DegreesToRadians(LookX)) * MathF.Cos(MathHelper.DegreesToRadians(LookY));
            ViewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(LookY));
            ViewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(LookX)) * MathF.Cos(MathHelper.DegreesToRadians(LookY));

            Vector3 acceleration = Vector3.Zero;
            if (keyboard[Keys.W] == InputState.Pressed)
                acceleration += ViewDir;

            if (keyboard[Keys.S] == InputState.Pressed)
                acceleration -= ViewDir;

            if (keyboard[Keys.D] == InputState.Pressed)
                acceleration += Vector3.Cross(ViewDir, Up).Normalized();

            if (keyboard[Keys.A] == InputState.Pressed)
                acceleration -= Vector3.Cross(ViewDir, Up).Normalized();

            Velocity += keyboard[Keys.LeftShift] == InputState.Pressed ? acceleration * 5.0f : (keyboard[Keys.LeftControl] == InputState.Pressed ? acceleration * 0.35f : acceleration);
            if (acceleration != Vector3.Zero || Velocity != Vector3.Zero)
                didMove = true;
            if (Vector3.Dot(Velocity, Velocity) < 0.01f)
                Velocity = Vector3.Zero;
            
            Position += Velocity * Speed * dT;
            Velocity *= 0.95f;
            View = GenerateMatrix(Position, ViewDir, Up);
        }

        public static Matrix4 GenerateMatrix(Vector3 position, Vector3 viewDir, Vector3 up)
        {
            return Matrix4.LookAt(position, position + viewDir, up);
        }
    }
}
