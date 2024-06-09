using OpenTK.Graphics.OpenGL;

namespace BBOpenGL
{
    public partial class BBG
    {
        public class Sampler : IDisposable
        {
            public enum MinFilter : uint
            {
                Nearest = TextureMinFilter.Nearest,
                Linear = TextureMinFilter.Linear,

                NearestMipmapNearest = TextureMinFilter.NearestMipmapNearest,
                LinearMipmapNearest = TextureMinFilter.LinearMipmapNearest,

                NearestMipmapLinear = TextureMinFilter.NearestMipmapLinear,
                LinearMipmapLinear = TextureMinFilter.LinearMipmapLinear,
            }
            public enum MagFilter : uint
            {
                Nearest = TextureMagFilter.Nearest,
                Linear = TextureMagFilter.Linear,
            }
            public enum WrapMode : uint
            {
                Repeat = TextureWrapMode.Repeat,
                ClampToEdge = TextureWrapMode.ClampToEdge,
            }
            public enum Anisotropy : uint
            {
                Samples1x = 1,
                Samples2x = 2,
                Samples4x = 4,
                Samples8x = 8,
                Samples16x = 16,
            }
            public enum CompareMode : uint
            {
                None = TextureCompareMode.None,
                CompareRefToTexture = TextureCompareMode.CompareRefToTexture
            }
            public enum CompareFunc : uint
            {
                Always = All.Always,
                Less = All.Less
            }

            public struct SamplerState
            {
                public MinFilter MinFilter;
                public MagFilter MagFilter;

                public WrapMode WrapModeS;
                public WrapMode WrapModeT;
                public WrapMode WrapModeR;

                public Anisotropy Anisotropy;

                public CompareMode CompareMode;
                public CompareFunc CompareFunc;

                public SamplerState()
                {
                    MinFilter = MinFilter.Nearest;
                    MagFilter = MagFilter.Nearest;
                    WrapModeS = WrapMode.ClampToEdge;
                    WrapModeT = WrapMode.ClampToEdge;
                    WrapModeR = WrapMode.ClampToEdge;
                    Anisotropy = Anisotropy.Samples1x;
                    CompareMode = CompareMode.None;
                    CompareFunc = CompareFunc.Always;
                }
            }

            public ref readonly SamplerState State => ref samplerState;

            private SamplerState samplerState;
            public readonly int ID;

            public Sampler(in SamplerState state)
            {
                GL.CreateSamplers(1, ref ID);
                SetState(state);
            }

            public void SetState(in SamplerState state)
            {
                GL.SamplerParameteri(ID, SamplerParameterI.TextureMinFilter, (int)state.MinFilter);
                GL.SamplerParameteri(ID, SamplerParameterI.TextureMagFilter, (int)state.MagFilter);

                GL.SamplerParameteri(ID, SamplerParameterI.TextureWrapS, (int)state.WrapModeS);
                GL.SamplerParameteri(ID, SamplerParameterI.TextureWrapT, (int)state.WrapModeT);
                GL.SamplerParameteri(ID, SamplerParameterI.TextureWrapR, (int)state.WrapModeR);

                GL.SamplerParameterf(ID, SamplerParameterF.TextureMaxAnisotropy, (float)state.Anisotropy);

                GL.SamplerParameteri(ID, SamplerParameterI.TextureCompareMode, (int)state.CompareMode);
                GL.SamplerParameteri(ID, SamplerParameterI.TextureCompareFunc, (int)state.CompareFunc);

                samplerState = state;
            }

            public void Dispose()
            {
                GL.DeleteSampler(ID);
            }

            public static bool IsMipmapFilter(MinFilter minFilter)
            {
                return minFilter == MinFilter.NearestMipmapNearest ||
                       minFilter == MinFilter.LinearMipmapNearest ||
                       minFilter == MinFilter.NearestMipmapLinear ||
                       minFilter == MinFilter.LinearMipmapLinear;
            }
        }

    }
}
