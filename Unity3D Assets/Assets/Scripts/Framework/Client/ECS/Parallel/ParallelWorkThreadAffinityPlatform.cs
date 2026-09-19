using System;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Client.ECS.Parallel
{
    internal static class ParallelWorkThreadAffinityPlatform
    {
        public static bool SupportsHardwareThreadAffinity
        {
            get
            {
#if UNITY_ANDROID
                return true;
#elif UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
                return true;
#else
                return false;
#endif
            }
        }

        public static List<UIntPtr> GetAvailableProcessorAffinities()
        {
#if UNITY_ANDROID
            return ParallelWorkThreadAffinityAndroid.GetAvailableProcessorAffinities();
#elif UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
            return ParallelWorkThreadAffinityWindows.GetAvailableProcessorAffinities();
#else
            int availableProcessorCount = Math.Max(1, Environment.ProcessorCount);
            List<UIntPtr> processorAffinities = new List<UIntPtr>(availableProcessorCount);
            for (int processorIndex = 0; processorIndex < availableProcessorCount; processorIndex++)
            {
                processorAffinities.Add(new UIntPtr((uint)(processorIndex + 1)));
            }
            return processorAffinities;
#endif
        }

        public static void BeginWorkerThreadAffinity(UIntPtr processorAffinity)
        {
#if UNITY_ANDROID
            ParallelWorkThreadAffinityAndroid.BeginWorkerThreadAffinity(processorAffinity);
#elif UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
            ParallelWorkThreadAffinityWindows.BeginWorkerThreadAffinity(processorAffinity);
#endif
        }

        public static void EndWorkerThreadAffinity()
        {
#if UNITY_ANDROID
            ParallelWorkThreadAffinityAndroid.EndWorkerThreadAffinity();
#elif UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
            ParallelWorkThreadAffinityWindows.EndWorkerThreadAffinity();
#endif
        }
    }
}
