using System;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    abstract class ShadowBase
    {
        public const int GLSL_MAX_UBO_POINT_SHADOW_COUNT = 8;

        private static int _countPointShadows;
        public static int CountPointShadows
        {
            get => _countPointShadows;

            protected set
            {
                unsafe
                {
                    _countPointShadows = value;
                    shadowsBuffer.SubData(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow), sizeof(int), _countPointShadows);
                }
            }
        }

        unsafe protected struct GLSLPointShadow
        {
            public long Sampler;
            public float NearPlane;
            public float FarPlane;

            // Can't store array of non primitive types as value type in C# (or can u?),
            // so here I am hardcoding each matrix..
            public Matrix4 PosX;
            public Matrix4 NegX;
            public Matrix4 PosY;
            public Matrix4 NegY;
            public Matrix4 PosZ;
            public Matrix4 NegZ;

            public readonly Vector3 _pad0;
            public int LightIndex;
        }

        private static unsafe BufferObject InitShadowBuffer()
        {
            BufferObject bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(GLSL_MAX_UBO_POINT_SHADOW_COUNT * sizeof(GLSLPointShadow) + sizeof(int), IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferRange(BufferRangeTarget.UniformBuffer, 2, 0, bufferObject.Size);

            return bufferObject;
        }
        private static readonly BufferObject shadowsBuffer = InitShadowBuffer();

        public readonly Texture DepthTexture;
        protected readonly Framebuffer framebuffer;
        public ShadowBase(TextureTarget2d textureTarget)
        {
            framebuffer = new Framebuffer();
            DepthTexture = new Texture(textureTarget);
        }

        public abstract void CreateDepthMap(ModelSystem modelSystem);

        protected static unsafe void PointShadowUpload(int instance, in GLSLPointShadow glslPointShadow)
        {
            Debug.Assert(instance >= 0 && instance < GLSL_MAX_UBO_POINT_SHADOW_COUNT);
            shadowsBuffer.SubData(instance * sizeof(GLSLPointShadow), sizeof(GLSLPointShadow), glslPointShadow);
        }
    }
}
