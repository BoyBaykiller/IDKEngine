namespace IDKEngine.GpuTypes
{
    public record struct GpuIndicesTriplet
    {
        public int X;
        public int Y;
        public int Z;

        public GpuIndicesTriplet(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
