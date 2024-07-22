using System;
using OpenTK.Mathematics;
using BBOpenGL;
using IDKEngine.Shapes;

namespace IDKEngine.Render
{
    class BoxRenderer : IDisposable
    {
        private readonly BBG.AbstractShaderProgram shaderProgram;
        public BoxRenderer()
        {
            shaderProgram = new BBG.AbstractShaderProgram(
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Vertex, "Box/vertex.glsl"),
                BBG.AbstractShader.FromFile(BBG.ShaderStage.Fragment, "Box/fragment.glsl"));
        }

        public void Render(BBG.Texture result, Matrix4 matrix, Box box)
        {
            BBG.Rendering.Render("Bounding Box", new BBG.Rendering.RenderAttachments()
            {
                ColorAttachments = new BBG.Rendering.ColorAttachments()
                {
                    Textures = [result],
                    AttachmentLoadOp = BBG.Rendering.AttachmentLoadOp.Load,
                }
            }, new BBG.Rendering.GraphicsPipelineState()
            {
                DepthFunction = BBG.Rendering.DepthFunction.Always,
                FillMode = BBG.Rendering.FillMode.Line,
            }, () =>
            {
                shaderProgram.Upload(0, box.Min);
                shaderProgram.Upload(1, box.Max);
                shaderProgram.Upload(2, matrix);

                BBG.Cmd.UseShaderProgram(shaderProgram);

                BBG.Rendering.InferViewportSize();
                BBG.Rendering.DrawNonIndexed(BBG.Rendering.Topology.Quads, 0, 24);
            });
        }

        public void Dispose()
        {
            shaderProgram.Dispose();
        }
    }
}
