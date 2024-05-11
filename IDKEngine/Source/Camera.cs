using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Utils;
using IDKEngine.Windowing;

namespace IDKEngine
{
    class Camera
    {
        public const float MASS = 60.0f; // can easily make more dynamic in future

        public Vector3 ViewDir => GetViewDirFromAngles(LookX, LookY);
        public Vector3 PrevPosition { get; private set; }

        public Vector3 Position;
        public Vector3 Velocity;
        private Vector3 thisFrameAcceleration;

        public Vector3 UpVector;


        private float _lookX;
        public float LookX
        {
            get => _lookX;

            set
            {
                _lookX = value;
            }
        }

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

        public bool IsGravity;
        public float GravityDownForce;

        // Projection Params
        public Vector2i ProjectionSize;
        public float NearPlane = 0.1f;
        public float FarPlane = 250.0f;
        public float FovY = MathHelper.DegreesToRadians(102.0f);

        public Camera(Vector2i size, Vector3 position, float lookX = -90.0f, float lookY = 0.0f)
        {
            Position = position;
            PrevPosition = position;
            UpVector = new Vector3(0.0f, 1.0f, 0.0f);
            LookX = lookX;
            LookY = lookY;

            MouseSensitivity = 0.09f;
            KeyboardAccelerationSpeed = 30.0f * MASS; // same "experienced" acceleration regardless of mass

            ProjectionSize = size;

            IsGravity = false;
            GravityDownForce = 70.0f;
        }

        public void ProcessInputs(Keyboard keyboard, Mouse mouse)
        {
            Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

            LookX += mouseDelta.X * MouseSensitivity;
            LookY -= mouseDelta.Y * MouseSensitivity;

            Vector3 force = new Vector3();
            if (keyboard[Keys.W] == Keyboard.InputState.Pressed)
            {
                force += ViewDir * KeyboardAccelerationSpeed;
            }
            if (keyboard[Keys.S] == Keyboard.InputState.Pressed)
            {
                force -= ViewDir * KeyboardAccelerationSpeed;
            }
            if (keyboard[Keys.D] == Keyboard.InputState.Pressed)
            {
                force += Vector3.Cross(ViewDir, UpVector).Normalized() * KeyboardAccelerationSpeed;
            }
            if (keyboard[Keys.A] == Keyboard.InputState.Pressed)
            {
                force -= Vector3.Cross(ViewDir, UpVector).Normalized() * KeyboardAccelerationSpeed;
            }

            const float optionalBoost = 5.0f;
            if (keyboard[Keys.LeftShift] == Keyboard.InputState.Pressed)
            {
                force *= optionalBoost;
            }
            if (keyboard[Keys.LeftControl] == Keyboard.InputState.Pressed)
            {
                force *= (1.0f / optionalBoost);
            }

            if (keyboard[Keys.Space] == Keyboard.InputState.Pressed)
            {
                force.Y += KeyboardAccelerationSpeed * optionalBoost;
            }

            AddForce(force);
        }

        public void AdvanceSimulation(float dT)
        {
            if (IsGravity)
            {
                thisFrameAcceleration.Y += -GravityDownForce;
            }

            Position += dT * Velocity + 0.5f * thisFrameAcceleration * dT * dT;
            Velocity += thisFrameAcceleration * dT;

            if (Velocity.Length < 9.0f * dT)
            {
                Velocity = new Vector3(0.0f);
            }

            // Ideally we would want to have some forces not be effected by drag (such as gravity)
            const float dragConstant = 0.95f;
            float drag = MathF.Log10(dragConstant) * 144.0f;
            Velocity *= MathF.Exp(drag * dT); // https://stackoverflow.com/questions/61812575/which-formula-to-use-for-drag-simulation-each-frame

            thisFrameAcceleration = new Vector3(0.0f);
        }

        public void SetPrevToCurrentPosition()
        {
            PrevPosition = Position;
        }

        public void AddImpulse(Vector3 impulse)
        {
            Velocity += impulse / MASS;
        }

        public void AddForce(Vector3 force)
        {
            // f = m * a; a = f/m
            thisFrameAcceleration += force / MASS;
        }

        public static Vector3 GetViewDirFromAngles(float lookX, float lookY)
        {
            Vector3 viewDir;
            viewDir.X = MathF.Cos(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));
            viewDir.Y = MathF.Sin(MathHelper.DegreesToRadians(lookY));
            viewDir.Z = MathF.Sin(MathHelper.DegreesToRadians(lookX)) * MathF.Cos(MathHelper.DegreesToRadians(lookY));

            return viewDir;
        }

        public Matrix4 GetViewMatrix()
        {
            return GenerateViewMatrix(Position, ViewDir, UpVector);
        }

        public Matrix4 GetProjectionMatrix()
        {
            return MyMath.CreatePerspectiveFieldOfViewDepthZeroToOne(FovY, ProjectionSize.X / (float)ProjectionSize.Y, NearPlane, FarPlane);
        }

        public static Matrix4 GenerateViewMatrix(in Vector3 position, in Vector3 viewDir, in Vector3 upVector)
        {
            return Matrix4.LookAt(position, position + viewDir, upVector);
        }
    }
}
