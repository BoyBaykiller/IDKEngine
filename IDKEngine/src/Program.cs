namespace IDKEngine
{
    class Program
    {
        private static unsafe void Main()
        {
            Application application = new Application(832, 832, "IDKEngine");

            application.UpdatePeriod = 1.0 / application.VideoMode->RefreshRate;
            application.Start();
        }
    }
}
