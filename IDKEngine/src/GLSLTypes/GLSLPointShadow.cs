using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLPointShadow
    {
        public ulong Texture;
        public ulong ShadowTexture;

        // Can't store array of non primitive types as value type in C# (or can u?),
        // so here I am hardcoding each matrix..
        public Matrix4 PosX;
        public Matrix4 NegX;
        public Matrix4 PosY;
        public Matrix4 NegY;
        public Matrix4 PosZ;
        public Matrix4 NegZ;

        public Vector3 Position;
        public float NearPlane;

        private readonly Vector3 _pad0;
        public float FarPlane;
    }
}
