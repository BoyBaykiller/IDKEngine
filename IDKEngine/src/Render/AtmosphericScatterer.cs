using System;
using System.IO;
using OpenTK;
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
                shaderProgram.Upload("iSteps", _iSteps);
            }

            get => _iSteps;
        }

        private int _jSteps;
        public int JSteps
        {
            set
            {
                _jSteps = value;
                shaderProgram.Upload("jSteps", _jSteps);
            }

            get => _jSteps;
        }

        private float _time;
        public float Time
        {
            set
            {
                _time = value;
                shaderProgram.Upload("lightPos", new Vector3(0.0f, MathF.Sin(MathHelper.DegreesToRadians(_time * 360.0f)), MathF.Cos(MathHelper.DegreesToRadians(_time * 360.0f))) * 149600000e3f);
            }

            get => _time;
        }

        private float _lightIntensity;
        public float LightIntensity
        {
            set
            {
                _lightIntensity = Math.Max(value, 0.0f);
                shaderProgram.Upload("lightIntensity", _lightIntensity);
            }

            get => _lightIntensity;
        }

        public readonly Texture Result;
        private readonly ShaderProgram shaderProgram;
        private readonly BufferObject bufferObject;
        public AtmosphericScatterer(int size)
        {
            Result = new Texture(TextureTarget2d.TextureCubeMap);
            Result.MutableAllocate(size, size, 1, PixelInternalFormat.Rgba32f, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
            Result.SetFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);

            shaderProgram = new ShaderProgram(new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/AtmosphericScattering/compute.glsl")));
            
            bufferObject = new BufferObject();
            bufferObject.ImmutableAllocate(Vector4.SizeInBytes * 4 * 7 + Vector4.SizeInBytes, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
            bufferObject.BindBufferRange(BufferRangeTarget.UniformBuffer, 4, 0, bufferObject.Size);

            Matrix4 invProjection = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(90.0f), 1.0f, 0.1f, 10f).Inverted();
            Matrix4[] invViews = new Matrix4[]
            {
                Camera.GenerateMatrix(Vector3.Zero, new Vector3(1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // PositiveX
                Camera.GenerateMatrix(Vector3.Zero, new Vector3(-1.0f, 0.0f, 0.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // NegativeX
               
                Camera.GenerateMatrix(Vector3.Zero, new Vector3(0.0f, 1.0f, 0.0f), new Vector3(0.0f, 0.0f, 1.0f)).Inverted(), // PositiveY
                Camera.GenerateMatrix(Vector3.Zero, new Vector3(0.0f, -1.0f, 0.0f), new Vector3(0.0f, 0.0f, -1.0f)).Inverted(), // NegativeY

                Camera.GenerateMatrix(Vector3.Zero, new Vector3(0.0f, 0.0f, 1.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // PositiveZ
                Camera.GenerateMatrix(Vector3.Zero, new Vector3(0.0f, 0.0f, -1.0f), new Vector3(0.0f, -1.0f, 0.0f)).Inverted(), // NegativeZ
            };

            bufferObject.SubData(0, Vector4.SizeInBytes * 4, invProjection);
            bufferObject.SubData(Vector4.SizeInBytes * 4, Vector4.SizeInBytes * 4 * invViews.Length, invViews);

            Time = 0.05f;
            ISteps = 40;
            JSteps = 8;
            LightIntensity = 15.0f;
        }


        /// <summary>
        /// This method computes a whole cubemap rather than just whats visible. It is meant for precomputation and should not be called frequently for performance reasons
        /// </summary>
        /// <param name="renderParams"></param>
        public void Render()
        {
            Result.BindToImageUnit(0, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32f);

            shaderProgram.Use();
            GL.DispatchCompute((Result.Width + 8 - 1) / 8, (Result.Width + 4 - 1) / 4, 6);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        public void SetSize(int size)
        {
            Result.MutableAllocate(size, size, 1, Result.PixelInternalFormat, (IntPtr)0, PixelFormat.Rgba, PixelType.Float);
        }
    }
}
