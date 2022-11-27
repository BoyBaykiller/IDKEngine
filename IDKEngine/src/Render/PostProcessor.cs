using System;
using System.IO;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;
using System.Runtime.InteropServices;

namespace IDKEngine.Render
{
    unsafe class PostProcessor : IDisposable
    {
        public bool TaaEnabled
        {
            get => taaData->IsEnabled == 1;

            set
            {
                taaData->IsEnabled = value ? 1 : 0;
                taaBuffer.SubData(Vector2.SizeInBytes * GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT + sizeof(int), sizeof(int), taaData->IsEnabled);
            }
        }
        public int TaaSamples
        {
            get => taaData->Samples;

            set
            {
                Debug.Assert(value <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);
                taaData->Samples = value;
                taaBuffer.SubData(Vector2.SizeInBytes * GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT, sizeof(int), taaData->Samples);
            }
        }


        private bool _isDithering;
        public bool IsDithering
        {
            get => _isDithering;

            set
            {
                _isDithering = value;
                combineProgram.Upload("IsDithering", _isDithering);
            }
        }

        private float _gamma;
        public float Gamma
        {
            get => _gamma;

            set
            {
                _gamma = value;
                combineProgram.Upload("Gamma", _gamma);
            }
        }

        private bool _isTaaArtifactMitigation;
        public bool IsTaaArtifactMitigation
        {
            get => _isTaaArtifactMitigation;

            set
            {
                _isTaaArtifactMitigation = value;
                taaResolveProgram.Upload("IsTaaArtifactMitigation", _isTaaArtifactMitigation);
            }
        }

        public Texture Result => isPing ? taaPing : taaPong;

        private Texture taaPing;
        private Texture taaPong;
        private readonly ShaderProgram taaResolveProgram;
        private readonly ShaderProgram combineProgram;
        private readonly BufferObject taaBuffer;
        private readonly GLSLTaaData* taaData;
        private bool isPing;
        public PostProcessor(int width, int height, float gamma = 2.2f, int taaSamples = 6)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            combineProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/PostCombine/compute.glsl")));
            taaResolveProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/TAAResolve/compute.glsl")));

            taaBuffer = new BufferObject();
            taaBuffer.ImmutableAllocate(sizeof(GLSLTaaData), (IntPtr)0, BufferStorageFlags.DynamicStorageBit);
            taaBuffer.BindBufferBase(BufferRangeTarget.UniformBuffer, 3);

            taaData = Helper.Malloc<GLSLTaaData>();
            taaData->Samples = taaSamples;
            taaData->IsEnabled = 1;
            taaData->Scale = 5.0f;
            taaData->Frame = 0;
            taaBuffer.SubData(0, sizeof(GLSLTaaData), (IntPtr)taaData);

            SetSize(width, height);
            IsDithering = true;
            Gamma = gamma;
            IsTaaArtifactMitigation = true;
        }

        public void Compute(Texture v0, Texture v1, Texture v2, Texture v3, Texture velocityTexture, Texture depthTexture)
        {
            isPing = !isPing;
            (isPing ? taaPing : taaPong).BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, taaPing.SizedInternalFormat);

            if (v0 != null) v0.BindToUnit(0);
            else Texture.UnbindFromUnit(0);

            if (v1 != null) v1.BindToUnit(1);
            else Texture.UnbindFromUnit(1);

            if (v2 != null) v2.BindToUnit(2);
            else Texture.UnbindFromUnit(2);

            if (v3 != null) v3.BindToUnit(3);
            else Texture.UnbindFromUnit(3);

            combineProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

            if (taaData->IsEnabled == 1 && velocityTexture != null && depthTexture != null)
            {
                (isPing ? taaPing : taaPong).BindToImageUnit(0, 0, false, 0, TextureAccess.ReadWrite, taaPing.SizedInternalFormat);
                (isPing ? taaPong : taaPing).BindToUnit(0);
                velocityTexture.BindToUnit(1);
                depthTexture.BindToUnit(2);
                taaResolveProgram.Use();
                GL.DispatchCompute((taaPing.Width + 8 - 1) / 8, (taaPing.Height + 8 - 1) / 8, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);

                taaData->Frame++;
                taaBuffer.SubData(Vector2.SizeInBytes * GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT + 2 * sizeof(uint), sizeof(uint), taaData->Frame);
            }
        }

        public void SetSize(int width, int height)
        {
            if (taaPing != null) taaPing.Dispose();
            taaPing = new Texture(TextureTarget2d.Texture2D);
            taaPing.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPing.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPing.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            if (taaPong != null) taaPong.Dispose();
            taaPong = new Texture(TextureTarget2d.Texture2D);
            taaPong.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            taaPong.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            taaPong.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);

            Span<float> jitterData = new Span<float>(taaData->Jitters, GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT * 2);
            MyMath.GetHaltonSequence_2_3(jitterData);
            MyMath.MapHaltonSequence(jitterData, width, height);
            fixed (void* ptr = jitterData)
            {
                taaBuffer.SubData(0, sizeof(float) * jitterData.Length, (IntPtr)ptr);
            }
        }

        public void Dispose()
        {
            taaPing.Dispose();
            taaPong.Dispose();
            taaResolveProgram.Dispose();
            combineProgram.Dispose();
            taaBuffer.Dispose();
            Marshal.FreeHGlobal((nint)taaData);
        }

    }
}
