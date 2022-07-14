using System;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class SSR
    {
        private int _samples;
        public int Samples
        {
            get => _samples;

            set
            {
                _samples = value;
                shaderProgram.Upload("Samples", _samples);
            }
        }

        private int _binarySearchSamples;
        public int BinarySearchSamples
        {
            get => _binarySearchSamples;

            set
            {
                _binarySearchSamples = value;
                shaderProgram.Upload("BinarySearchSamples", _binarySearchSamples);
            }
        }

        private float _maxDist;
        public float MaxDist
        {
            get => _maxDist;

            set
            {
                _maxDist = value;
                shaderProgram.Upload("MaxDist", _maxDist);
            }
        }

        public Texture Result;
        private readonly ShaderProgram shaderProgram;
        public SSR(int width, int height, int samples, int binarySearchSamples, float maxDist)
        {
            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, System.IO.File.ReadAllText("res/shaders/SSR/compute.glsl")));

            SetSize(width, height);

            Samples = samples;
            BinarySearchSamples = binarySearchSamples;
            MaxDist = maxDist;
        }

        public unsafe void Compute(Texture samplerSrc, Texture normalTexture, Texture depthTexture)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);

            int* textures = stackalloc int[] { samplerSrc.ID, normalTexture.ID, depthTexture.ID };
            Texture.MultiBindToUnit(0, 4, textures);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Height + 8 - 1) / 8, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int width, int height)
        {
            if (Result != null) Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Result.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
            Result.ImmutableAllocate(width, height, 1, SizedInternalFormat.Rgba16f);
        }
    }
}
