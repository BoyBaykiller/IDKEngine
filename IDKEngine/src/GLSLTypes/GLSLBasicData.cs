using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLBasicData
    {
        public Matrix4 ProjView;
        public Matrix4 View;
        public Matrix4 InvView;
        public Vector3 CameraPos;
        public int FrameCount;
        public Matrix4 Projection;
        public Matrix4 InvProjection;
        public Matrix4 InvProjView;
        public Matrix4 PrevProjView;
        public float NearPlane;
        public float FarPlane;
    }
}
