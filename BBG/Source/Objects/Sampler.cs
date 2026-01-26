using OpenTK.Graphics.OpenGL;

namespace BBOpenGL;

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
            MirroredRepeat = TextureWrapMode.MirroredRepeat,
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

        public record struct SamplerState
        {
            public MinFilter MinFilter = MinFilter.Nearest;
            public MagFilter MagFilter = MagFilter.Nearest;

            public WrapMode WrapModeS = WrapMode.ClampToEdge;
            public WrapMode WrapModeT = WrapMode.ClampToEdge;
            public WrapMode WrapModeR = WrapMode.ClampToEdge;

            public Anisotropy Anisotropy = Anisotropy.Samples1x;

            public CompareMode CompareMode = CompareMode.None;
            public CompareFunc CompareFunc = CompareFunc.Always;

            public SamplerState()
            {
            }
        }

        public ref readonly SamplerState State => ref samplerState;

        public int ID { get; private set; }
        private SamplerState samplerState;

        public Sampler(in SamplerState state)
        {
            int id = 0;
            GL.CreateSamplers(1, ref id);

            ID = id;
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

        public bool IsDeleted()
        {
            return ID == 0;
        }

        public void Dispose()
        {
            if (IsDeleted())
            {
                return;
            }

            GL.DeleteSampler(ID);
            ID = 0;
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
