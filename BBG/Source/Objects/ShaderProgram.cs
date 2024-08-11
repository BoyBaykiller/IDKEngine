using System.Diagnostics;
using OpenTK.Mathematics;
using OpenTK.Graphics.OpenGL;
using BBLogger;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public class ShaderProgram : IDisposable
        {
            public readonly int ID;
            public ShaderProgram(Shader[] others)
            {
                ID = GL.CreateProgram();
                Link(others);
            }
            public ShaderProgram(Shader first, params Shader[] others)
                : this(others.Concat([first]).ToArray())
            {
            }

            public void Link(Shader first, params Shader[] others)
            {
                Shader[] shaders = others.Concat([first]).ToArray();
                Link(shaders);
            }

            public void Link(Shader[] shaders)
            {
                if (shaders.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < shaders.Length; i++)
                {
                    Shader shader = shaders[i];
                    if (shader.GetCompileStatus())
                    {
                        GL.AttachShader(ID, shaders[i].ID);
                    }
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
                    if (shader.GetCompileStatus())
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

            public unsafe void Upload(int location, in Matrix4 Matrix4, int count = 1, bool transpose = false)
            {
                fixed (float* ptr = &Matrix4.Row0.X)
                {
                    GL.ProgramUniformMatrix4fv(ID, location, count, transpose, ptr);
                }
            }

            public unsafe void Upload(int location, in Vector3 vector3, int count = 1)
            {
                fixed (float* ptr = &vector3.X)
                {
                    GL.ProgramUniform3fv(ID, location, count, ptr);
                }
            }

            public unsafe void Upload(int location, float x, int count = 1)
            {
                GL.ProgramUniform1fv(ID, location, count, &x);
            }
            public unsafe void Upload(string name, float x, int count = 1)
            {
                GL.ProgramUniform1fv(ID, GetUniformLocation(name), count, &x);
            }

            public unsafe void Upload(int location, int x, int count = 1)
            {
                GL.ProgramUniform1iv(ID, location, count, &x);
            }
            public unsafe void Upload(string name, int x, int count = 1)
            {
                GL.ProgramUniform1iv(ID, GetUniformLocation(name), count, &x);
            }

            public unsafe void Upload(int location, uint x, int count = 1)
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
            public AbstractShaderProgram(AbstractShader[] shaders)
                : base(shaders)
            {
                Shaders = shaders;
                globalInstances.Add(this);
            }

            public AbstractShaderProgram(AbstractShader first, params AbstractShader[] others)
                : this(others.Concat([first]).ToArray())
            {
            }

            public void Link(AbstractShader[] shaders)
            {
                DisposeShaders();
                Shaders = shaders;

                base.Link(shaders);
            }

            private void DisposeShaders()
            {
                if (Shaders != null)
                {
                    for (int i = 0; i < Shaders.Length; i++)
                    {
                        Shaders[i].Dispose();
                    }
                }
            }

            public new void Dispose()
            {
                DisposeShaders();
                globalInstances.Remove(this);

                base.Dispose();
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
                        Recompile(shaderProgram);

                        recompiledShadersNames += $"[{string.Join(", ", shaderProgram.Shaders.Select(shader => $"{shader.Name}"))}]";
                    }
                }

                if (recompiledShadersNames != string.Empty)
                {
                    Logger.Log(Logger.LogLevel.Info,
                           $"{nameof(AbstractShader.Preprocessor.Keyword.AppInclude)} \"{key}\" was assigned new value \"{value}\", " +
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
                    Recompile(shaderProgram);
                }
                sw.Stop();

                int numShaders = globalInstances.Sum(it => it.Shaders.Length);
                Logger.Log(Logger.LogLevel.Info, $"Parsed and recompiled {numShaders} shaders in {sw.ElapsedMilliseconds} milliseconds");
            }

            public static void Recompile(AbstractShaderProgram shaderProgram)
            {
                AbstractShader[] recompiledShaders = new AbstractShader[shaderProgram.Shaders.Length];
                for (int i = 0; i < recompiledShaders.Length; i++)
                {
                    AbstractShader existingShader = shaderProgram.Shaders[i];
                    recompiledShaders[i] = AbstractShader.FromFile(existingShader.ShaderStage, existingShader.LocalShaderPath, existingShader.DebugSaveAndRunRGA);
                }
                shaderProgram.Link(recompiledShaders);
            }
        }
    }
}