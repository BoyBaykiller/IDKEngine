using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public unsafe class TextureView : IDisposable
        {
            public readonly int ID;

            public TextureView(Texture texture, in Sampler.SamplerState samplerState, int level = 0)
            {
                GL.GenTextures(1, ref ID);
                GL.TextureView(ID, TextureTarget.Texture2d, texture.ID, (SizedInternalFormat)texture.Format, (uint)level, 1, 0, 1);

                GL.TextureParameteri(ID, TextureParameterName.TextureMinFilter, (int)samplerState.MinFilter);
                GL.TextureParameteri(ID, TextureParameterName.TextureMagFilter, (int)samplerState.MagFilter);
                
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapS, (int)samplerState.WrapModeS);
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapT, (int)samplerState.WrapModeT);
                GL.TextureParameteri(ID, TextureParameterName.TextureWrapR, (int)samplerState.WrapModeR);
                
                GL.TextureParameterf(ID, TextureParameterName.TextureMaxAnisotropy, (float)samplerState.Anisotropy);

                GL.TextureParameteri(ID, TextureParameterName.TextureCompareMode, (int)samplerState.CompareMode);
                GL.TextureParameteri(ID, TextureParameterName.TextureCompareFunc, (int)samplerState.CompareFunc);
            }

            public void Dispose()
            {
                GL.DeleteTexture(ID);
            }
        }
    }
}
