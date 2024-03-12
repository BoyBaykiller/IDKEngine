using System;
using OpenTK.Mathematics;

namespace IDKEngine.GpuTypes
{
    class CpuLight
    {
        public const float MASS = 0.5f; // can easily make more dynamic in future

        public GpuLight GpuLight;

        public Vector3 Velocity;
        private Vector3 thisFrameAcceleration;

        public CpuLight(Vector3 position, Vector3 velocity, Vector3 color, float radius)
        {
            GpuLight.Position = position;
            GpuLight.Color = color;
            GpuLight.Radius = radius;
            GpuLight.PointShadowIndex = -1;

            Velocity = velocity;
        }

        public CpuLight(Vector3 position, Vector3 color, float radius)
            : this(position, new Vector3(0.0f), color, radius)
        {
        }

        public void AdvanceSimulation(float dT)
        {
            //thisFrameAcceleration.Y += -70.0f;

            GpuLight.Position += dT * Velocity + 0.5f * thisFrameAcceleration * dT * dT;
            Velocity += thisFrameAcceleration * dT;

            if (Velocity.Length < 9.0f * dT)
            {
                Velocity = new Vector3(0.0f);
            }

            // Ideally we would want to have some forces not be effected by drag (such as gravity)
            //const float dragConstant = 0.99f;
            //float drag = MathF.Log10(dragConstant) * 144.0f;
            //Velocity *= MathF.Exp(drag * dT); // https://stackoverflow.com/questions/61812575/which-formula-to-use-for-drag-simulation-each-frame

            thisFrameAcceleration = new Vector3(0.0f);
        }

        public void AddImpulse(Vector3 impulse)
        {
            Velocity += impulse / MASS;
        }

        public void AddForce(Vector3 force)
        {
            // f = m * a; a = f / m
            thisFrameAcceleration += force / MASS;
        }

        public bool HasPointShadow()
        {
            return GpuLight.PointShadowIndex >= 0;
        }
    }
}
