using System;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Shapes;
using IDKEngine.OpenGL;

namespace IDKEngine.Render
{
    class BoxRenderer : IDisposable
    {
        private readonly AbstractShaderProgram shaderProgram;
        private readonly Framebuffer fbo;
        public BoxRenderer()
        {
            shaderProgram = new AbstractShaderProgram(
                new AbstractShader(ShaderType.VertexShader, "Box/vertex.glsl"),
                new AbstractShader(ShaderType.FragmentShader, "Box/fragment.glsl"));

            fbo = new Framebuffer();
        }

        public void Render(Texture result, Matrix4 matrix, in Box box)
        {
            fbo.SetRenderTarget(FramebufferAttachment.ColorAttachment0, result);

            GL.Viewport(0, 0, result.Width, result.Height);

            GL.DepthFunc(DepthFunction.Always);
            GL.Disable(EnableCap.CullFace);
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);

            shaderProgram.Upload(0, box.Min);
            shaderProgram.Upload(1, box.Max);
            shaderProgram.Upload(2, matrix);

            fbo.Bind();
            shaderProgram.Use();
            GL.DrawArrays(PrimitiveType.Quads, 0, 24);

            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        }

        public void Dispose()
        {
            shaderProgram.Dispose();
            fbo.Dispose();
        }
    }
}
