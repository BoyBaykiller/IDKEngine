using System;
using System.IO;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class AtmosphericScatterer
    {
        private int _iSteps;
        public int ISteps
        {
            set
            {
                _iSteps = value;
                shaderProgram.Upload("ISteps", _iSteps);
            }

            get => _iSteps;
        }

        private int _jSteps;
        public int JSteps
        {
            set
            {
                _jSteps = value;
                shaderProgram.Upload("JSteps", _jSteps);
            }

            get => _jSteps;
        }

        private float _time;
        public float Time
        {
            set
            {
                _time = value;
                shaderProgram.Upload("LightPos", new Vector3(0.0f, MathF.Sin(MathHelper.DegreesToRadians(_time * 360.0f)), MathF.Cos(MathHelper.DegreesToRadians(_time * 360.0f))) * 149600000e3f);
            }

            get => _time;
        }

        private float _lightIntensity;
        public float LightIntensity
        {
            set
            {
                _lightIntensity = Math.Max(value, 0.0f);
                shaderProgram.Upload("LightIntensity", _lightIntensity);
            }

            get => _lightIntensity;
        }

        public readonly Texture Result;
        private static readonly ShaderProgram shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/AtmosphericScattering/compute.glsl")));
        public unsafe AtmosphericScatterer(int size)
        {
            Result = new Texture(TextureTarget2d.TextureCubeMap);
            Result.MutableAllocate(size, size, 1, PixelInternalFormat.Rgba32f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
            /// Driver bug: Global seamless cubemap feature may be ignored when sampling from uniform samplerCube
            /// in Compute Shader with ARB_bindless_texture activated. So try switching to seamless_cubemap_per_texture
            /// More info: https://stackoverflow.com/questions/68735879/opengl-using-bindless-textures-on-sampler2d-disables-texturecubemapseamless
            Result.SetSeamlessCubeMapPerTextureARB_AMD(true);

            Time = 0.05f;
            ISteps = 40;
            JSteps = 8;
            LightIntensity = 15.0f;
        }

        public void Compute()
        {
            Result.BindToImageUnit(0, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Width + 8 - 1) / 8, 6);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int size)
        {
            Result.MutableAllocate(size, size, 1, Result.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
        }
    }
}
