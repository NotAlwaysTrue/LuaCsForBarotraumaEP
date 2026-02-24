using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Barotrauma
{
    public class SingleThreadWorker
    {
        private ConcurrentQueue<Action> actionQueue;
        private readonly Task workerTask;


        /// <summary>
        /// Enqueue an action
        /// </summary>
        /// <param name="action"></param>
        /// <returns>A boolean indicates whether the operation was successfully completed. True if successful, False otherwise</returns>
        public bool AddToQueue(Action action)
        {
            try
            {
                actionQueue.Enqueue(action);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task CreatActionProcessorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await ActionSignal.WaitAsync(100, token);
                    ProcessPendingCreateEvents();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void ProcessPendingCreateEvents()
        {
            // Dequeue and process all pending events currently in the queue.
            // Use a lock to synchronize modifications to shared lists / ID.
            while (actionQueue.TryDequeue(out Action PendingAction))
            {
                try
                {
                    PendingAction?.Invoke();
                }
                catch(Exception e)
                {
                    DebugConsole.ThrowError($"Error while processing action in SingleThreadWorker: {e.Message}\n{e.StackTrace}");
                }
            }
        }


        /// <summary>
        /// Initilize a SingleThreadWorker instance and start the worker thread
        /// </summary>
        public SingleThreadWorker()
        {
            actionQueue = new ConcurrentQueue<Action>();
            workerTask = Task.Run(() => CreatActionProcessorLoop(CancellationToken.None));
        }
    }
}
