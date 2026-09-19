using DedicatedServer.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DedicatedServer.UnityFramework
{
    public static class UnityLogger
    {
        public static void OutputMethod(LoggerUtil.LogType logType, string message)
        {
            switch (logType)
            {
                case LoggerUtil.LogType.Information:
                    Debug.Log(message);
                    break;
                case LoggerUtil.LogType.Warning:
                    Debug.LogWarning(message);
                    break;
                case LoggerUtil.LogType.Error:
                    Debug.LogError(message);
                    break;
            }
        }
    }
}
