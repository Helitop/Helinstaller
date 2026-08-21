// FILE [CS]: .\Helpers\Logger.cs

using System;
using System.Diagnostics;
using System.IO;

namespace Helinstaller.Helpers
{
    public static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt");
        private static readonly object LockObject = new object();

        public static void LogInfo(string message) => WriteLog("INFO", message);
        public static void LogWarning(string message) => WriteLog("WARN", message);
        public static void LogError(string message, Exception? ex = null)
        {
            string detail = message;
            if (ex != null)
            {
                detail += $" | Исключение: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}Стек вызовов: {ex.StackTrace}";
            }
            WriteLog("ERROR", detail);
        }

        private static void WriteLog(string level, string message)
        {
            try
            {
                lock (LockObject)
                {
                    string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogFilePath, logLine, System.Text.Encoding.UTF8);
                    Debug.Write(logLine);
                }
            }
            catch
            {
                // Подавляем ошибки самого логгера, чтобы не ронять приложение
            }
        }

        public static void OpenLogFile()
        {
            try
            {
                if (!File.Exists(LogFilePath))
                {
                    File.WriteAllText(LogFilePath, $"--- Лог инициализирован {DateTime.Now} ---{Environment.NewLine}", System.Text.Encoding.UTF8);
                }

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = LogFilePath,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Не удалось открыть файл логов: {ex.Message}");
            }
        }
    }
}