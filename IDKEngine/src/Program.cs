using OpenTK.Windowing.Desktop;
using OpenTK.Windowing;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Core;
using OpenTK.Core.Platform;
using OpenTK.Platform;
using OpenTK.Platform.Windows;

namespace IDKEngine
{
    class Program
    {
        private static void Main(string[] args)
        {
            Application application = new Application(832, 832, "IDKEngine");

            application.Start(144, 0);
        }
    }
}
