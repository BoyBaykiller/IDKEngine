using System;
using System.Collections.Concurrent;

namespace IDKEngine
{
    public static class MainThreadQueue
    {
        private static readonly ConcurrentQueue<Action> lazyActionsQueue = new ConcurrentQueue<Action>();
        private static readonly ConcurrentQueue<Action> hastyActionsQueue = new ConcurrentQueue<Action>();


        public static void AddToHastyQueue(Action action)
        {
            hastyActionsQueue.Enqueue(action);
        }

        public static void AddToLazyQueue(Action action)
        {
            lazyActionsQueue.Enqueue(action);
        }

        public static void Execute()
        {
            if (lazyActionsQueue.TryDequeue(out Action action))
            {
                action();
            }

            while (hastyActionsQueue.TryDequeue(out action))
            {
                action();
            }
        }
    }
}
