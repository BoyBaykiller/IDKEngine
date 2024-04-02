using System;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.OpenGL
{
    class SamplerObject : IDisposable
    {
        public readonly int ID;
        public SamplerObject()
        {
            GL.CreateSamplers(1, out ID);
        }

        public void SetSamplerParamter(SamplerParameterName samplerParameterName, int param)
        {
            GL.SamplerParameter(ID, samplerParameterName, param);
        }
        public void SetSamplerParamter(SamplerParameterName samplerParameterName, int[] param)
        {
            GL.SamplerParameter(ID, samplerParameterName, param);
        }

        public void SetSamplerParamter(SamplerParameterName samplerParameterName, float param)
        {
            GL.SamplerParameter(ID, samplerParameterName, param);
        }
        public void SetSamplerParamter(SamplerParameterName samplerParameterName, float[] param)
        {
            GL.SamplerParameter(ID, samplerParameterName, param);
        }

        public void Bind(int unit)
        {
            GL.BindSampler(unit, ID);
        }
        public static void MultiBind(int first, int[] units)
        {
            GL.BindSamplers(first, units.Length, units);
        }

        public void Dispose()
        {
            GL.DeleteSampler(ID);
        }
    }
}
