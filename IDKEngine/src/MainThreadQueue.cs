using System;
using System.Collections.Concurrent;

namespace IDKEngine
{
    public static class MainThreadQueue
    {
        private static readonly ConcurrentQueue<Action> actionsQueue = new ConcurrentQueue<Action>();

        /// <summary>
        /// Queues up an action for execution on the main thread.
        /// </summary>
        public static void Enqueue(Action action)
        {
            actionsQueue.Enqueue(action);
        }

        /// <summary>
        /// Executes one queued up action. This must only be called from the main thread.
        /// </summary>
        public static void ExecuteOne()
        {
            if (actionsQueue.TryDequeue(out Action action))
            {
                action();
            }
        }
    }
}
