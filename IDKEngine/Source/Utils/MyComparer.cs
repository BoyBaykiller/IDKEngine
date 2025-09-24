namespace IDKEngine.Utils
{
    public static class MyComparer
    {
        public interface IComparisons<T> where T : IComparisons<T>?
        {
            public static abstract bool operator <(in T left, in T right);
            public static abstract bool operator >(in T left, in T right);
        }

        public static int LessThan<T>(T x, T y) where T : IComparisons<T>
        {
            if (x > y) return 1;
            if (x < y) return -1;
            return 0;
        }

        public static int GreaterThan<T>(T x, T y) where T : IComparisons<T>
        {
            if (x > y) return -1;
            if (x < y) return 1;
            return 0;
        }

        public static int GreaterThan(int x, int y)
        {
            if (x > y) return -1;
            if (x < y) return 1;
            return 0;
        }

        public static int LessThan(int x, int y)
        {
            if (x > y) return 1;
            if (x < y) return -1;
            return 0;
        }

        public static int GreaterThan(float x, float y)
        {
            if (x > y) return -1;
            if (x < y) return 1;
            return 0;
        }

        public static int LessThan(float x, float y)
        {
            if (x > y) return 1;
            if (x < y) return -1;
            return 0;
        }

        public static int LessThan(uint x, uint y)
        {
            if (x > y) return 1;
            if (x < y) return -1;
            return 0;
        }
    }
}
