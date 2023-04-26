namespace IDKEngine
{
    unsafe struct GLSLTaaData
    {
        public const int GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT = 36; // used in shader and client code - keep in sync!

        // Can't have fixed size array of non primitive types in C# so here I am using float instead just vec2...
        public fixed float Jitters[GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT * 2];
        public int Samples;
        public int IsEnabled;
        public uint Frame;
        public float VelScale;
    }
}
