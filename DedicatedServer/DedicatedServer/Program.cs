using DedicatedServer.Demo.JumpingGame;
using System;
using System.Diagnostics;
using System.Threading;

namespace DedicatedServer
{
    class Program
    {
        private const ushort DefaultListeningPort = 5000;

        static void Main(string[] args)
        {
            ushort listeningPort = DefaultListeningPort;
            if (args.Length > 0 && !ushort.TryParse(args[0], out listeningPort))
            {
                Console.WriteLine("usage: DedicatedServer [listeningPort]");
                return;
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
