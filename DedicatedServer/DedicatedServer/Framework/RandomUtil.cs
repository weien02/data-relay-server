using System;

namespace DedicatedServer.Framework
{
    public static class RandomUtil
    {
#if UNITY_EDITOR
        // fixed seed random number generator for debugging persistance
        private static Random _random = new Random(0);
#else
        private static Random _random = new Random();
#endif

        public static int GetInteger(int minValue, int maxValue)
        {
            return _random.Next(minValue, maxValue);
        }
    }
}
