using System;
using System.IO;

namespace IDKEngine
{
    static class Logger
    {
        public enum LogLevel
        {
            Info = 0,
            Warn = 1,
            Error = 2,
            Fatal = 3,
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
            
            Console.WriteLine(formated);
            outText.WriteLine(formated);
            outText.Flush();
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
