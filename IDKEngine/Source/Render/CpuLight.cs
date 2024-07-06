using IDKEngine.GpuTypes;
using OpenTK.Mathematics;

namespace IDKEngine.Render
{
    class CpuLight
    {
        public const float MASS = 0.5f;

        public GpuLight GpuLight;

        public Vector3 Velocity;
        private Vector3 thisFrameAcceleration;
        public CpuLight(Vector3 position, Vector3 color, float radius)
            : this(position, new Vector3(0.0f), color, radius)
        {
        }

        public CpuLight(Vector3 position, Vector3 velocity, Vector3 color, float radius)
            : this(new GpuLight() { Position = position, Color = color, Radius = radius }, velocity)
        {
        }

        public CpuLight(in GpuLight gpuLight, Vector3 velocity)
        {
            GpuLight = gpuLight;
            Velocity = velocity;
        }

        public CpuLight(in GpuLight gpuLight)
        {
            GpuLight = gpuLight;
        }

        public static implicit operator CpuLight(in GpuLight gpuLight)
        {
            return new CpuLight(gpuLight);
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

        public void ConnectPointShadow(int pointShadowIndex)
        {
            GpuLight.PointShadowIndex = pointShadowIndex;
        }

        public void DisconnectPointShadow()
        {
            GpuLight.PointShadowIndex = -1;
        }
    }
}
