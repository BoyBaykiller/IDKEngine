using System;
using System.Diagnostics;
using IDKEngine.Utils;

namespace IDKEngine
{
    class Program
    {
        private static void Main()
        {
            Application application = new Application(1280, 720, "IDKEngine");

            // If application is run inside a debugger then we don't catch Exceptions
            // as this worsens the debugging experience.
            // Otherwise, if the app is run standalone, then we catch and log the Exception.
            if (Debugger.IsAttached)
            {
                application.Run();
            }
            else
            {
                try
                {
                    application.Run();
                }
                catch (Exception ex)
                {
                    Logger.Log(Logger.LogLevel.Fatal, ex.Message);
                }
            }

        }
    }
}
