using OpenTK.Mathematics;

namespace IDKEngine
{
    struct GLSLBasicData
    {
        public Matrix4 ProjView;
        public Matrix4 View;
        public Matrix4 InvView;
        public Matrix4 PrevView;
        public Vector3 CameraPos;
        public uint Frame;
        public Matrix4 Projection;
        public Matrix4 InvProjection;
        public Matrix4 InvProjView;
        public Matrix4 PrevProjView;
        public float NearPlane;
        public float FarPlane;
        public float DeltaUpdate;
        public float Time;
    }
}
