using System;
using OpenTK;
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
        public void ProcessInputs(float dT, out bool frameChanged)
        {
            frameChanged = false;

            Vector2 mouseDelta = MouseManager.DeltaPosition;

            LookX += mouseDelta.X * Sensitivity;
            LookY -= mouseDelta.Y * Sensitivity;
            if (mouseDelta.X != 0 || mouseDelta.Y != 0)
                frameChanged = true;

            if (LookY >= 90) LookY = 89.999f;
            if (LookY <= -90) LookY = -89.999f;

            ViewDir.X = MathF.Cos(MathHelper.DegreesToRadians(LookX)) * MathF.Cos(MathHelper.DegreesToRadians(LookY));
            ViewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(LookY));
            ViewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(LookX)) * MathF.Cos(MathHelper.DegreesToRadians(LookY));

            Vector3 acceleration = Vector3.Zero;
            if (KeyboardManager.IsKeyDown(Keys.W))
                acceleration += ViewDir;
            
            if (KeyboardManager.IsKeyDown(Keys.S))
                acceleration -= ViewDir;
            
            if (KeyboardManager.IsKeyDown(Keys.D))
                acceleration += Vector3.Cross(ViewDir, Up).Normalized();

            if (KeyboardManager.IsKeyDown(Keys.A))
                acceleration -= Vector3.Cross(ViewDir, Up).Normalized();

            Velocity += KeyboardManager.IsKeyDown(Keys.LeftShift) ? acceleration * 5.0f : (KeyboardManager.IsKeyDown(Keys.LeftControl) ? acceleration * 0.35f : acceleration);
            if (acceleration != Vector3.Zero || Velocity != Vector3.Zero)
                frameChanged = true;
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
