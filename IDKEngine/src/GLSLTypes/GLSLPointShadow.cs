using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLPointShadow
    {
        public ulong Texture;
        public ulong ShadowTexture;

        // Can't have fixed size array of non primitive types in C# so here I am hardcoding each matrix..
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
