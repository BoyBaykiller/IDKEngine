using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLTaaData
    {
        public const int GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT = 36; // used in shader and client code - keep in sync!

        public Vector2 Jitter;
        public int Samples;
        public int IsEnabled;
        public uint Frame;
        public float VelScale;
    }
}
