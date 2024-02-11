using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
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

        public const string BASE_INCLUDE_PATH = "res/shaders/";

        public readonly int ID;
        public readonly ShaderType ShaderType;
        public static string debugShaderString;
        public Shader(ShaderType shaderType, string srcCode, Dictionary<string, string> shaderInsertions = null)
        {
            ShaderType = shaderType;

            ID = GL.CreateShader(shaderType);

            srcCode = PreProcess(srcCode, shaderInsertions);
            debugShaderString = srcCode;

            GL.ShaderSource(ID, srcCode);
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

        private static string PreProcess(string src, Dictionary<string, string> shaderInsertions)
        {
            StringBuilder result = new StringBuilder(src.Length);

            int startIndex = 0;
            while (true)
            {
                // Advance to the next keyword and append all text leading up to it

                int oldStartIndex = startIndex;
                startIndex = AdvanceToNextKeyword(src, startIndex, out Keyword keyword);

                result.Append(src, oldStartIndex, startIndex - oldStartIndex);

                if (keyword == Keyword.NoKeyword)
                {
                    break;
                }

                startIndex += keyword.ToString().Length + 1;
                int endIndex = src.IndexOf(')', startIndex);

                string userKey = src.Substring(startIndex, endIndex - startIndex); // this gets the userKey between the parentheses, for example MY_VALUE in AppInsert(MY_VALUE)
                startIndex += userKey.Length + 1; // account for user key name and ending ")"
                
                if (keyword == Keyword.AppInsert)
                {
                    if (shaderInsertions == null || !shaderInsertions.TryGetValue(userKey, out string userValue))
                    {
                        const string defaultFallbackValue = "0";
                        Logger.Log(Logger.LogLevel.Warn, $"The application does not provide a glsl {keyword} value for {userKey}. {defaultFallbackValue} as fallback is inserted");
                        userValue = defaultFallbackValue;
                    }

                    result.Append(userValue);
                }

                if (keyword == Keyword.AppInclude)
                {
                    string path = BASE_INCLUDE_PATH + userKey;
                    if (!File.Exists(path))
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Include file {path} does not exist");
                        continue;
                    }

                    int lineCount = CountLines(src, startIndex);
                    string includeSrc = File.ReadAllText(path);

                    result.AppendLine("#line 1");
                    result.AppendLine(PreProcess(includeSrc, shaderInsertions));
                    result.AppendLine($"#line {lineCount + 1}");
                }
            }
            return result.ToString();
        }

        private static int AdvanceToNextKeyword(string srcCode, int startIndex, out Keyword keyword)
        {
            // TODO: If wee add more Keywords then we should generalize and optimize this function

            keyword = Keyword.NoKeyword;

            int appInsertIndex = srcCode.IndexOf(Keyword.AppInsert.ToString(), startIndex);
            int appIncludeIndex = srcCode.IndexOf(Keyword.AppInclude.ToString(), startIndex);

            if (appInsertIndex == -1 && appIncludeIndex == -1)
            {
                return srcCode.Length;
            }

            if (appIncludeIndex == -1)
            {
                keyword = Keyword.AppInsert;
                return appInsertIndex;
            }

            if (appInsertIndex == -1)
            {
                keyword = Keyword.AppInclude;
                return appIncludeIndex;
            }

            if (appInsertIndex < appIncludeIndex)
            {
                keyword = Keyword.AppInsert;
                return appInsertIndex;
            }

            if (appIncludeIndex < appInsertIndex)
            {
                keyword = Keyword.AppInclude;
                return appIncludeIndex;
            }

            return -1;
        }
        private static int CountLines(string src, int max)
        {
            int lineCount = 0;

            int startIndex = 0;
            while (true)
            {
                startIndex = src.IndexOf('\n', startIndex + 1);
                if (startIndex == -1 || startIndex > max)
                {
                    break;
                }

                lineCount++;
            }

            return lineCount;
        }

        public void Dispose()
        {
            GL.DeleteShader(ID);
        }
    }
}
