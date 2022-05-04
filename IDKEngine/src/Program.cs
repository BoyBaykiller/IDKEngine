namespace IDKEngine
{
    class Program
    {
        private static unsafe void Main()
        {
            Application application = new Application(1760, 990, "IDKEngine");

            application.UpdatePeriod = 1.0 / application.VideoMode->RefreshRate;
            application.Start();
        }
    }
}
