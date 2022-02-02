using OpenTK;

namespace IDKEngine
{
    class Program
    {
        static void Main(string[] args)
        {
            using Window window = new Window();
            window.Run(DisplayDevice.Default.RefreshRate);
        }
    }
}
