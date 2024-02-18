using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public struct Sphere
    {
        public float RadiusSquared => Radius * Radius;

        public Vector3 Center;
        public float Radius;

        public Sphere(Vector3 center, float radius)
        {
            Center = center;
            Radius = radius;
        }
    }
}
