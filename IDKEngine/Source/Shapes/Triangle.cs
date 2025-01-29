using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public record struct Triangle
    {
        public readonly Vector3 Normal
        {
            get
            {
                Vector3 p0p1 = Position1 - Position0;
                Vector3 p0p2 = Position2 - Position0;
                Vector3 triNormal = Vector3.Normalize(Vector3.Cross(p0p1, p0p2));

                return triNormal;
            }
        }

        public readonly Vector3 Centroid => (Position0 + Position1 + Position2) / 3.0f;

        public Vector3 Position0;
        public Vector3 Position1;
        public Vector3 Position2;

        public Triangle(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Position0 = p0;
            Position1 = p1;
            Position2 = p2;
        }

        public void Transform(in Matrix4 model)
        {
            Position0 = (new Vector4(Position0, 1.0f) * model).Xyz;
            Position1 = (new Vector4(Position1, 1.0f) * model).Xyz;
            Position2 = (new Vector4(Position2, 1.0f) * model).Xyz;
        }

        public static Triangle Transformed(Triangle triangle, in Matrix4 model)
        {
            triangle.Transform(model);
            return triangle;
        }
    }
}
