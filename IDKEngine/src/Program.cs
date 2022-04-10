using System.Collections.Generic;
namespace IDKEngine
{
    class Program
    {
        //private static readonly List<int> bvh = new List<int>();
        //private static void BuildBVH(int node, int level = 0)
        //{
        //    if (level == 4)
        //        return;
            
        //    bvh.Add(node + 1);
        //    BuildBVH(bvh.Count - 1, level + 1);

        //    bvh.Add((int)GetRightChildIndex((uint)node, 5, level));
        //    BuildBVH(bvh.Count - 1, level + 1);
        //}

        private static unsafe void Main()
        {
            Application application = new Application(1280, 720, "IDKEngine");

            application.UpdatePeriod = 1.0 / application.VideoMode->RefreshRate;
            application.Start();
        }
    }
}
