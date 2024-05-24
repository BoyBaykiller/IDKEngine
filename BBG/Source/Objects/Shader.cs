using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using BBLogger;

namespace BBOpenGL
{
    public static partial class BBG
    {
        public enum ShaderStage : uint
        {
            Vertex = ShaderType.VertexShader,
            Geometry = ShaderType.GeometryShader,
            Fragment = ShaderType.FragmentShader,
            Compute = ShaderType.ComputeShader,

            /// <summary>
            /// Requires GL_NV_mesh_shader
            /// </summary>
            TaskNV = All.TaskShaderNv,

            /// <summary>
            /// Requires GL_NV_mesh_shader
            /// </summary>
            MeshNV = All.MeshShaderNv,
        }

        public class Shader : IDisposable
        {
            public readonly int ID;
            public readonly ShaderStage ShaderStage;
            public readonly string Name;
            public Shader(ShaderStage stage, string srcCode, string name = null)
            {
                ShaderStage = stage;

                ID = GL.CreateShader((ShaderType)stage);

                Name = name ?? stage.ToString();

                GL.ShaderSource(ID, srcCode);
                GL.CompileShader(ID);

                GL.GetShaderInfoLog(ID, out string shaderInfoLog);
                if (shaderInfoLog != string.Empty)
                {
                    bool compiled = GetCompileStatus();
                    Logger.LogLevel level = compiled ? Logger.LogLevel.Info : Logger.LogLevel.Error;

                    Logger.Log(level, $"Shader {Name} log:\n{shaderInfoLog}");
                }
            }

            public bool GetCompileStatus()
            {
                int success = 0;
                GL.GetShaderi(ID, ShaderParameterName.CompileStatus, ref success);
                return success == 1;
            }

            public void Dispose()
            {
                GL.DeleteShader(ID);
            }
        }

        public class AbstractShader : Shader
        {
            public const string SHADER_PATH = "Ressource/Shaders/";

            public string FullShaderPath => Path.Combine(SHADER_PATH, LocalShaderPath);

            public readonly string LocalShaderPath;
            public AbstractShader(ShaderStage stage, string localShaderPath)
                : this(stage, File.ReadAllText(Path.Combine(SHADER_PATH, localShaderPath)), localShaderPath)
            {
                LocalShaderPath = localShaderPath;
            }

            private AbstractShader(ShaderStage stage, string srcCode, string name)
                : base(stage, Preprocessor.PreProcess(srcCode, AbstractShaderProgram.GlobalShaderInsertions, stage, name), name)
            {
                // We currently dont allow public construction from just a srcCode
                // as that would make the LocalShaderPath variable meaningless, which we need for shader recompilation.
            }

            public static class Preprocessor
            {
                public const bool SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH = false; // for debugging only (requires GL_GOOGLE_cpp_style_line_directive)

                public static string DEBUG_LAST_PRE_PROCESSED;

                public enum Keyword : int
                {
                    None,
                    AppInsert,
                    AppInclude,
                }

                public struct PreProcessInfo
                {
                    public string[] UsedAppInsertionKeys;
                }

                public static string PreProcess(string source, IReadOnlyDictionary<string, string> shaderInsertions, ShaderStage shaderStage, string name = null)
                {
                    return PreProcess(source, shaderInsertions, shaderStage, out _, name);
                }

                public static string PreProcess(string source, IReadOnlyDictionary<string, string> shaderInsertions, ShaderStage shaderStage, out PreProcessInfo preProcessInfo, string name = null)
                {
                    List<string> usedAppInsertions = new List<string>();
                    List<string> pathsAlreadyIncluded = new List<string>();
                    StringBuilder result = RecursiveResolveKeywords(source, name);

                    {
                        string copy = result.ToString();
                        Match match = Regex.Match(copy, "#version .*\n*"); // detect GLSL version statement up to line break
                        int afterVersionStatement = match.Index + match.Length; // 0 if not found

                        int lineCountVersionStatement = CountLines(copy, afterVersionStatement);

                        string toInsert =
                            $"""
                            #extension GL_ARB_bindless_texture : require
                            #extension GL_EXT_shader_image_load_formatted : enable
                            #if {(SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH ? 1 : 0)}
                                #extension GL_GOOGLE_cpp_style_line_directive : require
                            #endif
                            #define APP_SHADER_STAGE_{shaderStage.ToString().ToUpper()}
                            #line {lineCountVersionStatement + 1}

                            """;

                        result = result.Insert(afterVersionStatement, toInsert);
                    }

                    preProcessInfo.UsedAppInsertionKeys = usedAppInsertions.ToArray();
                    
                    string preprocessed = result.ToString();
                    DEBUG_LAST_PRE_PROCESSED = preprocessed;

                    return preprocessed;

                    StringBuilder RecursiveResolveKeywords(string source, string name = null)
                    {
                        StringBuilder result = new StringBuilder(source.Length);

                        int currentIndex = 0;
                        while (true)
                        {
                            int oldStartIndex = currentIndex;
                            currentIndex = AdvanceToNextKeyword(source, currentIndex, out int expressionLength, out Keyword keyword, out string userKey);

                            result.Append(source, oldStartIndex, currentIndex - oldStartIndex);

                            if (keyword == Keyword.None)
                            {
                                break;
                            }

                            if (keyword == Keyword.AppInsert)
                            {
                                if (!shaderInsertions.TryGetValue(userKey, out string appInsertionValue))
                                {
                                    const string defaultFallbackValue = "0";
                                    appInsertionValue = defaultFallbackValue;

                                    Logger.Log(Logger.LogLevel.Error, $"The application does not provide a glsl {keyword} value for {userKey}. {defaultFallbackValue} as fallback is inserted");
                                }

                                result.Append(appInsertionValue);
                                usedAppInsertions.Add(userKey);
                            }
                            else if (keyword == Keyword.AppInclude)
                            {
                                string path = SHADER_PATH + userKey;
                                if (!File.Exists(path))
                                {
                                    Logger.Log(Logger.LogLevel.Error, $"Include file {path} does not exist");
                                }
                                else
                                {
                                    string includedText;
                                    if (pathsAlreadyIncluded.Contains(path))
                                    {
                                        includedText = $"// Omitted including \"{path}\" as it's already part of this file";
                                    }
                                    else
                                    {
                                        includedText = File.ReadAllText(path);
                                        pathsAlreadyIncluded.Add(path);
                                    }

                                    int lineCount = CountLines(source, currentIndex);

                                    string newLine = "#line 1";
                                    if (SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH)
                                    {
                                        newLine += $" \"{path}\"";
                                    }
                                    newLine += $" // Including \"{path}\"";
                                    result.AppendLine(newLine);

                                    result.Append(RecursiveResolveKeywords(includedText, path));
                                    result.Append('\n');

                                    string origionalLine = $"#line {lineCount + 1}";
                                    string safeSourceName = name ?? "No source name given";
                                    if (SHADER_ERRORS_IN_INCLUDES_WITH_CORRECT_PATH)
                                    {
                                        origionalLine += $" \"{safeSourceName}\"";
                                    }
                                    origionalLine += $" // Included \"{path}\"";
                                    result.AppendLine(origionalLine);
                                }
                            }

                            currentIndex += expressionLength;
                        }
                        return result;
                    }
                }
                
                private static int AdvanceToNextKeyword(string source, int startIndex, out int expressionLength, out Keyword keyword, out string value)
                {
                    expressionLength = 0;
                    keyword = Keyword.None;
                    value = null;

                    Regex regex = new Regex(@$"(?<!\/\/.*?)({Keyword.AppInsert}|{Keyword.AppInclude})\((.*?)\)");
                    Match match = regex.Match(source, startIndex);
                    if (match.Success)
                    {
                        expressionLength = match.Groups[0].Length;
                        keyword = Enum.Parse<Keyword>(match.Groups[1].Value);
                        value = match.Groups[2].Value;

                        return match.Index;
                    }
                    else
                    {
                        return source.Length;
                    }
                }
                
                private static int CountLines(string searchString, int count, int startIndex = 0)
                {
                    int lineCount = 0;

                    int end = startIndex + count;
                    while (true)
                    {
                        startIndex = searchString.IndexOf('\n', startIndex + 1);
                        if (startIndex == -1 || startIndex > end)
                        {
                            break;
                        }

                        lineCount++;
                    }

                    return lineCount;
                }
            }
        }
    }
}
