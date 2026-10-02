using StardewModdingAPI;

namespace StardewPresence.Framework.Services
{
    public static class ModLogger
    {
        public static void Log(IMonitor monitor, string message, LogLevel level)
        {
            monitor.Log(message, level);
        }

        public static void LogInfo(IMonitor monitor, string message) => monitor.Log(message, LogLevel.Info);
        public static void LogTrace(IMonitor monitor, string message) => monitor.Log(message, LogLevel.Trace);
        public static void LogWarn(IMonitor monitor, string message) => monitor.Log(message, LogLevel.Warn);
        public static void LogError(IMonitor monitor, string message) => monitor.Log(message, LogLevel.Error);
    }
}
