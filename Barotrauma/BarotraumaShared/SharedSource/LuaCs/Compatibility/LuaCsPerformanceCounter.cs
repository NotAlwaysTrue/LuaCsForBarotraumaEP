using System;
using System.Collections.Generic;
using System.Diagnostics;


namespace Barotrauma
{
    /// <summary>
    /// <b>[Obsolete]</b> Legacy compatibility only.
    /// </summary>
    [Obsolete("Deprecated.")]
    public class LuaCsPerformanceCounter
    {
        public bool EnablePerformanceCounter = false;

        public double UpdateElapsedTime;
        public Dictionary<string, Dictionary<string, double>> HookElapsedTime = new Dictionary<string, Dictionary<string, double>>();

        public static float MemoryUsage
        {
            get
            {
                Process proc = Process.GetCurrentProcess();
                float memory = MathF.Round(proc.PrivateMemorySize64 / (1024 * 1024), 2);
                proc.Dispose();

                return memory;
            }
        }

        public void SetHookElapsedTicks(string eventName, string hookName, long ticks)
        {
            if (!HookElapsedTime.ContainsKey(eventName)) 
            { 
                HookElapsedTime[eventName] = new Dictionary<string, double>(); 
            }

            HookElapsedTime[eventName][hookName] = (double)ticks / Stopwatch.Frequency;
        }
    }
}
