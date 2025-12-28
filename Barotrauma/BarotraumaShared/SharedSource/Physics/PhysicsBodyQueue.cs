using System;
using System.Collections.Generic;

namespace Barotrauma
{
    /// <summary>
    /// Thread-safe queue for deferring physics body creation operations to the main thread.
    /// This is necessary because Farseer Physics' DynamicTree is not thread-safe,
    /// and physics bodies cannot be safely created during parallel item updates.
    /// </summary>
    static class PhysicsBodyQueue
    {
        private static readonly object _lock = new object();
        private static readonly Queue<Action> _pendingCreations = new Queue<Action>();

        /// <summary>
        /// Enqueues a physics body creation action to be executed on the main thread.
        /// This method is thread-safe and can be called from parallel update loops.
        /// </summary>
        /// <param name="createAction">The action that creates the physics body</param>
        public static void EnqueueCreation(Action createAction)
        {
            if (createAction == null) { return; }
            lock (_lock)
            {
                _pendingCreations.Enqueue(createAction);
            }
        }

        /// <summary>
        /// Gets the number of pending physics body creation operations.
        /// </summary>
        public static int PendingCount
        {
            get
            {
                lock (_lock)
                {
                    return _pendingCreations.Count;
                }
            }
        }

        /// <summary>
        /// Processes all pending physics body creation operations.
        /// Must be called on the main thread, outside of any parallel loops.
        /// </summary>
        public static void ProcessPendingCreations()
        {
            while (true)
            {
                Action action;
                lock (_lock)
                {
                    if (_pendingCreations.Count == 0) { break; }
                    action = _pendingCreations.Dequeue();
                }
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error processing deferred physics body creation: {e.Message}", e);
                }
            }
        }

        /// <summary>
        /// Clears all pending physics body creation operations.
        /// Should be called when ending a round or cleaning up.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _pendingCreations.Clear();
            }
        }
    }
}

