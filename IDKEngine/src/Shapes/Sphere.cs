using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public struct Sphere
    {
        public Vector3 Center;
        public float Radius;

        public Sphere(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        public float RadiusSquared()
        {
            return Radius * Radius;
        }
    }
}
