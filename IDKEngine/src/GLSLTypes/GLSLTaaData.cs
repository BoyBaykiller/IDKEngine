namespace IDKEngine
{
    unsafe struct GLSLTaaData
    {
        public const int GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT = 36; // used in shader and client code - keep in sync!

        // Thanks again to C# for not letting me declare fixed size array of unmanaged custom struct (Vector2 I would like to have instead)
        public fixed float Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT * 2];
        public int Samples;
        public int IsEnabled;
        public uint Frame;
        public float VelScale;
    }
}
