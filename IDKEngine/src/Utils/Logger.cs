using System;
using System.IO;

namespace IDKEngine.Utils
{
    static class Logger
    {
        public enum LogLevel : int
        {
            Info,
            Warn,
            Error,
            Fatal,
        }

        private const string PATH = "log.txt";
        private const string DATE_TIME_FORMAT = "HH:mm:ss.f";

        private static StreamWriter outText;
        private static bool lazyLoaded = false;
        public static void Log(LogLevel level, string text)
        {
            if (!lazyLoaded)
            {
                outText = new StreamWriter(File.Create(PATH));
                lazyLoaded = true;
            }

            string preText = $"{DateTime.Now.ToString(DATE_TIME_FORMAT)} [{level.ToString().ToUpper()}] ";
            string indentedText = Indent(text, preText.Length);
            string formatedText = $"{preText}{indentedText}";

            lock (Console.Out)
            {
                Console.ForegroundColor = LogLevelToColor(level);
                Console.WriteLine(formatedText);
                Console.ResetColor();

                outText.WriteLine(formatedText);
                outText.Flush();
            }
        }

        private static ConsoleColor LogLevelToColor(LogLevel level)
        {
            ConsoleColor consoleColor = level switch
            {
                LogLevel.Info => ConsoleColor.Gray,
                LogLevel.Warn => ConsoleColor.DarkYellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Fatal => ConsoleColor.DarkRed,
                _ => throw new NotImplementedException($"Can not convert {nameof(level)} = {level} to {nameof(consoleColor)}"),
            };
            return consoleColor;
        }

        private static string Indent(string text, int spaces)
        {
            string indentation = new string(' ', spaces);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    text = text.Insert(i + 1, indentation);
                    i += indentation.Length;
                }
            }
            return text;
        }
    }
}
