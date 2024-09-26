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
            public Shader(ShaderStage stage, string source, string name = null)
            {
                ShaderStage = stage;

                ID = GL.CreateShader((ShaderType)stage);

                Name = name ?? stage.ToString();

                GL.ShaderSource(ID, source);
                GL.CompileShader(ID);

                GL.GetShaderInfoLog(ID, out string shaderInfoLog);
                if (shaderInfoLog != string.Empty)
                {
                    bool compiled = GetCompileStatus();
                    Logger.LogLevel level = compiled ? Logger.LogLevel.Info : Logger.LogLevel.Error;

                    Logger.Log(level, $"Shader \"{Name}\" log:\n{shaderInfoLog}");
                }
            }

            public bool GetCompileStatus()
            {
                GL.GetShaderi(ID, ShaderParameterName.CompileStatus, out int success);
                return success == 1;
            }

            public void Dispose()
            {
                GL.DeleteShader(ID);
            }
        }

        public class AbstractShader : Shader
        {
            public static string SHADER_PATH = "Resource/Shaders/";

            public string FullShaderPath => Path.Combine(SHADER_PATH, LocalShaderPath);

            public readonly string LocalShaderPath;
            public readonly bool DebugSaveAndRunRGA;

            public static AbstractShader FromFile(ShaderStage shaderStage, string localShaderPath, bool debugSaveAndRunRGA = false)
            {
                string source = File.ReadAllText(Path.Combine(SHADER_PATH, localShaderPath));
                AbstractShader result = new AbstractShader(shaderStage, source, localShaderPath, debugSaveAndRunRGA);

                return result;
            }

            private AbstractShader(ShaderStage shaderStage, string source, string localShaderPath, bool debugSaveAndRunRGA = false)
                : base(shaderStage, Preprocessor.PreProcess(source, AbstractShaderProgram.GlobalShaderInsertions, shaderStage, localShaderPath), localShaderPath)
            {
                if (debugSaveAndRunRGA && GetCompileStatus())
                {
                    // This is only relevant for development.
                    // Runs Radeon GPU Analyzer on the shader code and writes the results + preprocessed code to disk
                    string outPath = Path.Combine(SHADER_PATH, "bin", localShaderPath);
                    string shaderCode = Preprocessor.PreProcess(source, AbstractShaderProgram.GlobalShaderInsertions, shaderStage, localShaderPath);
                    __DebugSaveAndRunRGA(shaderStage, shaderCode, outPath);
                }
                LocalShaderPath = localShaderPath;
                DebugSaveAndRunRGA = debugSaveAndRunRGA;
            }

            public static class Preprocessor
            {
                public static readonly bool SUPPORTS_LINE_SOURCEFILE = GetGLSLLineSourcefileSupport(out GLSL_EXTENSION_NAME_LINE_SOURCEFILE);
                private static readonly string? GLSL_EXTENSION_NAME_LINE_SOURCEFILE; // The required GLSL-extension that enables #line "filename" or null if none

                public enum Keyword : int
                {
                    None,
                    AppInsert,
                    AppInclude,
                }

                public record struct PreProcessInfo
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

                    string result = RecursiveResolveKeywords(source, name).ToString();
                    result = RemoveUnusedShaderStorageBlocks(result).ToString();

                    Match match = Regex.Match(result, "#version .*\n*"); // detect GLSL version statement up to line break
                    int afterVersionStatement = match.Index + match.Length; // 0 if not found
                    int versionStatementLineCount = CountLines(result, afterVersionStatement);
                    result = result.Insert(afterVersionStatement,
                        $"""
                        #if {(GLSL_EXTENSION_NAME_LINE_SOURCEFILE != null ? 1 : 0)}
                        #extension {GLSL_EXTENSION_NAME_LINE_SOURCEFILE} : enable
                        #endif

                        // Keep in sync between shader and client code!
                        #define {ShaderStageShaderInsertion(shaderStage)} 1
                        #define {VendorToShaderInsertion(GetDeviceInfo().Vendor)} 1
                        #line {versionStatementLineCount + 1}

                        #extension GL_ARB_bindless_texture : require
                        #extension GL_EXT_shader_image_load_formatted : require

                        """
                    );

                    preProcessInfo.UsedAppInsertionKeys = usedAppInsertions.ToArray();
                    return result;

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
                                    if (SUPPORTS_LINE_SOURCEFILE)
                                    {
                                        newLine += $" \"{path}\"";
                                    }
                                    newLine += $" // Including \"{path}\"";
                                    result.AppendLine(newLine);

                                    result.Append(RecursiveResolveKeywords(includedText, path));
                                    result.Append('\n');

                                    string origionalLine = $"#line {lineCount + 1}";
                                    string safeSourceName = name ?? "No source name given";
                                    if (SUPPORTS_LINE_SOURCEFILE)
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

                /// <summary>
                /// Removes GLSL Shader Storage Blocks declaration which are not referenced by their interface name.
                /// This function exists to avoid hitting the limit of 16 Shader Storage Blocks on NVIDIA
                /// https://forums.developer.nvidia.com/t/increase-maximum-allowed-shader-storage-blocks/293755/1.
                /// Note that it messes up "#line X" preprocessor statements, but it is assumed that
                /// all Shader Storage Blocks are defined in their own file that is included so it only affects that file.
                /// </summary>
                /// <param name="text"></param>
                /// <returns></returns>
                private static StringBuilder RemoveUnusedShaderStorageBlocks(string text)
                {
                    StringBuilder result = new StringBuilder(text.Length);
                    Regex searchDeclaration = new Regex(@"layout\s*\([^)]*\)\s*(?:\b\w+\b\s*)*buffer\b[\s\S]*?}\s*(\w+)\s*;\s*");
                    int numReferencedDeclarations = 0;

                    int currentIndex = 0;
                    while (true)
                    {
                        Match match = searchDeclaration.Match(text, currentIndex);
                        if (match.Success)
                        {
                            Group shaderStorageBlock = match.Groups[0];
                            Group instanceName = match.Groups[1];

                            bool instanceNameReferenced = new Regex($@"\b{instanceName.Value}\.").Match(text, currentIndex).Success;
                            int end = shaderStorageBlock.Index;
                            if (instanceNameReferenced)
                            {
                                end += shaderStorageBlock.Length;
                                numReferencedDeclarations++;
                            }
                            result.Append(text, currentIndex, end - currentIndex);

                            currentIndex = shaderStorageBlock.Index + shaderStorageBlock.Length;
                        }
                        else
                        {
                            result.Append(text, currentIndex, text.Length - currentIndex);
                            break;
                        }
                    }

                    return result;
                }

                private static int AdvanceToNextKeyword(string source, int startIndex, out int expressionLength, out Keyword keyword, out string value)
                {
                    expressionLength = 0;
                    keyword = Keyword.None;
                    value = null;

                    Regex searchKeywords = new Regex(@$"(?<!\/\/.*?)({Keyword.AppInsert}|{Keyword.AppInclude})\((.*?)\)");
                    Match match = searchKeywords.Match(source, startIndex);
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

                private static bool GetGLSLLineSourcefileSupport(out string? requiredGLSLExtension)
                {
                    ref readonly DeviceInfo device = ref GetDeviceInfo();

                    if (device.ExtensionSupport.ShadingLanguageInclude)
                    {
                        requiredGLSLExtension = "GL_ARB_shading_language_include";
                        return true;
                    }

                    // On Windows-AMD with current drivers (24.6.1) includes with source file require
                    // this extension which is supported but not advertised by the driver so we check for it empirically
                    requiredGLSLExtension = "GL_GOOGLE_cpp_style_line_directive";
                    string shaderString =
                        $$"""
                        #version 460 core
                        
                        #extension {{requiredGLSLExtension}} : require

                        #line 1 "Example-Filename"
                        
                        void main() {}
                        """;

                    Shader shader = new Shader(ShaderStage.Fragment, shaderString, "Check for (#line \"filename\") support");
                    if (shader.GetCompileStatus())
                    {
                        return true;
                    }
                    else
                    {
                        Logger.Log(Logger.LogLevel.Warn, """
                            If you see this message (#line \"filename\") is not supported and errors in
                            included GLSL shader code will not have the correct filename. The line will still be correct.
                            """);

                        return false;
                    }
                }

                private static string ShaderStageShaderInsertion(ShaderStage shaderStage)
                {
                    string insertion = shaderStage switch
                    {
                        ShaderStage.Vertex => "VERTEX",
                        ShaderStage.Geometry => "GEOMETRY",
                        ShaderStage.Fragment => "FRAGMENT",
                        ShaderStage.Compute => "COMPUTE",
                        ShaderStage.TaskNV => "TASK",
                        ShaderStage.MeshNV => "MESH",
                        _ => throw new NotSupportedException($"Can not convert {nameof(shaderStage)} = {shaderStage} to {nameof(insertion)}"),
                    };

                    return $"APP_SHADER_STAGE_{insertion}";
                }

                private static string VendorToShaderInsertion(GpuVendor gpuVendor)
                {
                    string insertion = gpuVendor switch
                    {
                        GpuVendor.AMD => "AMD",
                        GpuVendor.INTEL => "INTEL",
                        GpuVendor.NVIDIA => "NVIDIA",
                        GpuVendor.Unknown => "UNKNOWN",
                        _ => throw new NotSupportedException($"Can not convert {nameof(gpuVendor)} = {gpuVendor} to {nameof(insertion)}"),
                    };

                    return $"APP_VENDOR_{insertion}";
                }
            }

            /// <summary>
            /// This is only meant to be used in development. It invokes the "rga" tool for shader analysis
            /// </summary>
            private static void __DebugSaveAndRunRGA(ShaderStage shaderStage, string shaderCode, string outPath)
            {
                const string SHADER_ANALYZER_TOOL_NAME = "rga"; // https://github.com/GPUOpen-Tools/radeon_gpu_analyzer

                string rgaShaderStage = shaderStage switch
                {
                    ShaderStage.Vertex => "--vert",
                    ShaderStage.Geometry => "--geom",
                    ShaderStage.Fragment => "--frag",
                    ShaderStage.Compute => "--comp",
                    _ => throw new NotSupportedException($"Can not convert {nameof(shaderStage)} = {shaderStage} to {nameof(rgaShaderStage)}"),
                };

                string outDir = Path.GetDirectoryName(outPath);
                Directory.CreateDirectory(outDir);

                File.WriteAllText(outPath, shaderCode);

                string arguments = $"-s opengl -c gfx1010 {rgaShaderStage} {outPath} " +
                                   $"--isa {Path.Combine(outDir, "isa_output.txt")} " +
                                   $"--livereg {Path.Combine(outDir, "livereg_report.txt")} ";
                
                System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo()
                {
                    FileName = SHADER_ANALYZER_TOOL_NAME,
                    Arguments = arguments,
                };

                try
                {
                    System.Diagnostics.Process? proc = System.Diagnostics.Process.Start(startInfo);

                    proc.Start();
                }
                catch (Exception)
                {
                    Logger.Log(Logger.LogLevel.Error, $"Failed to create process. Be sure to provide a {SHADER_ANALYZER_TOOL_NAME} binary (https://github.com/GPUOpen-Tools/radeon_gpu_analyzer) in working dir or PATH");
                }
            }
        }
    }
}
