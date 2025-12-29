using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;

namespace Barotrauma
{
    /// <summary>
    /// High-performance lock-free thread-safe queue for deferring physics operations to the main thread.
    /// This is necessary because Farseer Physics' DynamicTree is not thread-safe,
    /// and physics operations cannot be safely performed during parallel updates.
    /// 
    /// Uses System.Threading.Channels for optimal throughput with single-reader pattern.
    /// Channel&lt;T&gt; provides better performance than ConcurrentQueue in producer-consumer scenarios.
    /// 
    /// Supported operations include:
    /// - Physics body creation
    /// - Physics body transform updates (SetTransform, SetTransformIgnoreContacts)
    /// - Any other operation that modifies the Farseer physics world
    /// </summary>
    /// <remarks>
    /// Workflow:
    /// <code>
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
    /// </code>
    /// </remarks>
    static class PhysicsBodyQueue
    {
        // High-performance unbounded channel optimized for single-reader scenario
        private static readonly Channel<Action> _channel = Channel.CreateUnbounded<Action>(
            new UnboundedChannelOptions
            {
                SingleReader = true,                    // Only main thread reads - enables optimizations
                SingleWriter = false,                   // Multiple parallel threads may write
                AllowSynchronousContinuations = false   // Prevent stack dives, improve throughput
            });

        private static readonly ChannelWriter<Action> _writer = _channel.Writer;
        private static readonly ChannelReader<Action> _reader = _channel.Reader;

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
        /// This method is lock-free and can be safely called from parallel update loops.
        /// Uses Channel's optimized TryWrite which is faster than ConcurrentQueue.Enqueue.
        /// </summary>
        /// <param name="operation">The physics operation to defer</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Enqueue(Action operation)
        {
            if (operation == null) { return; }
            _writer.TryWrite(operation);
        }

        /// <summary>
        /// Enqueues a physics body creation action to be executed on the main thread.
        /// This method is lock-free and can be safely called from parallel update loops.
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
        /// 
        /// Hot path optimization: Most calls occur outside parallel context, so we check
        /// the non-parallel case first to improve branch prediction.
        /// </summary>
        /// <param name="operation">The physics operation to execute</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ExecuteOrDefer(Action operation)
        {
            if (operation == null) { return; }
            
            // Hot path: Most calls are outside parallel context - execute immediately
            if (!_isInParallelContext)
            {
                operation();
                return;
            }
            
            // Cold path: In parallel context - defer to queue
            _writer.TryWrite(operation);
        }

        /// <summary>
        /// Gets whether there are any pending physics operations.
        /// This is an O(1) operation.
        /// </summary>
        public static bool HasPending => _reader.TryPeek(out _);

        /// <summary>
        /// Gets the approximate number of pending physics operations.
        /// Note: This may have some overhead compared to the previous atomic counter.
        /// Use HasPending for simple empty checks.
        /// </summary>
        public static int PendingCount => _reader.Count;

        /// <summary>
        /// Processes all pending physics operations.
        /// Must be called on the main thread, outside of any parallel loops.
        /// Uses Channel's optimized TryRead for single-reader scenario.
        /// </summary>
        public static void ProcessPendingOperations()
        {
            while (_reader.TryRead(out Action action))
            {
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
            while (_reader.TryRead(out _)) { }
        }
    }
}
