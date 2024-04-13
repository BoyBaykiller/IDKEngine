using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.OpenGL
{
    class Sampler : IDisposable
    {
        public enum MinFilter : int
        {
            Nearest = TextureMinFilter.Nearest,
            Linear = TextureMinFilter.Linear,

            NearestMipmapNearest = TextureMinFilter.NearestMipmapNearest,
            LinearMipmapNearest = TextureMinFilter.LinearMipmapNearest,

            NearestMipmapLinear = TextureMinFilter.NearestMipmapLinear,
            LinearMipmapLinear = TextureMinFilter.LinearMipmapLinear,
        }
        public enum MagFilter : int
        {
            Nearest = TextureMagFilter.Nearest,
            Linear = TextureMagFilter.Linear,
        }
        public enum WrapMode : int
        {
            Repeat = TextureWrapMode.Repeat,
            ClampToEdge = TextureWrapMode.ClampToEdge,
        }
        public enum Anisotropy : int
        {
            Samples1x = 1,
            Samples2x = 2,
            Samples4x = 4,
            Samples8x = 8,
            Samples16x = 16,
        }
        public enum CompareMode : int
        {
            None = TextureCompareMode.None,
            CompareRefToTexture = TextureCompareMode.CompareRefToTexture
        }
        public enum CompareFunc : int
        {
            Always = All.Always,
            Less = All.Less
        }


        public struct State
        {
            public MinFilter MinFilter;
            public MagFilter MagFilter;

            public WrapMode WrapModeS;
            public WrapMode WrapModeT;
            public WrapMode WrapModeR;

            public Anisotropy Anisotropy;

            public CompareMode CompareMode;
            public CompareFunc CompareFunc;

            public State()
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

        private readonly State state;
        public readonly int ID;
        public Sampler(State state)
        {
            GL.CreateSamplers(1, out ID);
            SetState(state);
        }

        public void SetState(in State state)
        {
            GL.SamplerParameter(ID, SamplerParameterName.TextureMinFilter, (int)state.MinFilter);
            GL.SamplerParameter(ID, SamplerParameterName.TextureMagFilter, (int)state.MagFilter);

            GL.SamplerParameter(ID, SamplerParameterName.TextureWrapS, (int)state.WrapModeS);
            GL.SamplerParameter(ID, SamplerParameterName.TextureWrapT, (int)state.WrapModeT);
            GL.SamplerParameter(ID, SamplerParameterName.TextureWrapR, (int)state.WrapModeR);

            GL.SamplerParameter(ID, SamplerParameterName.TextureMaxAnisotropyExt, (float)state.Anisotropy);

            GL.SamplerParameter(ID, SamplerParameterName.TextureCompareMode, (int)state.CompareMode);
            GL.SamplerParameter(ID, SamplerParameterName.TextureCompareFunc, (int)state.CompareFunc);
        }

        public ref readonly State GetState()
        {
            return ref state;
        }

        public void Bind(int unit)
        {
            GL.BindSampler(unit, ID);
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
