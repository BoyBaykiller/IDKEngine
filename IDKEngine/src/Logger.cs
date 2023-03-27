using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace IDKEngine
{
    // TODO: Implement
    static class Logger
    {
        public enum Type
        {
            Info = 0,
            Warn = 1,
            Error = 2,
            Fatal = 3,
        }

        private const string PATH = "log.txt";
        private const string DATE_TIME_FORMAT = "yyyy-MM-dd HH:mm:ss.fff";
        private static StreamWriter outText;

        private static bool lazyLoaded = false;
        public static void Log(Type type, string text)
        {
            if (!lazyLoaded && !File.Exists(PATH))
            {
                outText = new StreamWriter(File.Create(PATH));

                lazyLoaded = true;
            }

            string preText;
            switch (type)
            {
                case Type.Info:
                    preText = $"{DateTime.Now.ToString(DATE_TIME_FORMAT)} [DEBUG]  {text}";
                    break;

                case Type.Warn:
                    break;

                case Type.Error:
                    break;
            }
        }
    }
}
