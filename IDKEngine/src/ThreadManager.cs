using System;
using System.Threading;
using System.Collections.Generic;

namespace IDKEngine
{
    static class ThreadManager
    {
        public enum Flag
        {
            /// <summary>
            /// This thread waits until invocation of the action on the main thread
            /// </summary>
            Stall = 0,

            /// <summary>
            /// Queues action for the main thread but does not wait for its invocation
            /// </summary>
            NoStall = 1,
        }

        private static readonly List<Action> invocationQueue = new List<Action>();
        private static readonly List<Action> invocationQueueCopied = new List<Action>();
        private static readonly AutoResetEvent stopWaitHandle = new AutoResetEvent(false);
        private static bool actionsToBeInvoked = false;

        /// <summary>
        /// Queues an action for invocation on main thread
        /// </summary>
        /// <param name="action">Action to be invoked</param>
        /// <param name="flag">Priority of execution</param>
        public static void AddToQueue(Action action, Flag flag)
        {
            switch (flag)
            {
                case Flag.Stall:
                    Action stallAction = new Action(() =>
                    {
                        action();
                        stopWaitHandle.Set();
                    });

                    lock (invocationQueue)
                    {
                        invocationQueue.Add(stallAction);
                    }

                    stopWaitHandle.WaitOne();
                    break;

                case Flag.NoStall:
                    lock (invocationQueue)
                    {
                        invocationQueue.Add(action);
                    }
                    break;

                default:
                    throw new Exception($"{flag} is invalid");
            }
            actionsToBeInvoked = true;
        }

        public static void InvokeQueuedActions()
        {
            if (actionsToBeInvoked)
            {
                invocationQueueCopied.Clear();
                lock (invocationQueue)
                {
                    invocationQueueCopied.AddRange(invocationQueue);
                    invocationQueue.Clear();
                    actionsToBeInvoked = false;
                }

                for (int i = 0; i < invocationQueueCopied.Count; i++)
                    invocationQueueCopied[i]();
            }
        }
    }
}