using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DedicatedServer.Framework.Client.ECS.Parallel
{
#if UNITY_STANDALONE_WIN || (UNITY_EDITOR_WIN && !UNITY_ANDROID)
    /// <summary>
    /// Real parallel threads running in different CPU cores.
    /// </summary>
    internal static class ParallelWorkThreadAffinityWindows
    {
        public static List<UIntPtr> GetAvailableProcessorAffinities()
        {
            ulong processAffinityMask = GetProcessAffinityMaskOrFallback();
            int maxProcessorBitCount = IntPtr.Size * 8;
            List<UIntPtr> processorAffinities = new List<UIntPtr>();
            for (int processorBitIndex = 0; processorBitIndex < maxProcessorBitCount; processorBitIndex++)
            {
                ulong processorMask = 1UL << processorBitIndex;
                if ((processAffinityMask & processorMask) == 0)
                {
                    continue;
                }
                processorAffinities.Add(CreateAffinityMask(processorMask));
            }
            return processorAffinities;
        }

        public static void BeginWorkerThreadAffinity(UIntPtr processorAffinity)
        {
            Thread.BeginThreadAffinity();
            UIntPtr previousAffinity = SetThreadAffinityMask(GetCurrentThread(), processorAffinity);
            if (previousAffinity == UIntPtr.Zero)
            {
                int win32ErrorCode = Marshal.GetLastWin32Error();
                Thread.EndThreadAffinity();
                throw new InvalidOperationException("Failed to set worker thread affinity. Win32 error code = " + win32ErrorCode);
            }
        }

        public static void EndWorkerThreadAffinity()
        {
            Thread.EndThreadAffinity();
        }

        private static UIntPtr CreateAffinityMask(ulong processorMask)
        {
            return IntPtr.Size == sizeof(ulong) ? new UIntPtr(processorMask) : new UIntPtr((uint)processorMask);
        }

        private static ulong GetProcessAffinityMaskOrFallback()
        {
            UIntPtr processAffinityMask;
            UIntPtr systemAffinityMask;
            bool gotProcessAffinityMask = GetProcessAffinityMask(GetCurrentProcess(), out processAffinityMask, out systemAffinityMask);
            if (gotProcessAffinityMask)
            {
                ulong resolvedProcessAffinityMask = processAffinityMask.ToUInt64();
                if (resolvedProcessAffinityMask != 0)
                {
                    return resolvedProcessAffinityMask;
                }

                ulong resolvedSystemAffinityMask = systemAffinityMask.ToUInt64();
                if (resolvedSystemAffinityMask != 0)
                {
                    return resolvedSystemAffinityMask;
                }
            }

            int fallbackProcessorCount = Math.Max(1, Math.Min(Environment.ProcessorCount, IntPtr.Size * 8));
            ulong fallbackAffinityMask = 0;
            for (int processorBitIndex = 0; processorBitIndex < fallbackProcessorCount; processorBitIndex++)
            {
                fallbackAffinityMask |= 1UL << processorBitIndex;
            }
            return fallbackAffinityMask;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessAffinityMask(IntPtr processHandle, out UIntPtr processAffinityMask, out UIntPtr systemAffinityMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr SetThreadAffinityMask(IntPtr threadHandle, UIntPtr threadAffinityMask);
    }
#endif
}
