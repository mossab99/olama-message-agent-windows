using System;
using System.IO;
using System.Text;

namespace OlamaMessageAgent
{
    public static class Logger
    {
        private static readonly string LogDir;
        private static readonly string LogFilePath;
        private static readonly object LockObj = new object();

        public static event Action<string> OnLogAdded;

        static Logger()
        {
            string envDir = Environment.GetEnvironmentVariable("OLAMA_AGENT_SETTINGS_DIR");
            if (!string.IsNullOrEmpty(envDir))
            {
                LogDir = envDir;
            }
            else
            {
                LogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OlamaMessageAgent"
                );
            }
            LogFilePath = Path.Combine(LogDir, "agent.log");

            try
            {
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }
            }
            catch
            {
                // Fallback to current directory if AppData is blocked
                LogDir = AppDomain.CurrentDomain.BaseDirectory;
                LogFilePath = Path.Combine(LogDir, "agent.log");
            }
        }

        public static void Info(string message)
        {
            Log("INFO", message);
        }

        public static void Warn(string message)
        {
            Log("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            var sb = new StringBuilder(message);
            if (ex != null)
            {
                sb.AppendLine();
                sb.Append("Exception: ").Append(ex.GetType().Name).Append(" - ").AppendLine(ex.Message);
                sb.Append("Stack Trace: ").Append(ex.StackTrace);
            }
            Log("ERROR", sb.ToString());
        }

        private static void Log(string level, string message)
        {
            // Sanity filter to prevent accidental logging of API secrets or sensitive auth headers
            string sanitized = SanitizeMessage(message);

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {sanitized}";

            lock (LockObj)
            {
                try
                {
                    File.AppendAllLines(LogFilePath, new[] { line }, Encoding.UTF8);
                }
                catch
                {
                    // Ignore write failures to prevent app crashes
                }
            }

            OnLogAdded?.Invoke(line);
        }

        private static string SanitizeMessage(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return msg;

            // Remove anything resembling olama_agent_ followed by hex chars (API Keys)
            // Just in case, replace key patterns
            int idx = msg.IndexOf("olama_agent_", StringComparison.OrdinalIgnoreCase);
            while (idx != -1)
            {
                // Replace the key with [HIDDEN]
                int endIdx = idx + 12;
                while (endIdx < msg.Length && ((msg[endIdx] >= '0' && msg[endIdx] <= '9') || 
                                               (msg[endIdx] >= 'a' && msg[endIdx] <= 'f') || 
                                               (msg[endIdx] >= 'A' && msg[endIdx] <= 'F')))
                {
                    endIdx++;
                }
                msg = msg.Substring(0, idx) + "[SECRET_KEY_REDACTED]" + msg.Substring(endIdx);
                idx = msg.IndexOf("olama_agent_", StringComparison.OrdinalIgnoreCase);
            }

            return msg;
        }

        public static string GetLogPath()
        {
            return LogFilePath;
        }

        public static string ReadAllLogs()
        {
            lock (LockObj)
            {
                try
                {
                    if (File.Exists(LogFilePath))
                    {
                        return File.ReadAllText(LogFilePath, Encoding.UTF8);
                    }
                }
                catch
                {
                    return "Error reading log file.";
                }
            }
            return string.Empty;
        }

        public static void ClearLogs()
        {
            lock (LockObj)
            {
                try
                {
                    if (File.Exists(LogFilePath))
                    {
                        File.WriteAllText(LogFilePath, string.Empty, Encoding.UTF8);
                    }
                }
                catch
                {
                    // Ignore
                }
            }
            OnLogAdded?.Invoke($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [INFO] Logs cleared by user.");
        }
    }
}
