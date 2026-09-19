using System;
using System.Diagnostics;

namespace DedicatedServer.Framework
{
    public class LoggerUtil
    {
        private static Action<LogType, string> _outputMethod = (LogType logType, string message) => Console.WriteLine(string.Format("[{0}]{1}", logType.ToString(), message));

        private string _typeName;

        private LoggerUtil(Type type)
        {
            this._typeName = type.FullName;
        }

        public static LoggerUtil GetLogger<T>()
        {
            return new LoggerUtil(typeof(T));
        }

        public static void SetOutputMethod(Action<LogType, string> outputMethod)
        {
            _outputMethod = outputMethod;
        }

        public static string FormatElapsedTime(TimeSpan elapsedTime)
        {
            return elapsedTime.TotalMilliseconds.ToString("0.###") + " ms";
        }

        public static string FormatElapsedTimeFromTicks(long elapsedTicks)
        {
            double elapsedMilliseconds = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            return elapsedMilliseconds.ToString("0.###") + " ms";
        }

        private void Log(LogType logType, string message)
        {
            _outputMethod(logType, string.Format("[{0}] {1}", this._typeName, message));
        }

        public void Log(object message)
        {
            Log(LogType.Information, message.ToString());
        }

        public void LogWarning(object message)
        {
            Log(LogType.Warning, message.ToString());
        }

        public void LogError(object message)
        {
            Log(LogType.Error, message.ToString());
        }

        public void LogException(Action action)
        {
            try
            {
                action();
            }
            catch(Exception e)
            {
                Log("Exception " + e.ToString());
            }
        }

        public void Assert(bool condition, string message)
        {
            if (!condition)
            {
                this.LogError("Assertion failed: " + message);
            }
        }

        public enum LogType
        {
            Information,
            Warning,
            Error,
        }
    }
}
