using OpenTK;

namespace IDKEngine
{
    struct GLSLBasicData
    {
        public Matrix4 ProjView;
        public Matrix4 View;
        public Matrix4 InvView;
        public Vector3 CameraPos;
        private readonly float _pad0;
        public Matrix4 Projection;
        public Matrix4 InvProjection;
        public Matrix4 InvProjView;
        public float NearPlane;
        public float FarPlane;
    }
}
