using Barotrauma.Networking;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static Barotrauma.EosInterface.Ownership;

namespace Barotrauma
{
    public class SingleThreadWorker
    {
        private ConcurrentQueue<Action> ActionQueue;

        public static SingleThreadWorker Instance = new SingleThreadWorker();

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly SemaphoreSlim actionSignal = new SemaphoreSlim(0);
        private static Task WorkerTask;

        public static readonly SemaphoreSlim SingleThreadActionStandbySignal = new SemaphoreSlim(1);

        /// <summary>
        /// Initilize a SingleThreadWorker
        /// SingleThreadWorker or STW for short is a FIFO queue ensure single-thread execution of a series of actions.
        /// </summary>
        public SingleThreadWorker()
        {
            ActionQueue = new ConcurrentQueue<Action>();
            WorkerTask = CreateProcessTask(cancellationTokenSource.Token);
        }

        public void Dispose()
        {
            cancellationTokenSource.Cancel();
            WorkerTask.Wait();
            WorkerTask.Dispose();
            Instance = null;
            cancellationTokenSource.Dispose();
            actionSignal.Dispose();
            SingleThreadActionStandbySignal.Dispose();
        }

        private async Task CreateProcessTask(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await actionSignal.WaitAsync(100, token);
                    SingleThreadActionStandbySignal.Wait(CancellationToken.None);
                    RunActions();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                finally
                {
                    SingleThreadActionStandbySignal.Release();
                }
            }
        }

        /// <summary>
        /// Add a pending action in a STW queue.
        /// DO NOT ABUSE IT OR IT WILL SLOW DOWN MAIN THREAD!!!!
        /// </summary>
        /// <param name="action"></param>
        public void AddAction(Action action)
        {
            // enqueue and let background task handle the rest
            ActionQueue.Enqueue(action);

            if (actionSignal.CurrentCount == 0)
            {
                actionSignal.Release();
            }
        }

        /// <summary>
        /// Run all pending actions in the STW queue
        /// </summary>
        [STAThread]
        private void RunActions()
        {
            while (ActionQueue.TryDequeue(out Action action))
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception e)
                {
                    // Just try-catch and do nothing but print errorlogs. We cannot afford crashing the game.
                    ConsoleColor originalForeground = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"WARNING: Error occurred when running Single Thread Actions." +
                        $"If the server didn't crash or stop responding then this should be fine \n{e}");
                    Console.ForegroundColor = Console.ForegroundColor;
                }
            }
        }

    }
}
