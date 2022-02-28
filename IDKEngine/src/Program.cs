namespace IDKEngine
{
    class Program
    {
        private static void Main()
        {
            Application application = new Application(832, 832, "IDKEngine");

            // TODO: Take monitors refresh rate
            application.Start(144, 0);
        }
    }
}
