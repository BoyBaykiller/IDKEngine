﻿using System.Text;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using BBLogger;

namespace BBOpenGL;

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
        public Shader(ShaderStage stage, string shaderSource, string name = null)
        {
            ShaderStage = stage;

            ID = GL.CreateShader((ShaderType)stage);

            Name = name ?? stage.ToString();

            GL.ShaderSource(ID, shaderSource);
            GL.CompileShader(ID);

            GL.GetShaderInfoLog(ID, out string shaderInfoLog);
            if (shaderInfoLog != string.Empty)
            {
                bool compiled = IsCompiledSuccessfully();
                Logger.LogLevel level = compiled ? Logger.LogLevel.Info : Logger.LogLevel.Error;

                Logger.Log(level, $"Shader \"{Name}\" log:\n{shaderInfoLog}");
            }
        }

        public bool IsCompiledSuccessfully()
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
            string unprocessedSource = File.ReadAllText(Path.Combine(SHADER_PATH, localShaderPath));
            AbstractShader result = new AbstractShader(shaderStage, unprocessedSource, localShaderPath, debugSaveAndRunRGA);

            return result;
        }

        private AbstractShader(ShaderStage shaderStage, string source, string localShaderPath, bool debugSaveAndRunRGA = false)
            : base(shaderStage, Preprocessor.PreProcess(source, shaderStage, AbstractShaderProgram.GlobalShaderInsertions, localShaderPath), localShaderPath)
        {
            if (debugSaveAndRunRGA)
            {
                // This is only relevant for development.
                // Runs Radeon GPU Analyzer on the processed shader code and writes the results and code to disk

                string preprocessedShaderPath = Path.Combine(SHADER_PATH, "bin", localShaderPath);
                string shaderCode = Preprocessor.PreProcess(source, shaderStage, AbstractShaderProgram.GlobalShaderInsertions, localShaderPath);

                string outDir = Path.GetDirectoryName(preprocessedShaderPath);
                Directory.CreateDirectory(outDir);

                File.WriteAllText(preprocessedShaderPath, shaderCode);

                if (IsCompiledSuccessfully())
                {
                    __DebugSaveAndRunRGA(shaderStage, preprocessedShaderPath);
                }
            }

            LocalShaderPath = localShaderPath;
            DebugSaveAndRunRGA = debugSaveAndRunRGA;
        }

        public static AbstractShader Recompile(AbstractShader existingShader)
        {
            AbstractShader newShader = FromFile(existingShader.ShaderStage, existingShader.LocalShaderPath, existingShader.DebugSaveAndRunRGA);
            return newShader;
        }

        /// <summary>
        /// This is only meant to be used in development. It invokes the "rga" tool for shader analysis
        /// </summary>
        private static void __DebugSaveAndRunRGA(ShaderStage shaderStage, string shaderPath)
        {
            const string SHADER_ANALYZER_TOOL_NAME = "rga"; // https://github.com/GPUOpen-Tools/radeon_gpu_analyzer

            string rgaShaderStage = shaderStage switch
            {
                ShaderStage.Vertex => "vert",
                ShaderStage.Geometry => "geom",
                ShaderStage.Fragment => "frag",
                ShaderStage.Compute => "comp",
                _ => throw new NotSupportedException($"Can not convert {nameof(shaderStage)} = {shaderStage} to {nameof(rgaShaderStage)}"),
            };

            string outDir = Path.GetDirectoryName(shaderPath);

            string arguments = $"-s opengl -c gfx1010 --{rgaShaderStage} {shaderPath} " +
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

        public static class Preprocessor
        {
            public static readonly bool SUPPORTS_LINE_DIRECTIVE_SOURCEFILE = GetGLSLLineSourcefileSupport(out EXTENSION_NAME_LINE_DIRECTIVE_SOURCEFILE);
            private static readonly string? EXTENSION_NAME_LINE_DIRECTIVE_SOURCEFILE; // The required GLSL-extension that enables #line "filename" or null if none

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

            public static string PreProcess(string source, ShaderStage shaderStage, IReadOnlyDictionary<string, string> shaderInsertions, string name = null)
            {
                return PreProcess(source, shaderStage, shaderInsertions, out _, name);
            }

            public static string PreProcess(string source, ShaderStage shaderStage, IReadOnlyDictionary<string, string> shaderInsertions, out PreProcessInfo preProcessInfo, string name = null)
            {
                List<string> usedAppInsertions = new List<string>();
                List<string> pathsAlreadyIncluded = new List<string>();

                string result = ResolveKeywordsRecursive(source, name).ToString();
                result = RemoveUnusedShaderStorageBlocks(result).ToString();

                Match match = Regex.Match(result, "#version .*\n*"); // detect GLSL version statement up to line break
                int afterVersionStatement = match.Index + match.Length; // 0 if not found
                int versionStatementLineCount = CountLines(result, afterVersionStatement);
                result = result.Insert(afterVersionStatement,
                    $"""
                    #if {(EXTENSION_NAME_LINE_DIRECTIVE_SOURCEFILE != null ? 1 : 0)}
                    #extension {EXTENSION_NAME_LINE_DIRECTIVE_SOURCEFILE} : enable
                    #endif

                    // Keep in sync between shader and client code!
                    #define {GetShaderStageInsertion(shaderStage)} 1
                    #define {GetVendorInsertion()} 1
                    
                    const uint MIN_SUBGROUP_SIZE = {GetMinSubgroupSize()};

                    #extension GL_ARB_bindless_texture : require
                    #extension GL_EXT_shader_image_load_formatted : require
                    #line {versionStatementLineCount + 1}

                    """
                );

                preProcessInfo.UsedAppInsertionKeys = usedAppInsertions.ToArray();
                return result;

                StringBuilder ResolveKeywordsRecursive(string source, string name = null)
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
                            string path = Path.Combine(SHADER_PATH, userKey);
                            string includedText;
                            if (pathsAlreadyIncluded.Contains(path))
                            {
                                includedText = $"// Omitted including \"{path}\" as it's already part of this file";
                                result.AppendLine(includedText);
                            }
                            else
                            {
                                includedText = File.ReadAllText(path);
                                pathsAlreadyIncluded.Add(path);

                                string lineDirective = "#line 1";
                                if (SUPPORTS_LINE_DIRECTIVE_SOURCEFILE)
                                {
                                    lineDirective += $" \"{path}\"";
                                }
                                lineDirective += $" // Including \"{path}\"";
                                result.AppendLine(lineDirective);

                                result.Append(ResolveKeywordsRecursive(includedText, path));
                                result.Append('\n');

                                string originalLine = $"#line {CountLines(source, currentIndex) + 1}";
                                string safeSourceName = name ?? "No source name given";
                                if (SUPPORTS_LINE_DIRECTIVE_SOURCEFILE)
                                {
                                    originalLine += $" \"{safeSourceName}\"";
                                }
                                originalLine += $" // Included \"{path}\"";
                                result.AppendLine(originalLine);
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

                if (numReferencedDeclarations >= 16)
                {
                    Logger.Log(Logger.LogLevel.Warn, """
                        The number of shader storage blocks referenced by the shader exceeds the limit
                        in current NVIDIA drivers: https://forums.developer.nvidia.com/t/increase-maximum-allowed-shader-storage-blocks/293755/1
                        This shader will fail to compile on NVIDIA GPUs!
                        """);
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

            public static int CountLines(string searchString, int count, int startIndex = 0)
            {
                int lineCount = 0;

                int end = startIndex + count;
                while (startIndex < end)
                {
                    startIndex = searchString.IndexOf('\n', startIndex + 1);
                    if (startIndex == -1)
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
                if (shader.IsCompiledSuccessfully())
                {
                    return true;
                }
                else
                {
                    Logger.Log(Logger.LogLevel.Warn, """
                        #line \"filename\" is not supported and errors in
                        included GLSL shader code will not have the correct filename. The line will still be correct.
                        """);

                    return false;
                }
            }

            private static string GetShaderStageInsertion(ShaderStage shaderStage)
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

            private static string GetVendorInsertion()
            {
                GpuVendor vendor = GetGpuVendor();
                string insertion = vendor switch
                {
                    GpuVendor.AMD => "AMD",
                    GpuVendor.INTEL => "INTEL",
                    GpuVendor.NVIDIA => "NVIDIA",
                    GpuVendor.UNKNOWN => "UNKNOWN",
                    _ => throw new NotSupportedException($"Can not convert {nameof(vendor)} = {vendor} to {nameof(insertion)}"),
                };

                return $"APP_VENDOR_{insertion}";
            }

            private static uint GetMinSubgroupSize()
            {
                GpuVendor vendor = GetGpuVendor();
                if (vendor == GpuVendor.NVIDIA)
                {
                    return 32; // NVIDIA always has 32
                }
                if (vendor == GpuVendor.AMD)
                {
                    return 32; // AMD can run shaders in both wave32 or wave64 mode
                }
                else
                {
                    return 8; // Intel can go as low as 8 (this is also for anything else) 
                }
            }
        }
    }
}
