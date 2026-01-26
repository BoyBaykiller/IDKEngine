using System;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using IDKEngine.Utils;
using IDKEngine.Shapes;
using IDKEngine.Windowing;

namespace IDKEngine;

public class Camera
{
    public const float MASS = 60.0f;

    public Vector3 ViewDir => MyMath.PolarToCartesian(MyMath.DegreesToRadians(Yaw), MyMath.DegreesToRadians(Pitch));
    public Vector3 PrevPosition { get; private set; }

    public Vector3 Position;
    public Vector3 Velocity;

    public Vector3 UpVector;

    public float Yaw;

    private float _pitch;
    public float Pitch
    {
        get => _pitch;

        set
        {
            _pitch = Math.Clamp(value, 0.001f, 179.999f);
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
    public float FovY = MyMath.DegreesToRadians(102.0f);

    public SceneVsMovingSphereCollisionSettings CollisionSettings = new SceneVsMovingSphereCollisionSettings()
    {
        IsEnabled = true,
        Settings = new Intersections.SceneVsMovingSphereSettings()
        {
            TestSteps = 3,
            RecursiveSteps = 12,
            EpsilonNormalOffset = 0.001f
        }
    };
    public float CollisionRadius = 0.5f;

    private Vector3 thisFrameAcceleration;
    public Camera(Vector2i size, Vector3 position, float yaw = 0.0f, float pitch = 90.0f)
    {
        Position = position;
        PrevPosition = position;
        UpVector = new Vector3(0.0f, 1.0f, 0.0f);
        Yaw = yaw;
        Pitch = pitch;

        MouseSensitivity = 0.09f;
        KeyboardAccelerationSpeed = 30.0f * MASS; // same "experienced" acceleration regardless of mass

        ProjectionSize = size;

        IsGravity = false;
        GravityDownForce = 70.0f;
    }

    public void ProcessInputs(Keyboard keyboard, Mouse mouse)
    {
        Vector2 mouseDelta = mouse.Position - mouse.LastPosition;

        Yaw += mouseDelta.X * MouseSensitivity;
        Pitch += mouseDelta.Y * MouseSensitivity;

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
            Vector3 dir = Vector3.Normalize(Vector3.Cross(ViewDir, UpVector));
            force += dir * KeyboardAccelerationSpeed;
        }
        if (keyboard[Keys.A] == Keyboard.InputState.Pressed)
        {
            Vector3 dir = Vector3.Normalize(Vector3.Cross(ViewDir, UpVector));
            force -= dir * KeyboardAccelerationSpeed;
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

        // Equivalent to https://en.wikipedia.org/wiki/Verlet_integration#Velocity_Verlet assuming currentAcceleration == nextAcceleration
        Position = IntegratePosOverTimeWConstAccel(Position, Velocity, thisFrameAcceleration, dT);
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

        static Vector3 IntegratePosOverTimeWConstAccel(Vector3 position, Vector3 velocity, Vector3 acceleration, float dT)
        {
            // Closed form solution for integration of position over time with constant acceleration
            return position + velocity * dT + 0.5f * acceleration * dT * dT;
        }
    }

    public void CollisionDetection(ModelManager modelManager)
    {
        if (CollisionSettings.IsEnabled)
        {
            Sphere movingSphere = new Sphere(PrevPosition, CollisionRadius);
            Vector3 prevSpherePos = movingSphere.Center;
            Intersections.SceneVsMovingSphereCollisionRoutine(modelManager, CollisionSettings.Settings, ref movingSphere, ref Position, (in Intersections.SceneHitInfo hitInfo) =>
            {
                Vector3 deltaStep = Position - prevSpherePos;
                Vector3 slidedDeltaStep = Plane.Project(deltaStep, hitInfo.SlidingPlane);
                Position = movingSphere.Center + slidedDeltaStep;

                Velocity = Plane.Project(Velocity, hitInfo.SlidingPlane);

                prevSpherePos = movingSphere.Center;
            });
        }
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

    public Matrix4 GetViewMatrix()
    {
        return GenerateViewMatrix(Position, ViewDir, UpVector);
    }

    public Matrix4 GetProjectionMatrix()
    {
        return MyMath.CreatePerspectiveFieldOfViewDepthZeroToOne(FovY, ProjectionSize.X / (float)ProjectionSize.Y, NearPlane, FarPlane);
    }

    public static Matrix4 GenerateViewMatrix(Vector3 position, Vector3 viewDir, Vector3 upVector)
    {
        return Matrix4.LookAt(position, position + viewDir, upVector);
    }
}
