using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ShadingRateClassifier
    {
        // Definied by spec
        public const int TILE_SIZE = 16;

        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/ShadingRateClassification/compute.glsl")));

        private bool _isDebug;
        public bool IsDebug
        {
            get => _isDebug;

            set
            {
                _isDebug = value;
                shaderProgram.Upload("IsDebug", IsDebug);
            }
        }


        private float _aggressiveness;
        public float Aggressiveness
        {
            get => _aggressiveness;

            set
            {
                _aggressiveness = value;
                shaderProgram.Upload("Aggressiveness", Aggressiveness);
            }
        }

        public Texture Result;
        private int width;
        private int height;
        public ShadingRateClassifier(int width, int height, float aggressiveness = 5.0f)
        {
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            // Shading rate texture must be imuutable by spec
            Result.ImmutableAllocate(width / 16, height / 16, 1, SizedInternalFormat.R32ui);

            this.width = width;
            this.height = height;
            Aggressiveness = aggressiveness;
        }

        public void Compute(Texture shaded, Texture velocity)
        {
            Result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.R32ui);
            shaded.BindToImageUnit(1, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba16f);
            velocity.BindToUnit(0);
            shaded.BindToUnit(1);

            shaderProgram.Use();
            GL.DispatchCompute((width + TILE_SIZE - 1) / TILE_SIZE, (height + TILE_SIZE - 1) / TILE_SIZE, 1);
            // GL.NV.ShadingRateImageBarrier(true);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }   

        public void SetSize(int width, int height)
        {
            // Shading rate texture must be immutable by spec so recreate the whole texture
            Result.Dispose();
            Result = new Texture(TextureTarget2d.Texture2D);
            Result.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            Result.ImmutableAllocate(width / 16, height / 16, 1, SizedInternalFormat.R32ui);

            this.width = width;
            this.height = height;
        }
    }
}
