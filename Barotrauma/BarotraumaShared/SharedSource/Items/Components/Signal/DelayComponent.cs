using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
namespace Barotrauma.Items.Components
{
    class DelayComponent : ItemComponent
    {
        class DelayedSignal
        {
            public readonly Signal Signal;
            //in number of frames
            public int SendTimer;
            //in number of frames
            public int SendDuration;

            public DelayedSignal(Signal signal, int sendTimer)
            {
                Signal = signal;
                SendTimer = sendTimer;
            }
        }

        private int signalQueueSize;
        private int delayTicks;

        // Thread-safe queue for concurrent access
        private readonly ConcurrentQueue<DelayedSignal> signalQueue = new ConcurrentQueue<DelayedSignal>();

        private DelayedSignal prevQueuedSignal;
        
        private float delay;
        [InGameEditable(MinValueFloat = 0.0f, MaxValueFloat = 60.0f, DecimalCount = 2), Serialize(1.0f, IsPropertySaveable.Yes, description: "How long the item delays the signals (in seconds).", alwaysUseInstanceValues: true)]
        public float Delay
        {
            get { return delay; }
            set
            {
                if (value == delay) { return; }
                delay = value;
                delayTicks = (int)(delay / Timing.Step);
                signalQueueSize = Math.Max(delayTicks, 1) * 2;
                // ConcurrentQueue doesn't have Clear(), drain it instead
                while (signalQueue.TryDequeue(out _)) { }
            }
        }

        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "Should the component discard previously received signals when a new one is received.", alwaysUseInstanceValues: true)]
        public bool ResetWhenSignalReceived
        {
            get;
            set;
        }

        [InGameEditable, Serialize(false, IsPropertySaveable.Yes, description: "Should the component discard previously received signals when the incoming signal changes.", alwaysUseInstanceValues: true)]
        public bool ResetWhenDifferentSignalReceived
        {
            get;
            set;
        }

        public DelayComponent(Item item, ContentXElement element)
            : base (item, element)
        {
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            if (signalQueue.IsEmpty)
            {
                IsActive = false;
                return;
            }

            // Use ToArray() snapshot for thread-safe iteration
            foreach (var val in signalQueue.ToArray())
            {
                val.SendTimer -= 1;
            }
            while (signalQueue.TryPeek(out var signalOut) && signalOut.SendTimer <= 0)
            {
                signalOut.SendDuration -= 1;
                item.SendSignal(new Signal(signalOut.Signal.value, sender: signalOut.Signal.sender, strength: signalOut.Signal.strength), "signal_out");
                if (signalOut.SendDuration <= 0) 
                {
                    //check the queue isn't empty again, because sending the signal may empty it
                    //if this component is set to reset when it receives a signal and the signal is routed back to this component
                    signalQueue.TryDequeue(out _);
                } 
                else 
                { 
                    break; 
                }
            }
        }

        public override void ReceiveSignal(Signal signal, Connection connection)
        {
            switch (connection.Name)
            {
                case "signal_in":
                    if (signalQueue.Count >= signalQueueSize) { return; }
                    if (ResetWhenSignalReceived) 
                    { 
                        prevQueuedSignal = null; 
                        while (signalQueue.TryDequeue(out _)) { } 
                    }
                    if (ResetWhenDifferentSignalReceived && signalQueue.TryPeek(out var peekSignal) && peekSignal.Signal.value != signal.value)
                    {
                        prevQueuedSignal = null;
                        while (signalQueue.TryDequeue(out _)) { }
                    }

                    if (prevQueuedSignal != null && 
                        prevQueuedSignal.Signal.value == signal.value && 
                        MathUtils.NearlyEqual(prevQueuedSignal.Signal.strength, signal.strength) &&
                        ((prevQueuedSignal.SendTimer + prevQueuedSignal.SendDuration == delayTicks) || (prevQueuedSignal.SendTimer <= 0 && prevQueuedSignal.SendDuration > 0)))
                    {
                        prevQueuedSignal.SendDuration += 1;
                        return;
                    }

                    prevQueuedSignal = new DelayedSignal(signal, delayTicks)
                    {
                        SendDuration = 1
                    };
                    signalQueue.Enqueue(prevQueuedSignal);
                    IsActive = true;
                    break;
                case "set_delay":
                    if (float.TryParse(signal.value, NumberStyles.Any, CultureInfo.InvariantCulture, out float newDelay))
                    {
						newDelay = MathHelper.Clamp(newDelay, 0, 60);
                        if (!signalQueue.IsEmpty && newDelay != Delay)
                        {
                            prevQueuedSignal = null;
                            while (signalQueue.TryDequeue(out _)) { }
                        }
                        Delay = newDelay;
                    }
                    break;
            }
        }
    }
}
