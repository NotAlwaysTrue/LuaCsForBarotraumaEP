using System;
using System.Collections.Generic;
using System.Threading;

namespace Barotrauma
{
    /// <summary>
    /// Thread-safe queue for deferring physics operations to the main thread.
    /// This is necessary because Farseer Physics' DynamicTree is not thread-safe,
    /// and physics operations cannot be safely performed during parallel updates.
    /// 
    /// Supported operations include:
    /// - Physics body creation
    /// - Physics body transform updates (SetTransform, SetTransformIgnoreContacts)
    /// - Any other operation that modifies the Farseer physics world
    /// </summary>
/// <start>
///  ├─> PhysicsBodyQueue.IsInParallelContext = true (ThreadStatic)
///  ├─> Item.Update()
///  │     └─> StatusEffect.Apply()
///  │           └─> Character.Kill()
///  │                 └─> Item.Drop()
///  │                       └─> Check if IsInParallelContext == true
///  │                             └─> PhysicsBodyQueue.Enqueue(Physics operation)
///  ├──> PhysicsBodyQueue.IsInParallelContext = false
///  └──> PhysicsBodyQueue.ProcessPendingOperations() ← Main thread executes
///        └─> body.SetTransformIgnoreContacts()
    static class PhysicsBodyQueue
    {
        private static readonly object _lock = new object();
        private static readonly Queue<Action> _pendingOperations = new Queue<Action>();

        /// <summary>
        /// Thread-local flag indicating whether the current thread is in a parallel physics update context.
        /// When true, physics operations should be deferred using this queue instead of executing immediately.
        /// </summary>
        [ThreadStatic]
        private static bool _isInParallelContext;

        /// <summary>
        /// Gets or sets whether the current thread is in a parallel update context.
        /// When true, physics operations should be queued instead of executed immediately.
        /// </summary>
        public static bool IsInParallelContext
        {
            get => _isInParallelContext;
            set => _isInParallelContext = value;
        }

        /// <summary>
        /// Enqueues a physics operation to be executed on the main thread.
        /// This method is thread-safe and can be called from parallel update loops.
        /// </summary>
        /// <param name="operation">The physics operation to defer</param>
        public static void Enqueue(Action operation)
        {
            if (operation == null) { return; }
            lock (_lock)
            {
                _pendingOperations.Enqueue(operation);
            }
        }

        /// <summary>
        /// Enqueues a physics body creation action to be executed on the main thread.
        /// This method is thread-safe and can be called from parallel update loops.
        /// </summary>
        /// <param name="createAction">The action that creates the physics body</param>
        public static void EnqueueCreation(Action createAction)
        {
            Enqueue(createAction);
        }

        /// <summary>
        /// Executes a physics operation, either immediately or deferred depending on context.
        /// If called from a parallel context, the operation will be queued for later execution.
        /// If called from the main thread (outside parallel loops), the operation executes immediately.
        /// </summary>
        /// <param name="operation">The physics operation to execute</param>
        public static void ExecuteOrDefer(Action operation)
        {
            if (operation == null) { return; }
            
            if (_isInParallelContext)
            {
                Enqueue(operation);
            }
            else
            {
                operation();
            }
        }

        /// <summary>
        /// Gets the number of pending physics operations.
        /// </summary>
        public static int PendingCount
        {
            get
            {
                lock (_lock)
                {
                    return _pendingOperations.Count;
                }
            }
        }

        /// <summary>
        /// Processes all pending physics operations.
        /// Must be called on the main thread, outside of any parallel loops.
        /// </summary>
        public static void ProcessPendingOperations()
        {
            while (true)
            {
                Action action;
                lock (_lock)
                {
                    if (_pendingOperations.Count == 0) { break; }
                    action = _pendingOperations.Dequeue();
                }
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Error processing deferred physics operation: {e.Message}", e);
                }
            }
        }

        /// <summary>
        /// Legacy method for backwards compatibility.
        /// Calls ProcessPendingOperations().
        /// </summary>
        public static void ProcessPendingCreations()
        {
            ProcessPendingOperations();
        }

        /// <summary>
        /// Clears all pending physics operations.
        /// Should be called when ending a round or cleaning up.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _pendingOperations.Clear();
            }
        }
    }
}

