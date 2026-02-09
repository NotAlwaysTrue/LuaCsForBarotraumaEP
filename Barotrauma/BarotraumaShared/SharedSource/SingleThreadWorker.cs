using System;
using System.Collections.Concurrent;

namespace Barotrauma
{
    public class SingleThreadWorker
    {
        private ConcurrentQueue<Action> ActionQueue;

        public static SingleThreadWorker GlobalWorker = new SingleThreadWorker();

        /// <summary>
        /// Initilize a SingleThreadWorker
        /// SingleThreadWorker or STW for short is a FIFO queue ensure single-thread execution of a series of actions.
        /// </summary>
        public SingleThreadWorker()
        {
            ActionQueue = new ConcurrentQueue<Action>();
        }

        /// <summary>
        /// Add a pending action in a STW queue
        /// </summary>
        /// <param name="action"></param>
        public void AddAction(Action action)
        {
            ActionQueue.Enqueue(action);
        }

        /// <summary>
        /// Run all pending actions in the STW queue
        /// </summary>
        [STAThread]
        public void RunActions()
        {
            while (ActionQueue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    // Just try-catch and do nothing but print errorlogs. We cannot afford crashing the game.
                    ConsoleColor originalForeground = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"WARNING: Error occurred when running Single Thread Actions. " +
                        $"If the server didn't crash or stop responding then this should be fine \n{e}");
                    Console.ForegroundColor = Console.ForegroundColor;
                }
            }
        }

    }
}
