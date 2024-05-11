using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public struct Plane
    {
        public Vector3 Normal;

        public Plane(Vector3 normal)
        {
            Normal = normal;
        }

        public static Vector3 Project(in Vector3 v, in Plane plane)
        {
            Vector3 projectedOnNormal = Vector3.Dot(plane.Normal, v) * plane.Normal;
            Vector3 projectedOnPlane = v - projectedOnNormal;
            return projectedOnPlane;
        }

        public static Vector3 Reflect(in Vector3 incident, in Plane plane)
        {
            return incident - 2.0f * Vector3.Dot(plane.Normal, incident) * plane.Normal;
        }
    }
}
