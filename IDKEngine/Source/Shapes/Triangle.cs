using OpenTK.Mathematics;

namespace IDKEngine.Shapes
{
    public struct Triangle
    {
        public Vector3 Normal
        {
            get
            {
                Vector3 p0p1 = P1 - P0;
                Vector3 p0p2 = P2 - P0;
                Vector3 triNormal = Vector3.Normalize(Vector3.Cross(p0p1, p0p2));

                return triNormal;
            }
        }

        public Vector3 Center => (P0 + P1 + P2) / 3.0f;

        public Vector3 P0;
        public Vector3 P1;
        public Vector3 P2;

        public Triangle(in Vector3 p0, in Vector3 p1, in Vector3 p2)
        {
            P0 = p0;
            P1 = p1;
            P2 = p2;
        }

        public void Transform(in Matrix4 model)
        {
            P0 = (new Vector4(P0, 1.0f) * model).Xyz;
            P1 = (new Vector4(P1, 1.0f) * model).Xyz;
            P2 = (new Vector4(P2, 1.0f) * model).Xyz;
        }


        public static Triangle Transformed(Triangle triangle, in Matrix4 model)
        {
            triangle.Transform(model);
            return triangle;
        }
    }
}
