using IDKEngine.Shapes;

namespace IDKEngine.GpuTypes
{
    public static class Conversions
    {
        public static Sphere ToSphere(GpuLight light)
        {
            return new Sphere(light.Position, light.Radius);
        }

        public static Box ToBox(GpuBlasNode node)
        {
            return new Box(node.Min, node.Max);
        }

        public static Box ToBox(GpuTlasNode node)
        {
            return new Box(node.Min, node.Max);
        }

        public static Triangle ToTriangle(GpuTriangle triangle)
        {
            return new Triangle(triangle.Vertex0.Position, triangle.Vertex1.Position, triangle.Vertex2.Position);
        }
    }
}
