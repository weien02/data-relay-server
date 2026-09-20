using DedicatedServer.Demo.JumpingGame;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace DedicatedServer
{
    class Program
    {
        private const ushort DefaultListeningPort = 5000;
        private const string Usage = "usage: DedicatedServer [listeningPort] [--sync-rate framesPerSecond]";

        /// <summary>
        /// How long before a sync frame is due to stop sleeping and spin instead. Even with the
        /// timer resolution raised, Thread.Sleep can still overshoot by about a millisecond, and
        /// overshooting is what pushes a frame into the following tick.
        /// </summary>
        private const double SpinBeforeDeadlineSeconds = 0.0015;

        /// <summary>
        /// The longest the loop will sleep while no sync frame is due. Received packets wait in
        /// the socket buffer until a tick drains them, so this bounds how long inbound client
        /// state sits unread. It has to stay well under the sync interval, both to keep that
        /// latency low and because the frame synchronisation controller treats a tick longer
        /// than the interval as the loop failing to keep up.
        /// </summary>
        private const double SocketServiceIntervalSeconds = 0.004;

        // Windows' default timer resolution is ~15.6ms, so Thread.Sleep(1) really sleeps about
        // that long: measured at 13.58ms on the development machine, which is 41% of a 30Hz
        // frame spent doing nothing. Requesting 1ms resolution makes the wait below land near
        // its deadline instead of overshooting it. Since Windows 10 2004 the request applies to
        // this process only, so it does not degrade timer behaviour system wide.
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint milliseconds);

        static void Main(string[] args)
        {
            ushort listeningPort = DefaultListeningPort;
            // 0 means "leave whatever the demo configured alone".
            int syncRatePerSecond = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--sync-rate")
                {
                    if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out syncRatePerSecond) || syncRatePerSecond <= 0)
                    {
                        Console.WriteLine(Usage);
                        return;
                    }
                    i++;
                }
                else if (ushort.TryParse(args[i], out ushort parsedPort))
                {
                    listeningPort = parsedPort;
                }
                else
                {
                    Console.WriteLine(Usage);
                    return;
                }
            }

            // Printed so that a profiler can be attached by process id without
            // having to look it up, e.g. dotnet-counters monitor -p <pid>
            Console.WriteLine("process id " + Process.GetCurrentProcess().Id);

            // Ctrl+C shuts the tick loop down through Dispose instead of killing
            // the process, so the listening socket is released and any future
            // diagnostics session gets a chance to flush.
            bool isRunning = true;
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                isRunning = false;
            };

            JumpingGame jumpingGame = new JumpingGame();
            jumpingGame.Initialize();
            // Initialize applies the demo's own configuration, so any override has
            // to land after it and before StartServer, which is where the frame
            // synchronisation controller reads the rate for the last time.
            if (syncRatePerSecond > 0)
            {
                jumpingGame.configurations.SyncRatePerSecond = syncRatePerSecond;
            }
            // Recorded per run so a captured log says which rate produced it.
            Console.WriteLine("sync rate " + jumpingGame.configurations.SyncRatePerSecond + " frames/second");
            jumpingGame.StartServer(listeningPort);
            // Measure real elapsed time per iteration rather than assuming a fixed
            // step. The sync frame accumulator paces itself from this value, so a
            // hardcoded one silently decouples the effective sync rate from
            // Configurations.SyncRatePerSecond.
            Stopwatch tickClock = Stopwatch.StartNew();
            double previousElapsedSeconds = 0;
            // Wait until the next sync frame is actually due rather than sleeping a fixed amount.
            // A blind Thread.Sleep(1) costs a whole timer tick, so a frame due part way through
            // one does not get serviced until the tick after it. The sync rate survives that,
            // because the accumulator carries its remainder, but the frames arrive in bursts
            // instead of evenly spaced, and that jitter is what clients actually feel.
            double syncIntervalSeconds = 1.0 / jumpingGame.configurations.SyncRatePerSecond;
            double nextFrameTimeSeconds = syncIntervalSeconds;
            bool raisedTimerResolution = false;
            if (OperatingSystem.IsWindows())
            {
                raisedTimerResolution = TimeBeginPeriod(1) == 0;
            }
            try
            {
                while (isRunning)
                {
                    double elapsedSeconds = tickClock.Elapsed.TotalSeconds;
                    float deltaTime = (float)(elapsedSeconds - previousElapsedSeconds);
                    previousElapsedSeconds = elapsedSeconds;
                    jumpingGame.Update(deltaTime);

                    // Step over any deadlines that have already passed. When a tick runs longer
                    // than the sync interval there is nothing left to wait for, so the loop falls
                    // through and runs flat out instead of trying to honour a schedule it cannot
                    // meet. The frame synchronisation controller handles the catch-up from there.
                    double nowSeconds = tickClock.Elapsed.TotalSeconds;
                    while (nextFrameTimeSeconds <= nowSeconds)
                    {
                        nextFrameTimeSeconds += syncIntervalSeconds;
                    }

                    double secondsUntilFrame = nextFrameTimeSeconds - nowSeconds;
                    if (secondsUntilFrame <= SocketServiceIntervalSeconds)
                    {
                        // The next sync frame is imminent, so wait precisely and land on it.
                        double sleepSeconds = secondsUntilFrame - SpinBeforeDeadlineSeconds;
                        if (sleepSeconds > 0.001)
                        {
                            Thread.Sleep((int)(sleepSeconds * 1000.0));
                        }
                        while (tickClock.Elapsed.TotalSeconds < nextFrameTimeSeconds)
                        {
                            Thread.SpinWait(50);
                        }
                    }
                    else
                    {
                        // No frame due yet. Keep ticking anyway so received packets are drained
                        // promptly instead of waiting out the rest of the frame in the socket
                        // buffer. Precision does not matter here, so a plain sleep will do.
                        Thread.Sleep((int)(SocketServiceIntervalSeconds * 1000.0));
                    }
                }
            }
            finally
            {
                if (raisedTimerResolution)
                {
                    TimeEndPeriod(1);
                }
            }
            jumpingGame.Dispose();
        }
    }
}
