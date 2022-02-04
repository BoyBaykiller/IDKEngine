using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class DepthOfField
    {
        private float _focalLength;
        public float FocalLength
        {
            set
            {
                _focalLength = value;
                shaderProgram.Upload("FocalLength", _focalLength);
            }

            get => _focalLength;
        }

        private float _apertureRadius;
        public float ApertureRadius
        {
            set
            {
                _apertureRadius = value;
                shaderProgram.Upload("ApertureRadius", _apertureRadius);
            }

            get => _apertureRadius;
        }


        private static readonly ShaderProgram shaderProgram = new ShaderProgram(
            new Shader(ShaderType.ComputeShader, File.ReadAllText("res/shaders/DOF/compute.glsl")));
        public DepthOfField(float focalLength, float apertureRadius)
        {
            FocalLength = focalLength;
            ApertureRadius = apertureRadius;
        }

        public unsafe void Compute(Texture depthTextureSrc, Texture unbluredSrc, Texture bluredSrc, Texture dest)
        {
            dest.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba16f);
            int* textures = stackalloc int[] { depthTextureSrc.ID, unbluredSrc.ID, bluredSrc.ID };
            Texture.MultiBindToUnit(0, 3, textures);

            shaderProgram.Use();
            GL.DispatchCompute((dest.Width + 8 - 1) / 8, (dest.Height + 4 - 1) / 4, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit);
        }
    }
}
