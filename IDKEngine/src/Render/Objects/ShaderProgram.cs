using System;
using System.Linq;
using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    readonly struct Shader : IDisposable
    {
        public readonly int ID;
        public readonly ShaderType ShaderType;

        public Shader(ShaderType shaderType, string sourceCode)
        {
            ShaderType = shaderType;
            
            ID = GL.CreateShader(shaderType);

            GL.ShaderSource(ID, sourceCode);
            GL.CompileShader(ID);

            string infoLog = GL.GetShaderInfoLog(ID);
            if (infoLog != string.Empty)
            {
                Logger.Log(Logger.LogLevel.Warn, infoLog);
            }
        }

        public void Dispose()
        {
            GL.DeleteShader(ID);
        }
    }

    class ShaderProgram : IDisposable
    {
        private static int lastBindedID = 0;

        public readonly int ID;
        public ShaderProgram(params Shader[] shaders)
        {
            ID = GL.CreateProgram();
            Link(shaders);
        }

        public void Link(params Shader[] shaders)
        {
            Debug.Assert(shaders.All(s => shaders.All(s1 => s.ID == s1.ID || s1.ShaderType != s.ShaderType)));

            for (int i = 0; i < shaders.Length; i++)
                GL.AttachShader(ID, shaders[i].ID);

            GL.LinkProgram(ID);

            string infoLog = GL.GetProgramInfoLog(ID);
            if (infoLog != string.Empty)
            {
                Logger.Log(Logger.LogLevel.Warn, infoLog);
            }

            for (int i = 0; i < shaders.Length; i++)
            {
                GL.DetachShader(ID, shaders[i].ID);
            }
        }

        public void Use()
        {
            if (lastBindedID != ID)
            {
                GL.UseProgram(ID);
                lastBindedID = ID;
            }
        }

        public static void Use(int id)
        {
            if (lastBindedID != id)
            {
                GL.UseProgram(id);
                lastBindedID = id;
            }
        }

        public unsafe void Upload(int location, in Matrix4 matrix4, int count = 1, bool transpose = false)
        {
            fixed (float* ptr = &matrix4.Row0.X)
            {
                GL.ProgramUniformMatrix4(ID, location, count, transpose, ptr);
            }
        }
        public unsafe void Upload(string name, in Matrix4 matrix4, int count = 1, bool transpose = false)
        {
            fixed (float* ptr = &matrix4.Row0.X)
            {
                GL.ProgramUniformMatrix4(ID, GetUniformLocation(name), count, transpose, ptr);
            }
        }

        public unsafe void Upload(int location, in Vector4 vector4, int count = 1)
        {
            fixed (float* ptr = &vector4.X)
            {
                GL.ProgramUniform4(ID, location, count, ptr);
            }
        }
        public unsafe void Upload(string name, in Vector4 vector4, int count = 1)
        {
            fixed (float* ptr = &vector4.X)
            {
                GL.ProgramUniform4(ID, GetUniformLocation(name), count, ptr);
            }
        }

        public unsafe void Upload(int location, in Vector3 vector3, int count = 1)
        {
            fixed (float* ptr = &vector3.X)
            {
                GL.ProgramUniform3(ID, location, count, ptr);
            }
        }
        public unsafe void Upload(string name, in Vector3 vector3, int count = 1)
        {
            fixed (float* ptr = &vector3.X)
            {
                GL.ProgramUniform3(ID, GetUniformLocation(name), count, ptr);
            }
        }

        public unsafe void Upload(int location, in Vector2 vector2, int count = 1)
        {
            fixed (float* ptr = &vector2.X)
            {
                GL.ProgramUniform2(ID, location, count, ptr);
            }
        }
        public unsafe void Upload(string name, in Vector2 vector2, int count = 1)
        {
            fixed (float* ptr = &vector2.X)
            {
                GL.ProgramUniform2(ID, GetUniformLocation(name), count, ptr);
            }
        }

        public void Upload(int location, float x, int count = 1)
        {
            GL.ProgramUniform1(ID, location, count, ref x);
        }
        public void Upload(string name, float x, int count = 1)
        {
            GL.ProgramUniform1(ID, GetUniformLocation(name), count, ref x);
        }

        public void Upload(int location, int x, int count = 1)
        {
            GL.ProgramUniform1(ID, location, count, ref x);
        }
        public void Upload(string name, int x, int count = 1)
        {
            GL.ProgramUniform1(ID, GetUniformLocation(name), count, ref x);
        }

        public void Upload(int location, uint x, int count = 1)
        {
            GL.ProgramUniform1((uint)ID, location, count, ref x);
        }
        public void Upload(string name, uint x, int count = 1)
        {
            GL.ProgramUniform1((uint)ID, GetUniformLocation(name), count, ref x);
        }

        public void Upload(int location, bool x)
        {
            Upload(location, x ? 1 : 0);
        }
        public void Upload(string name, bool x)
        {
            Upload(name, x ? 1 : 0);
        }

        public int GetUniformLocation(string name)
        {
            return GL.GetUniformLocation(ID, name);
        }

        public void Dispose()
        {
            GL.DeleteProgram(ID);
            if (ID == lastBindedID)
            {
                lastBindedID = 0;
            }
        }
    }
}