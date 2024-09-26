using IDKEngine.Bvh;
using IDKEngine.GpuTypes;

namespace IDKEngine.Shapes
{
    public static class Conversions
    {
        public static Sphere ToSphere(in GpuLight light)
        {
            return new Sphere(light.Position, light.Radius);
        }

        public static Box ToBox(in GpuBlasNode node)
        {
            return new Box(node.Min, node.Max);
        }

        public static Box ToBox(in GpuTlasNode node)
        {
            return new Box(node.Min, node.Max);
        }

        public static Triangle ToTriangle(in BLAS.Triangle triangle)
        {
            return new Triangle(triangle.Position0, triangle.Position1, triangle.Position2);
        }
    }
}
