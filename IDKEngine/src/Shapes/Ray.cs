using OpenTK.Mathematics;

namespace IDKEngine.Shapes
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
            Vector4 rayView = new Vector4(0.0f);
            rayView.Xy = ndc * new Matrix2(inverseProj.Row0.Xy, inverseProj.Row1.Xy);
            rayView.Z = -1.0f;
            rayView.W = 0.0f;

            Vector3 rayWorld = Vector3.Normalize((rayView * inverseView).Xyz);
            return new Ray(origin, rayWorld);
        }
    }
}
