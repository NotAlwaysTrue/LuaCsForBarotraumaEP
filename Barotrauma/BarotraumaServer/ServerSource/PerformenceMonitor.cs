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

        private static Queue<double> tickrate60s = new Queue<double>(61);

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
        public int ConnectClients
        {
            get { return Client.ClientList.Count; }
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
                return tickrate60s.Count > 0 ? tickrate60s.Average() : 60;
            }
        }

        public double TotalTimeElapsed
        {
            get
            {
                return PMStopwatch.Elapsed.TotalMilliseconds;
            }
        }

        public TimeSpan TimeElapsed
        {
            get
            {
                return TimeSpan.FromMilliseconds(TotalTimeElapsed);
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
            if (tickrate60s.Count > 60)
            {
                tickrate60s.Dequeue();
            }
            if (TotalTimeElapsed - 1000 >= tickratetimer)
            {
                RealTickRate = LastSecondTicks / (TotalTimeElapsed - tickratetimer) * 1000;
                tickrate60s.Enqueue(RealTickRate);
                tickratetimer = TotalTimeElapsed;
                LastSecondTicks = 0;
            }
            if (TotalTimeElapsed - 60000 >= tickrate60stimer)
            {
                GameServer.Log(PM.ToString(), ServerLog.MessageType.ServerMessage);
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
            return $"Server Performence Info \n" +
                   $"Item Count: {ItemCount}\n" +
                   $"Character Count: {CharacterCount}\n" +
                   $"Clients Count {ConnectClients}\n " +
                   $"PhysicsBody Count: {PhysicsBodyCount}\n" +
                   $"Tick Rate: {RealTickRate}\n" +
                   $"Min Tick Rate: {TickRateLow}\n" +
                   $"Max Tick Rate: {TickRateHigh}\n" +
                   $"Total Ticks: {TotalTicks}\n" +
                   $"All time Average Tick Rate: {AverageTickRate}\n" +
                   $"60s Average Tick Rate: {AverageTickRate10s}\n" +
                   $"Server Run Time: {TimeElapsed}\n" +
                   $"Memory Usage: {MemoryUsage}\n";
        }

    }
}
