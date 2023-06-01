using OpenTK.Mathematics;

namespace IDKEngine
{
    public struct Ray
    {
        public Vector3 Origin;
        public Vector3 Direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            Origin = origin;
            Direction = direction;
        }

        public Vector3 At(float t)
        {
            return Origin + Direction * t;
        }

        public Ray Transformed(Matrix4 invModel)
        {
            Ray ray = new Ray();
            ray.Origin = (new Vector4(Origin, 1.0f) * invModel).Xyz;
            ray.Direction = (new Vector4(Direction, 0.0f) * invModel).Xyz;

            return ray;
        }

        public static Ray GetWorldSpaceRay(Vector3 origin, Matrix4 inverseProj, Matrix4 inverseView, Vector2 ndc)
        {
            Vector4 rayEye = new Vector4(ndc.X, ndc.Y, -1.0f, 0.0f) * inverseProj;
            rayEye.Zw = new Vector2(-1.0f, 0.0f);
            return new Ray(origin, Vector3.Normalize((rayEye * inverseView).Xyz));
        }
    }
}
