using DedicatedServer.Demo.JumpingGame;
using System;
using System.Diagnostics;
using System.Threading;

namespace DedicatedServer
{
    class Program
    {
        private const ushort DefaultListeningPort = 5000;
        private const string Usage = "usage: DedicatedServer [listeningPort] [--sync-rate framesPerSecond]";

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
            while (isRunning)
            {
                double elapsedSeconds = tickClock.Elapsed.TotalSeconds;
                float deltaTime = (float)(elapsedSeconds - previousElapsedSeconds);
                previousElapsedSeconds = elapsedSeconds;
                jumpingGame.Update(deltaTime);
                Thread.Sleep(1);
            }
            jumpingGame.Dispose();
        }
    }
}
