using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using BBLogger;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public unsafe class ShaderProgram : IDisposable
        {
            public readonly int ID;
            public ShaderProgram(Shader[] others)
            {
                ID = GL.CreateProgram();
                Link(others);
            }

            public void Link(Shader[] shaders)
            {
                if (shaders.Any(it => !it.IsCompiledSuccessfully()))
                {
                    return;
                }

                for (int i = 0; i < shaders.Length; i++)
                {
                    Shader shader = shaders[i];
                    GL.AttachShader(ID, shaders[i].ID);
                }

                GL.LinkProgram(ID);

                GL.GetProgramInfoLog(ID, out string infoLog);
                if (infoLog != string.Empty)
                {
                    string shaderNames = string.Join(", ", shaders.Select(shader => $"{shader.Name}").ToArray());

                    Logger.LogLevel level = GetLinkStatus() ? Logger.LogLevel.Info : Logger.LogLevel.Error;
                    Logger.Log(level, $"ShaderProgram [{shaderNames}] log:\n{infoLog}");
                }

                for (int i = 0; i < shaders.Length; i++)
                {
                    Shader shader = shaders[i];
                    if (shader.IsCompiledSuccessfully())
                    {
                        GL.DetachShader(ID, shaders[i].ID);
                    }
                }
            }

            public bool GetLinkStatus()
            {
                GL.GetProgrami(ID, ProgramProperty.LinkStatus, out int success);
                return success == 1;
            }

            public void Upload(int location, in Matrix4 matrix4, int count = 1, bool transpose = false)
            {
                fixed (Matrix4* ptr = &matrix4)
                {
                    GL.ProgramUniformMatrix4fv(ID, location, count, transpose, (float*)ptr);
                }
            }

            public void Upload(int location, Vector3 vector3, int count = 1)
            {
                GL.ProgramUniform3fv(ID, location, count, &vector3.X);
            }

            public void Upload(int location, in Vector4 vector4, int count = 1)
            {
                fixed (Vector4* ptr = &vector4)
                {
                    GL.ProgramUniform4fv(ID, location, count, (float*)ptr);
                }
            }

            public void Upload(int location, float x, int count = 1)
            {
                GL.ProgramUniform1fv(ID, location, count, &x);
            }
            public void Upload(string name, float x, int count = 1)
            {
                GL.ProgramUniform1fv(ID, GetUniformLocation(name), count, &x);
            }

            public void Upload(int location, int x, int count = 1)
            {
                GL.ProgramUniform1iv(ID, location, count, &x);
            }
            public void Upload(string name, int x, int count = 1)
            {
                GL.ProgramUniform1iv(ID, GetUniformLocation(name), count, &x);
            }

            public void Upload(int location, uint x, int count = 1)
            {
                GL.ProgramUniform1uiv(ID, location, count, &x);
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
            }
        }

        public class AbstractShaderProgram : ShaderProgram, IDisposable
        {
            private static readonly List<AbstractShaderProgram> globalInstances = new List<AbstractShaderProgram>();

            public AbstractShader[] Shaders { get; private set; }
            public AbstractShaderProgram(params AbstractShader[] shaders)
                : base(shaders)
            {
                Shaders = shaders;
                globalInstances.Add(this);
            }

            public void Recompile()
            {
                AbstractShader[] newShaders = new AbstractShader[Shaders.Length];
                for (int i = 0; i < newShaders.Length; i++)
                {
                    newShaders[i] = AbstractShader.Recompile(Shaders[i]);
                }

                if (newShaders.Any(it => !it.IsCompiledSuccessfully()))
                {
                    DisposeShaders(newShaders);
                    return;
                }

                DisposeShaders(Shaders);
                Shaders = newShaders;

                Link(Shaders);
            }


            public new void Dispose()
            {
                DisposeShaders(Shaders);
                globalInstances.Remove(this);

                base.Dispose();
            }

            private static void DisposeShaders(AbstractShader[] shaders)
            {
                for (int i = 0; i < shaders.Length; i++)
                {
                    shaders[i].Dispose();
                }
            }

            /// <summary>
            /// Responsible for managing global shader insertions like "AppInsert(USE_TLAS)".
            /// In that example the key is "USE_TLAS". If the application updates the value for that key
            /// all shaders will be recompiled and their programs linked again.
            /// </summary>
            public static IReadOnlyDictionary<string, string> GlobalShaderInsertions => globalShaderInsertions;

            private static readonly Dictionary<string, string> globalShaderInsertions = new Dictionary<string, string>();

            public static void SetShaderInsertionValue(string key, bool value)
            {
                SetShaderInsertionValue(key, value ? "1" : "0");
            }
            
            public static void SetShaderInsertionValue(string key, int value)
            {
                SetShaderInsertionValue(key, value.ToString());
            }
            
            public static void SetShaderInsertionValue(string key, string value)
            {
                if (globalShaderInsertions.TryGetValue(key, out string prevValue) && prevValue == value)
                {
                    return;
                }
                globalShaderInsertions[key] = value;

                string recompiledShadersNames = string.Empty;
                for (int i = 0; i < globalInstances.Count; i++)
                {
                    AbstractShaderProgram shaderProgram = globalInstances[i];

                    bool programIncludesAppInsertionKey = false;
                    for (int j = 0; j < shaderProgram.Shaders.Length; j++)
                    {
                        AbstractShader shader = shaderProgram.Shaders[j];

                        string srcCode = File.ReadAllText(shader.FullShaderPath);
                        AbstractShader.Preprocessor.PreProcess(srcCode, GlobalShaderInsertions, shader.ShaderStage, out AbstractShader.Preprocessor.PreProcessInfo preprocessInfo);

                        if (preprocessInfo.UsedAppInsertionKeys.Contains(key))
                        {
                            programIncludesAppInsertionKey = true;
                            break;
                        }
                    }

                    if (programIncludesAppInsertionKey)
                    {
                        shaderProgram.Recompile();

                        recompiledShadersNames += $"[{string.Join(", ", shaderProgram.Shaders.Select(shader => $"{shader.Name}"))}]";
                    }
                }

                if (recompiledShadersNames != string.Empty)
                {
                    Logger.Log(Logger.LogLevel.Info,
                           $"{nameof(AbstractShader.Preprocessor.Keyword.AppInsert)} \"{key}\" was assigned new value \"{value}\", " +
                           $"causing shader recompilation for {recompiledShadersNames}"
                       );
                }
            }

            public static void RecompileAll()
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < globalInstances.Count; i++)
                {
                    AbstractShaderProgram shaderProgram = globalInstances[i];
                    shaderProgram.Recompile();
                }
                sw.Stop();

                int numShaders = globalInstances.Sum(it => it.Shaders.Length);
                Logger.Log(Logger.LogLevel.Info, $"Parsed and recompiled {numShaders} shaders in {sw.ElapsedMilliseconds} milliseconds");
            }
        }
    }
}