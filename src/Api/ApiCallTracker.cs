using System;
using System.Collections.Generic;
using System.Threading;

namespace WvWarlord.Api
{
    /// <summary>
    /// Global call-volume counters and a rolling text log for the DevDebug window.
    /// Blish (Gw2ApiManager) calls weight 1; direct-endpoint (raw HttpClient) calls
    /// weight 1,000,000 so spikes from the fallback path are visible at a glance.
    /// </summary>
    public static class ApiCallTracker
    {
        private static long _totalWeightedCalls = 0;
        private static long _blishCalls = 0;
        private static long _directCalls = 0;
        private static long _catchCount = 0;

        public static long TotalWeightedCalls => Interlocked.Read(ref _totalWeightedCalls);
        public static long BlishCallCount => Interlocked.Read(ref _blishCalls);
        public static long DirectCallCount => Interlocked.Read(ref _directCalls);
        public static long CatchCount => Interlocked.Read(ref _catchCount);

        /// <summary>Increment whenever a try/catch actually catches something -- distinct from ordinary success logging, which is intentionally not recorded past startup.</summary>
        public static void RecordCatch() => Interlocked.Increment(ref _catchCount);

        private const int MaxLogLines = 500;
        private static readonly LinkedList<string> _logLines = new LinkedList<string>();
        private static readonly object _logLock = new object();

        public static event EventHandler LogUpdated;

        public static void RecordBlishCall()
        {
            Interlocked.Add(ref _totalWeightedCalls, 1);
            Interlocked.Increment(ref _blishCalls);
        }

        public static void RecordDirectCall()
        {
            Interlocked.Add(ref _totalWeightedCalls, 1_000_000);
            Interlocked.Increment(ref _directCalls);
        }

        public static void Log(string line)
        {
            string stamped = $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
            lock (_logLock)
            {
                _logLines.AddLast(stamped);
                while (_logLines.Count > MaxLogLines) _logLines.RemoveFirst();
            }
            LogUpdated?.Invoke(null, EventArgs.Empty);
        }

        public static string GetLogText()
        {
            lock (_logLock)
            {
                return string.Join("\n", _logLines);
            }
        }

        public static void ClearLogs()
        {
            lock (_logLock) { _logLines.Clear(); }
            LogUpdated?.Invoke(null, EventArgs.Empty);
        }

        public static void ResetCounters()
        {
            Interlocked.Exchange(ref _totalWeightedCalls, 0);
            Interlocked.Exchange(ref _blishCalls, 0);
            Interlocked.Exchange(ref _directCalls, 0);
        }
    }
}
