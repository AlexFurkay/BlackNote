using System;
using System.Diagnostics;
using System.IO;

namespace BlackNote
{
    /// <summary>Startup timing log, enabled by the BLACKNOTE_TRACE environment variable (path of the log file).</summary>
    internal static class StartupTrace
    {
        private static readonly string LogPath = Environment.GetEnvironmentVariable("BLACKNOTE_TRACE");

        public static void Mark(string step)
        {
            if (string.IsNullOrEmpty(LogPath)) return;
            var since = DateTime.Now - Process.GetCurrentProcess().StartTime;
            try { File.AppendAllText(LogPath, $"{since.TotalMilliseconds,8:0} ms  {step}{Environment.NewLine}"); } catch { }
        }
    }
}
