using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace IDKEngine
{
    class Camera
    {
        public Vector3 ViewDir => GetViewDirFromAngles(LookX, LookY);

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

        public float Speed;
        public float Sensitivity;
        public Camera(Vector3 position, Vector3 up, float lookX = -90.0f, float lookY = 0.0f, float mouseSensitivity = 0.1f, float speed = 1.0f)
        {
            Position = position;
            UpVector = up;
            LookX = lookX;
            LookY = lookY;

            Speed = speed;
            Sensitivity = mouseSensitivity;
        }

        public void ProcessInputs(Keyboard keyboard, Mouse mouse, float dT)
        {
            Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

            LookX += mouseDelta.X * Sensitivity;
            LookY -= mouseDelta.Y * Sensitivity;

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
                acceleration += Vector3.Cross(ViewDir, UpVector).Normalized();
            }

            if (keyboard[Keys.A] == InputState.Pressed)
            {
                acceleration -= Vector3.Cross(ViewDir, UpVector).Normalized();
            }

            acceleration *= 144.0f;

            Velocity *= MathF.Exp(MathF.Log10(0.95f) * 144.0f * dT);
            Position += dT * Velocity * Speed + 0.5f * acceleration * dT * dT;
            Velocity += (keyboard[Keys.LeftShift] == InputState.Pressed ? acceleration * 5.0f : (keyboard[Keys.LeftControl] == InputState.Pressed ? acceleration * 0.25f : acceleration)) * dT;

            if (Vector3.Dot(Velocity, Velocity) < 0.01f)
            {
                Velocity = Vector3.Zero;
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
            return GenerateViewMatrix(Position, ViewDir, UpVector);
        }

        public static Matrix4 GenerateViewMatrix(in Vector3 position, in Vector3 viewDir, in Vector3 upVector)
        {
            return Matrix4.LookAt(position, position + viewDir, upVector);
        }
    }
}
