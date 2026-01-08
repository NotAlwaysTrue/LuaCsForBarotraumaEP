using System;
using System.Collections.Concurrent;

namespace Barotrauma
{
    public class SingleThreadWorker
    {
        private ConcurrentQueue<Action> ActionQueue;

        public static SingleThreadWorker GlobalWorker = new SingleThreadWorker();

        public SingleThreadWorker()
        {
            ActionQueue = new ConcurrentQueue<Action>();
        }

        public void AddAction(Action action)
        {
            ActionQueue.Enqueue(action);
        }

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
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"WARNING: Error occurred when running Single Thread Actions \n{e}");
                    Console.ForegroundColor = Console.ForegroundColor;
                }
            }
        }

    }
}
