using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL4;

namespace IDKEngine.Render.Objects
{
    class Shader : IDisposable
    {
        private enum Keyword : int
        {
            NoKeyword,
            AppInsert,
            AppInclude,
        }

        public const string SHADER_PATH = "res/shaders/";

        public readonly int ID;
        public readonly ShaderType ShaderType;

        public static Shader ShaderFromFile(ShaderType shaderType, string localShaderPath, Dictionary<string, string> shaderInsertions = null)
        {
            string path = Path.Combine(SHADER_PATH, localShaderPath);
            string srcCode = File.ReadAllText(path);
            string name = path;
            Shader shader = new Shader(
                shaderType,
                srcCode,
                name,
                shaderInsertions
            );

            return shader;
        }

        public Shader(ShaderType shaderType, string srcCode, string srcName, Dictionary<string, string> shaderInsertions = null)
        {
            ShaderType = shaderType;

            ID = GL.CreateShader(shaderType);

            srcName ??= shaderType.ToString();
            string preprocessed = PreProcess(srcCode, shaderInsertions, srcName);

            GL.ShaderSource(ID, preprocessed);
            GL.CompileShader(ID);

            bool compiled = GetCompileStatus();
            string shaderInfoLog = GL.GetShaderInfoLog(ID);
            if (shaderInfoLog != string.Empty)
            {
                Logger.LogLevel level = compiled ? Logger.LogLevel.Info : Logger.LogLevel.Error;
                Logger.Log(level, shaderInfoLog);
            }
        }

        public bool GetCompileStatus()
        {
            GL.GetShader(ID, ShaderParameter.CompileStatus, out int success);
            return success == 1;
        }

        public void Dispose()
        {
            GL.DeleteShader(ID);
        }

        public const bool REPORT_SHADER_ERRORS_WITH_NAME = false; // for debugging only (requires GL_GOOGLE_cpp_style_line_directive)
        private static string PreProcess(string source, Dictionary<string, string> shaderInsertions, string sourceName)
        {
            StringBuilder result = ResolveKeywords(source, shaderInsertions, sourceName);

            if (REPORT_SHADER_ERRORS_WITH_NAME)
            {
                string copy = result.ToString();
                
                Match match = Regex.Match(copy, "#version .*\n*"); // detect GLSL version statement up to line break
                int afterVersionStatement = match.Index + match.Length; // 0 if not found

                int lineCountVersionStatement = CountLines(copy, afterVersionStatement);

                string toInsert =
                    $"""
                    #extension GL_GOOGLE_cpp_style_line_directive : require
                    #line {lineCountVersionStatement + 1} "{sourceName}"

                    """;

                result = result.Insert(afterVersionStatement, toInsert);
            }

            return result.ToString();

            static StringBuilder ResolveKeywords(string source, Dictionary<string, string> shaderInsertions, string sourceName)
            {
                StringBuilder result = new StringBuilder(source.Length);

                int currentIndex = 0;
                while (true)
                {
                    int oldStartIndex = currentIndex;
                    currentIndex = AdvanceToNextKeyword(source, currentIndex, out int expressionLength, out Keyword keyword, out string userKey);

                    result.Append(source, oldStartIndex, currentIndex - oldStartIndex);

                    if (keyword == Keyword.NoKeyword)
                    {
                        break;
                    }

                    if (keyword == Keyword.AppInsert)
                    {
                        if (shaderInsertions == null || !shaderInsertions.TryGetValue(userKey, out string userValue))
                        {
                            const string defaultFallbackValue = "0";
                            userValue = defaultFallbackValue;

                            Logger.Log(Logger.LogLevel.Error, $"The application does not provide a glsl {keyword} value for {userKey}. {defaultFallbackValue} as fallback is inserted");
                        }

                        result.Append(userValue);
                    }
                    else if (keyword == Keyword.AppInclude)
                    {
                        string path = SHADER_PATH + userKey;
                        if (!File.Exists(path))
                        {
                            Logger.Log(Logger.LogLevel.Error, $"Include file {path} does not exist");
                            continue;
                        }

                        int lineCount = CountLines(source, currentIndex);
                        string includedText = File.ReadAllText(path);

                        string newLine = "#line 1";
                        if (REPORT_SHADER_ERRORS_WITH_NAME) newLine += $"\"{path}\"";
                        result.AppendLine(newLine);

                        result.Append(ResolveKeywords(includedText, shaderInsertions, path));
                        result.Append('\n');

                        string origionalLine = $"#line {lineCount + 1}";
                        if (REPORT_SHADER_ERRORS_WITH_NAME) origionalLine += $"\"{sourceName}\"";
                        result.AppendLine(origionalLine);
                    }
                    else
                    {
                        throw new UnreachableException();
                    }

                    currentIndex += expressionLength;
                }
                return result;
            }
        }

        private static int AdvanceToNextKeyword(string source, int startIndex, out int expressionLength, out Keyword keyword, out string value)
        {
            expressionLength = 0;
            keyword = Keyword.NoKeyword;
            value = null;

            Regex regex = new Regex($"({Keyword.AppInsert}|{Keyword.AppInclude})\\((.*?)\\)");
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
