using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;


namespace Barotrauma
{
    public class PerformenceMonitor
    {
        static public PerformenceMonitor PM;

        private Stopwatch PMStopwatch = new Stopwatch();

        private double tickratetimer = 0;

        private double tickrate60stimer = 0;

        private static Queue<double> tickrate10s = new Queue<double>(10);

        public int ItemCount
        {
            get{ return Item.ItemList.Count; }
        }

        public int CharacterCount
        {
            get { return Character.CharacterList.Count; }
        }

        public int PhysicsBodyCount
        {
            get { return PhysicsBody.List.Count; }
        }

        public double RealTickRate
        {
            get; set;
        }

        public long TotalTicks
        {
            get;set;
        }

        public int LastSecondTicks
        {
            get; set;
        } = 0;
        
        public float AverageTickRate
        {
            get
            {
                return TotalTicks / (float)TotalTimeElapsed * 1000;
            }
        }

        public double AverageTickRate10s
        {
            get
            {
                return tickrate10s.Count > 0 ? tickrate10s.Average() : 60;
            }
        }

        public double TotalTimeElapsed
        {
            get
            {
                return PMStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        public float MemoryUsage
        {
            get
            {
                Process proc = Process.GetCurrentProcess();
                float memory = MathF.Round(proc.PrivateMemorySize64 / (1024 * 1024), 2);
                proc.Dispose();

                return memory;
            }
        }

        public double TickRateLow
        {
            get; set;
        }

        public double TickRateHigh
        {
            get; set;
        }

        public PerformenceMonitor() 
        {
            PM = this;
            RealTickRate = 60;
            TotalTicks = 0;
            LastSecondTicks = 60;
            TickRateLow = 60;
            TickRateHigh = 60;
            PMStopwatch.Start();
        }

        public void Update()
        {
            TotalTicks += 1;
            LastSecondTicks += 1;
            if(tickrate10s.Count >= 10)
            {
                tickrate10s.Dequeue();
            }
            if (TotalTimeElapsed - 1000 >= tickratetimer)
            {
                RealTickRate = LastSecondTicks / (TotalTimeElapsed - tickratetimer) * 1000;
                tickrate10s.Enqueue(RealTickRate);
                tickratetimer = TotalTimeElapsed;
                LastSecondTicks = 0;
            }
            if (TotalTimeElapsed - 60000 >= tickrate60stimer)
            {
#if !DEBUG
                GameServer.Log(PM.ToString(), ServerLog.MessageType.ServerMessage);
#endif
                TickRateLow = 60;
                TickRateHigh = 60;
                tickrate60stimer = TotalTimeElapsed;
            }
            if (RealTickRate > TickRateHigh)
            {
                TickRateHigh = RealTickRate;
            }
            if (RealTickRate < TickRateLow)
            {
                TickRateLow = RealTickRate;
            }
        }

        public void Dispose()
        {
            PMStopwatch.Reset();
            PM = null;
        }
        override public string ToString()
        {
            return
#if !DEBUG
                   $"Server Performence Info \n" +
#endif
                   $"Item Count: {ItemCount}\n" +
                   $"Character Count: {CharacterCount}\n" +
                   $"PhysicsBody Count: {PhysicsBodyCount}\n" +
                   $"Tick Rate: {RealTickRate}\n" +
                   $"Min Tick Rate: {TickRateLow}\n" +
                   $"Max Tick Rate: {TickRateHigh}\n" +
                   $"Total Ticks: {TotalTicks}\n" +
                   $"All time Average Tick Rate: {AverageTickRate}\n" +
                   $"10s Average Tick Rate: {AverageTickRate10s}\n" +
                   $"Total Time Elapsed: {TotalTimeElapsed}\n" +
                   $"Memory Usage: {MemoryUsage}\n";
        }

    }
}
