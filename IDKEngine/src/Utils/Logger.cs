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
            text = Indent(text, preText.Length);
            string formated = $"{preText}{text}";

            lock (Console.Out)
            {
                Console.ForegroundColor = LogLevelToColor(level);
                Console.WriteLine(formated);
                Console.ResetColor();
            }

            outText.WriteLine(formated);
            outText.Flush();
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
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    text = text.Insert(i + 1, new string(' ', spaces));
                    i += spaces;
                }
            }
            return text;
        }
    }
}
