using System;

namespace DedicatedServer.Framework
{
    public class Configurations
    {
        private static Configurations _globalConfigurations;
        public static Configurations GlobalConfigurations { get { if (_globalConfigurations is null) { _globalConfigurations = GetDefaultConfigurations(); } return _globalConfigurations; } }

        public int SyncRatePerSecond { get; set; }

        /// <summary>
        /// How long a session may go without a heartbeat before the server drops it.
        ///
        /// Configured as a duration rather than a frame count so that it means the same thing at
        /// every sync rate. It used to be stored directly in frames, which made the real timeout
        /// depend on a setting it had nothing to do with: the same 100 frames was 3.3s at 30Hz
        /// but 1.7s at 60Hz, so raising the sync rate silently halved how long a player could lag
        /// before being kicked.
        /// </summary>
        public double DisconnectThresholdSeconds { get; set; }

        /// <summary>
        /// The same timeout expressed in sync frames, which is the unit the heartbeat check
        /// compares in, since a session records the frame number its last heartbeat arrived on.
        ///
        /// Derived on every read rather than stored, because the sync rate is not necessarily
        /// known when the timeout is configured. The demo sets its configuration inside
        /// Initialize(), while the server's --sync-rate override is applied afterwards; a value
        /// computed at configuration time would be left over from the default rate.
        /// </summary>
        public int DisconnectThresholdFrameCount
        {
            get
            {
                // At least one frame, so a nonsensically small timeout cannot mean "drop every
                // session on the tick it connects".
                return Math.Max(1, (int)Math.Ceiling(this.DisconnectThresholdSeconds * this.SyncRatePerSecond));
            }
        }
        public bool EnableAutomaticAuthorityTransfer { get; set; }
        public bool EnableDirtyOnlySync { get; set; }
        public bool EnableParallelWriteTickLogging { get; set; }
        public int ParallelWriteTickWorkerThreadLimit { get; set; }

        private Configurations()
        {
            this.SyncRatePerSecond = 30;
            // Was 1000 frames, which at the default 30Hz was 33.3s.
            this.DisconnectThresholdSeconds = 30.0;
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
