using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class ForwardRenderer
    {
        public readonly LightManager LightingContext;

        public ForwardRenderer(LightManager lighter, int taaSamples)
        {
            Debug.Assert(taaSamples <= GLSLTaaData.GLSL_MAX_TAA_UBO_VEC2_JITTER_COUNT);

            LightingContext = lighter;
        }

        public void Draw(ModelSystem modelSystem, ShaderProgram depthOnlyProgram, ShaderProgram shadingProgram, ShaderProgram skyBoxProgram, Texture ambientOcclusion = null)
        {
            if (ambientOcclusion != null)
                ambientOcclusion.BindToUnit(0);
            else
                Texture.UnbindFromUnit(0);

            GL.ColorMask(false, false, false, false);
            depthOnlyProgram.Use();
            modelSystem.Draw();

            GL.ColorMask(true, true, true, true);
            GL.DepthFunc(DepthFunction.Equal);
            GL.DepthMask(false);
            shadingProgram.Use();
            modelSystem.Draw();

            GL.Disable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Lequal);

            skyBoxProgram.Use();
            GL.DrawArrays(PrimitiveType.Quads, 0, 24);

            GL.Enable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Less);
            GL.DepthMask(true);

            LightingContext.Draw();
        }
    }
}
