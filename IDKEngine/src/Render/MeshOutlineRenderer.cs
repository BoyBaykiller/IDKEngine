using System;
using System.IO;
using OpenTK.Graphics.OpenGL4;
using IDKEngine.Render.Objects;

namespace IDKEngine.Render
{
    class MeshOutlineRenderer : IDisposable
    {
        private int _meshIndex = -1;
        /// <summary>
        /// Negative numbers disable outline rendering. Otherwise out of bounds is undefined.
        /// </summary>
        public int MeshIndex
        {
            get => _meshIndex;

            set
            {
                _meshIndex = value;
                shaderProgram.Upload(0, _meshIndex);
            }
        }

        private readonly ShaderProgram shaderProgram;
        public MeshOutlineRenderer()
        {
            shaderProgram = new ShaderProgram(
                new Shader(ShaderType.VertexShader, File.ReadAllText("res/shaders/MeshOutline/vertex.glsl")),
                new Shader(ShaderType.FragmentShader, File.ReadAllText("res/shaders/MeshOutline/fragment.glsl")));
        }
        public void Render(Texture result)
        {
            if (_meshIndex == -1)
                return;

            result.BindToImageUnit(0, 0, false, 0, TextureAccess.WriteOnly, result.SizedInternalFormat);

            GL.ColorMask(false, false, false, false);
            GL.DepthMask(false);
            GL.DepthFunc(DepthFunction.Always);
            GL.Disable(EnableCap.CullFace);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);

            shaderProgram.Use();
            GL.DrawArrays(PrimitiveType.Quads, 0, 24);

            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            GL.Enable(EnableCap.CullFace);
            GL.DepthFunc(DepthFunction.Less);
            GL.DepthMask(true);
            GL.ColorMask(true, true, true, true);
        }

        public void Dispose()
        {
            shaderProgram.Dispose();
        }
    }
}
