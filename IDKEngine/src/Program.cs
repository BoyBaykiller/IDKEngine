namespace IDKEngine
{
    class Program
    {
        private static unsafe void Main()
        {
            Application application = new Application(1280, 720, "IDKEngine");

            application.UpdatePeriod = 1.0 / application.VideoMode->RefreshRate;
            application.Start();
        }
    }
}
