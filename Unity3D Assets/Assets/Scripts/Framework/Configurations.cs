namespace DedicatedServer.Framework
{
    public class Configurations
    {
        private static Configurations _globalConfigurations;
        public static Configurations GlobalConfigurations { get { if (_globalConfigurations is null) { _globalConfigurations = GetDefaultConfigurations(); } return _globalConfigurations; } }

        public int SyncRatePerSecond { get; set; }
        public int DisconnectThersholdFrameCount { get; set; }
        public bool EnableAutomaticAuthorityTransfer { get; set; }
        public bool EnableDirtyOnlySync { get; set; }
        public bool EnableParallelWriteTickLogging { get; set; }
        public int ParallelWriteTickWorkerThreadLimit { get; set; }

        private Configurations()
        {
            this.SyncRatePerSecond = 30;
            this.DisconnectThersholdFrameCount = 1000;
            this.EnableAutomaticAuthorityTransfer = true;
            this.EnableDirtyOnlySync = true;
            this.EnableParallelWriteTickLogging = false;
            this.ParallelWriteTickWorkerThreadLimit = -1;
        }

        public static Configurations GetDefaultConfigurations()
        {
            return new Configurations();
        }

        /// <summary>
        /// Global configurations should be the same for both server and client.
        /// And it should be effectively immutable after bootstrap the framework.
        /// </summary>
        /// <param name="configurations"></param>
        public static void SetGlobalConfigurations(Configurations configurations)
        {
            _globalConfigurations = configurations;
        }
    }
}
