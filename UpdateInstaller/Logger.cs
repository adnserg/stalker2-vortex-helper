using System;
using System.IO;
using System.Threading;

namespace UpdateInstaller
{
    public class Logger
    {
        private static readonly Lock _lock = new();
        private static readonly string _logFile = "updater.log";
        private static Logger _instance;

        public static Logger Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Logger();
                }
                return _instance;
            }
        }

        private Logger()
        {
        }

        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            lock (_lock)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var logEntry = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logFile, logEntry);
                }
                catch
                {
                    // Игнорируем ошибки записи лога, чтобы не сломать приложение
                }
            }
        }

        public void LogInfo(string message)
        {
            Log(message, LogLevel.Info);
        }

        public void LogWarning(string message)
        {
            Log(message, LogLevel.Warning);
        }

        public void LogError(string message, Exception ex = null)
        {
            if (ex != null)
            {
                Log($"{message} | Exception: {ex.Message} | StackTrace: {ex.StackTrace}", LogLevel.Error);
            }
            else
            {
                Log(message, LogLevel.Error);
            }
        }

        public void LogDebug(string message)
        {
            Log(message, LogLevel.Debug);
        }

        public void LogSuccess(string message)
        {
            Log(message, LogLevel.Success);
        }
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Success,
        Warning,
        Error
    }
}

