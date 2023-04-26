using System;
using System.IO;
using System.Text;
using System.Linq;
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

        public const string INCLUDE_PATH_PREFIX = "res/";

        public readonly int ID;
        public readonly ShaderType ShaderType;
        public Shader(ShaderType shaderType, string srcCode, params ValueTuple<string, string>[] appInsertions)
        {
            ShaderType = shaderType;

            ID = GL.CreateShader(shaderType);

            srcCode = PreProcess(srcCode, appInsertions.ToDictionary(it => it.Item1, it => it.Item2));

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

        private static string PreProcess(string src, Dictionary<string, string> appInsertions)
        {
            StringBuilder result = new StringBuilder(src.Length);

            int startIndex = 0;
            while (true)
            {
                int oldStartIndex = startIndex;
                startIndex = AdvanceToNextKeyword(src, startIndex, out Keyword keyword);

                result.Append(src, oldStartIndex, startIndex - oldStartIndex);

                if (keyword == Keyword.NoKeyword)
                {
                    break;
                }

                startIndex += keyword.ToString().Length + 1; // account for keyword length and starting "("
                int endIndex = src.IndexOf(')', startIndex); // find startIndex of ending ")"

                string userKey = src.Substring(startIndex, endIndex - startIndex);
                startIndex += userKey.Length + 1; // account for user key name and ending ")"
                
                if (keyword == Keyword.AppInsert)
                {
                    if (!appInsertions.TryGetValue(userKey, out string userValue))
                    {
                        Logger.Log(Logger.LogLevel.Error, $"The application does not provide a glsl {keyword} value for {userKey}");
                        continue;
                    }

                    result.AppendLine(userValue);
                }

                if (keyword == Keyword.AppInclude)
                {
                    int lineCount = CountLines(src, startIndex);

                    string path = INCLUDE_PATH_PREFIX + userKey;
                    if (!File.Exists(path))
                    {
                        Logger.Log(Logger.LogLevel.Error, $"Include file {path} does not exist");
                        continue;
                    }

                    string includeSrc = File.ReadAllText(path);
                    if (includeSrc.Contains(Keyword.AppInclude.ToString()) || includeSrc.Contains(Keyword.AppInsert.ToString()))
                    {
                        Logger.Log(Logger.LogLevel.Error, "Recursive includes or insertions intentionally not allowed");
                        continue;
                    }

                    result.AppendLine("#line 1");
                    result.AppendLine(includeSrc);
                    result.AppendLine($"#line {lineCount + 1}");
                }
            }
            return result.ToString();
        }
        private static int AdvanceToNextKeyword(string srcCode, int startIndex, out Keyword keyword)
        {
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
