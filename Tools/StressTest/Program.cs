using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StressTest
{
    internal sealed class Config
    {
        public int clientCount = 16;
        public double loadSeconds = 20;
        public double quiesceSeconds = 3;
        public IPAddress serverAddress = IPAddress.Loopback;
        public ushort serverPort = 5000;
        public int registryID = 0;
        public int publishEverySyncFrames = 1;
        public int joinLateCount;
        public double joinLateAfterSeconds = 5;
        public int loginStaggerMilliseconds = 10;
        public int spawnStaggerMilliseconds = 20;
        public int spawnSettleTimeoutSeconds = 10;
        public int handshakeTimeoutMilliseconds = 2000;
        public int handshakeAttempts = 3;
        public int socketReceiveBufferBytes = 4 * 1024 * 1024;
        public uint clientIDBase;
        public string jsonPath;
        public bool allowPlayerIDOverflow;

        // ---- reconnection episodes --------------------------------------------------------
        public int killCount;
        public KillMode killMode = KillMode.Rejoin;
        public double killForMinSeconds = 1;
        public double killForMaxSeconds = 8;
        public double killNotBeforeSeconds = 2;
        /// <summary>Slack left between the last possible return and the end of load, so recovery finishes inside the window.</summary>
        public double killSettleSeconds = 3;
        public double recoverTimeoutSeconds = 10;
        public int killSeed;
        public bool killSeedGiven;

        /// <summary>
        /// How long the server tolerates silence before it drops a session. Mirrors
        /// Configurations.DisconnectThresholdSeconds, which the JumpingGame demo server sets to 3.
        ///
        /// Episodes are tagged against this so the two regimes can be reported apart: below it
        /// the server never notices the client went away at all.
        ///
        /// This used to need the server's sync rate as well, because the timeout was stored as a
        /// frame count and so meant a different duration at every rate. It is a duration now, so
        /// the rate no longer comes into it.
        /// </summary>
        public double serverDisconnectThresholdSeconds = 3.0;

        public static bool TryParse(string[] args, out Config config, out string error)
        {
            config = new Config();
            // A distinct base per run by default. The server keeps a session forever and matches
            // a reconnect by client id, so reusing ids against a server that is still running
            // silently resumes the previous run's sessions and player ids.
            config.clientIDBase = (uint)(DateTime.UtcNow.Ticks & 0x00FFFFFF) * 1000;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : null;
                switch (argument)
                {
                    case "--clients":
                        if (!TryTakeInt(value, ref i, out config.clientCount)) { error = "--clients needs a number"; return false; }
                        break;
                    case "--duration":
                        if (!TryTakeDouble(value, ref i, out config.loadSeconds)) { error = "--duration needs a number of seconds"; return false; }
                        break;
                    case "--quiesce":
                        if (!TryTakeDouble(value, ref i, out config.quiesceSeconds)) { error = "--quiesce needs a number of seconds"; return false; }
                        break;
                    case "--host":
                        if (value == null || !IPAddress.TryParse(value, out config.serverAddress))
                        {
                            if (value == null) { error = "--host needs an address"; return false; }
                            IPAddress[] resolved;
                            try { resolved = Dns.GetHostAddresses(value); }
                            catch (Exception) { error = "could not resolve host " + value; return false; }
                            if (resolved.Length == 0) { error = "could not resolve host " + value; return false; }
                            config.serverAddress = resolved[0];
                        }
                        i++;
                        break;
                    case "--port":
                    {
                        int port;
                        if (!TryTakeInt(value, ref i, out port) || port <= 0 || port > ushort.MaxValue) { error = "--port needs a port number"; return false; }
                        config.serverPort = (ushort)port;
                        break;
                    }
                    case "--publish-every":
                        if (!TryTakeInt(value, ref i, out config.publishEverySyncFrames) || config.publishEverySyncFrames < 1) { error = "--publish-every needs a positive number of sync frames"; return false; }
                        break;
                    case "--join-late":
                        if (!TryTakeInt(value, ref i, out config.joinLateCount) || config.joinLateCount < 0) { error = "--join-late needs a non-negative number of clients"; return false; }
                        break;
                    case "--join-late-after":
                        if (!TryTakeDouble(value, ref i, out config.joinLateAfterSeconds) || config.joinLateAfterSeconds < 0) { error = "--join-late-after needs a number of seconds"; return false; }
                        break;
                    case "--registry-id":
                        if (!TryTakeInt(value, ref i, out config.registryID)) { error = "--registry-id needs a number"; return false; }
                        break;
                    case "--login-stagger":
                        if (!TryTakeInt(value, ref i, out config.loginStaggerMilliseconds)) { error = "--login-stagger needs milliseconds"; return false; }
                        break;
                    case "--spawn-stagger":
                        if (!TryTakeInt(value, ref i, out config.spawnStaggerMilliseconds)) { error = "--spawn-stagger needs milliseconds"; return false; }
                        break;
                    case "--rcvbuf":
                        if (!TryTakeInt(value, ref i, out config.socketReceiveBufferBytes)) { error = "--rcvbuf needs a number of bytes"; return false; }
                        break;
                    case "--client-id-base":
                    {
                        int parsed;
                        if (!TryTakeInt(value, ref i, out parsed) || parsed < 0) { error = "--client-id-base needs a non-negative number"; return false; }
                        config.clientIDBase = (uint)parsed;
                        break;
                    }
                    case "--json":
                        if (value == null) { error = "--json needs a file path"; return false; }
                        config.jsonPath = value;
                        i++;
                        break;
                    case "--allow-player-id-overflow":
                        config.allowPlayerIDOverflow = true;
                        break;
                    case "--kill":
                        if (!TryTakeInt(value, ref i, out config.killCount) || config.killCount < 0) { error = "--kill needs a non-negative number of clients"; return false; }
                        break;
                    case "--kill-mode":
                        if (value == null) { error = "--kill-mode needs rejoin or blackout"; return false; }
                        if (string.Equals(value, "rejoin", StringComparison.OrdinalIgnoreCase)) { config.killMode = KillMode.Rejoin; }
                        else if (string.Equals(value, "blackout", StringComparison.OrdinalIgnoreCase)) { config.killMode = KillMode.Blackout; }
                        else { error = "--kill-mode must be rejoin or blackout"; return false; }
                        i++;
                        break;
                    case "--kill-for-min":
                        if (!TryTakeDouble(value, ref i, out config.killForMinSeconds) || config.killForMinSeconds < 0) { error = "--kill-for-min needs a non-negative number of seconds"; return false; }
                        break;
                    case "--kill-for-max":
                        if (!TryTakeDouble(value, ref i, out config.killForMaxSeconds) || config.killForMaxSeconds < 0) { error = "--kill-for-max needs a non-negative number of seconds"; return false; }
                        break;
                    case "--kill-not-before":
                        if (!TryTakeDouble(value, ref i, out config.killNotBeforeSeconds) || config.killNotBeforeSeconds < 0) { error = "--kill-not-before needs a non-negative number of seconds"; return false; }
                        break;
                    case "--kill-settle":
                        if (!TryTakeDouble(value, ref i, out config.killSettleSeconds) || config.killSettleSeconds < 0) { error = "--kill-settle needs a non-negative number of seconds"; return false; }
                        break;
                    case "--recover-timeout":
                        if (!TryTakeDouble(value, ref i, out config.recoverTimeoutSeconds) || config.recoverTimeoutSeconds <= 0) { error = "--recover-timeout needs a positive number of seconds"; return false; }
                        break;
                    case "--kill-seed":
                        if (!TryTakeInt(value, ref i, out config.killSeed)) { error = "--kill-seed needs a number"; return false; }
                        config.killSeedGiven = true;
                        break;
                    case "--server-disconnect-seconds":
                        if (!TryTakeDouble(value, ref i, out config.serverDisconnectThresholdSeconds) || config.serverDisconnectThresholdSeconds <= 0) { error = "--server-disconnect-seconds needs a positive number of seconds"; return false; }
                        break;
                    case "--help":
                    case "-h":
                        error = "help";
                        return false;
                    default:
                        error = "unknown argument " + argument;
                        return false;
                }
            }

            if (config.clientCount < 2)
            {
                error = "--clients must be at least 2, because the whole point is what each client sees of the others";
                return false;
            }
            if (config.joinLateCount >= config.clientCount)
            {
                error = "--join-late must leave at least two clients to start the run with";
                return false;
            }
            if (config.joinLateCount > 0 && config.joinLateAfterSeconds >= config.loadSeconds)
            {
                error = "--join-late-after must be shorter than --duration, otherwise the late clients never join";
                return false;
            }
            // GameRoom.GeneratePlayerID throws once 255 ids have been handed out, and that
            // exception is not caught anywhere in the server's tick loop.
            if (config.clientCount > 255 && !config.allowPlayerIDOverflow)
            {
                error = "--clients above 255 exhausts the server's player id space (PlayerID is a byte) and crashes it."
                    + " Pass --allow-player-id-overflow if that is what you mean to test.";
                return false;
            }

            if (config.killCount > 0)
            {
                // A seed is always chosen, not only when asked for, so every run prints one that
                // reproduces it exactly. Random kill times are worthless for a report otherwise.
                if (!config.killSeedGiven)
                {
                    config.killSeed = Environment.TickCount;
                }
                int killable = config.clientCount - config.joinLateCount;
                if (config.killCount >= killable)
                {
                    error = "--kill must leave at least one of the " + killable + " starting clients alive,"
                        + " because a killed client's recovery is judged against what the others are publishing.";
                    return false;
                }
                if (config.killForMinSeconds > config.killForMaxSeconds)
                {
                    error = "--kill-for-min cannot be larger than --kill-for-max";
                    return false;
                }
                double latestKill = config.loadSeconds - config.killForMaxSeconds - config.killSettleSeconds;
                if (latestKill <= config.killNotBeforeSeconds)
                {
                    error = "there is no room to kill anyone: --duration " + config.loadSeconds.ToString(CultureInfo.InvariantCulture)
                        + "s has to cover --kill-not-before (" + config.killNotBeforeSeconds.ToString(CultureInfo.InvariantCulture)
                        + "s) plus --kill-for-max (" + config.killForMaxSeconds.ToString(CultureInfo.InvariantCulture)
                        + "s) plus --kill-settle (" + config.killSettleSeconds.ToString(CultureInfo.InvariantCulture)
                        + "s). Raise --duration or lower one of those."
                        + " Without the slack a reconnect would still be in flight when the run ends,"
                        + " and the final cross check would report the harness's own timing as a fault.";
                    return false;
                }
            }
            return true;
        }

        private static bool TryTakeInt(string value, ref int index, out int result)
        {
            result = 0;
            if (value == null || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                return false;
            }
            index++;
            return true;
        }

        private static bool TryTakeDouble(string value, ref int index, out double result)
        {
            result = 0;
            if (value == null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                return false;
            }
            index++;
            return true;
        }

        public const string Usage =
            "usage: StressTest [options]\n" +
            "\n" +
            "  --clients N            number of simulated users (default 16, server caps at 255)\n" +
            "  --duration S           seconds of load after everyone has spawned (default 20)\n" +
            "  --quiesce S            seconds to stop moving and let the last updates land (default 3)\n" +
            "  --host ADDRESS         server address (default 127.0.0.1)\n" +
            "  --port N               server port (default 5000)\n" +
            "  --publish-every N      publish state every Nth sync frame (default 1, every frame)\n" +
            "  --join-late N          hold N clients back and join them mid-run (default 0)\n" +
            "  --join-late-after S    when the held-back clients join, in seconds into the load (default 5)\n" +
            "  --registry-id N        entity registry id to spawn (default 0, the JumpingGame avatar)\n" +
            "  --login-stagger MS     delay between logins (default 10)\n" +
            "  --spawn-stagger MS     delay between spawn requests (default 20)\n" +
            "  --rcvbuf BYTES         per socket receive buffer (default 4194304)\n" +
            "  --client-id-base N     first client id (default unique per run; reusing one against a\n" +
            "                         running server resumes that run's sessions instead of new ones)\n" +
            "  --json PATH            also write the full report as JSON\n" +
            "  --allow-player-id-overflow   permit --clients above 255\n" +
            "\n" +
            "reconnection episodes (kill a client mid-run and measure how long it takes to come back)\n" +
            "\n" +
            "  --kill N               kill N clients once each, at random moments (default 0, off)\n" +
            "  --kill-mode MODE       rejoin (close the socket and log in again, like a restarted\n" +
            "                         process) or blackout (keep the socket, go silent, like losing\n" +
            "                         wifi). Default rejoin.\n" +
            "  --kill-for-min S       shortest time a killed client stays away (default 1)\n" +
            "  --kill-for-max S       longest time a killed client stays away (default 8)\n" +
            "  --kill-not-before S    earliest kill, in seconds into the load (default 2)\n" +
            "  --kill-settle S        slack left between the last return and the end of load so a\n" +
            "                         recovery cannot run past the run (default 3)\n" +
            "  --recover-timeout S    give up on an episode after this long (default 10)\n" +
            "  --kill-seed N          seed for the kill times and away times. One is always chosen\n" +
            "                         and printed, so any run can be replayed exactly.\n" +
            "  --server-disconnect-seconds S  the server's DisconnectThresholdSeconds (default 3, which\n" +
            "                         is what the JumpingGame demo server sets). This is the threshold an\n" +
            "                         away time is compared against: below it the server never notices\n" +
            "                         the client left at all.\n";
    }

    /// <summary>
    /// The outcome of the final sweep: what every observer believed about every other player
    /// once the load stopped and the last updates had time to arrive.
    /// </summary>
    internal sealed class VerificationResult
    {
        public long pairsChecked;
        public long converged;
        public long missingEntity;
        public long noDataAtAll;
        public long diverged;
        public long divergedSequenceShortfallTotal;
        public long divergedSequenceShortfallMax;
        public long corruptFinalState;
        public long authorityMismatch;
        public long respawnConverged;
        public long respawnMismatch;
        public long respawnMissingLostUpdate;
        public long respawnMissingJoinedLate;
        public readonly List<string> samples = new List<string>();

        public void AddSample(string text)
        {
            if (this.samples.Count < 12)
            {
                this.samples.Add(text);
            }
        }
    }

    /// <summary>
    /// The clients that are up, and the receive loop each one is running. Clients can be added
    /// while the load is already running, so every read takes a snapshot under the lock rather
    /// than iterating the live list.
    /// </summary>
    internal sealed class LiveSet
    {
        private readonly object _gate = new object();
        private readonly List<VirtualClient> _clients = new List<VirtualClient>();
        private readonly List<Task> _loops = new List<Task>();

        public void Add(VirtualClient client, Task loop)
        {
            lock (this._gate)
            {
                this._clients.Add(client);
                this._loops.Add(loop);
            }
        }

        /// <summary>
        /// Registers a replacement receive loop for a client that is already in the set, so the
        /// final wait covers it. A rejoin starts a new loop but must not add the client twice,
        /// or the cross check would compare it against itself.
        /// </summary>
        public void AddLoop(Task loop)
        {
            lock (this._gate)
            {
                this._loops.Add(loop);
            }
        }

        public List<VirtualClient> Snapshot()
        {
            lock (this._gate)
            {
                return new List<VirtualClient>(this._clients);
            }
        }

        public List<Task> SnapshotLoops()
        {
            lock (this._gate)
            {
                return new List<Task>(this._loops);
            }
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            Config config;
            string error;
            if (!Config.TryParse(args, out config, out error))
            {
                if (error == "help")
                {
                    Console.Write(Config.Usage);
                    return 0;
                }
                Console.Error.WriteLine("error: " + error);
                Console.Error.WriteLine();
                Console.Error.Write(Config.Usage);
                return 2;
            }
            try
            {
                return RunAsync(config).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("error: " + exception);
                return 2;
            }
        }

        private static async Task<int> RunAsync(Config config)
        {
            // Checked before any load runs. The report is only written at the very end, so an
            // unwritable path discovered then would cost the whole run.
            if (config.jsonPath != null)
            {
                try
                {
                    Report.EnsureDirectoryExists(config.jsonPath);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("error: cannot write --json to " + config.jsonPath);
                    Console.Error.WriteLine("       " + exception.Message);
                    return 2;
                }
            }

            SharedState shared = new SharedState();
            List<VirtualClient> clients = new List<VirtualClient>(config.clientCount);
            for (int i = 0; i < config.clientCount; i++)
            {
                clients.Add(new VirtualClient(config, shared, i, config.clientIDBase + (uint)i));
            }

            CancellationTokenSource cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            Console.WriteLine("target          " + config.serverAddress + ":" + config.serverPort);
            Console.WriteLine("clients         " + config.clientCount);
            Console.WriteLine("client id base  " + config.clientIDBase);
            if (config.killCount > 0)
            {
                Console.WriteLine("kills           " + config.killCount + " clients, " + config.killMode.ToString().ToLowerInvariant()
                    + ", away " + config.killForMinSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                    + "-" + config.killForMaxSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s"
                    + ", seed " + config.killSeed);
                Console.WriteLine("server drops at " + config.serverDisconnectThresholdSeconds.ToString("0.00", CultureInfo.InvariantCulture)
                    + "s of silence");
                if (config.killMode == KillMode.Blackout && config.killForMaxSeconds >= config.serverDisconnectThresholdSeconds)
                {
                    Console.WriteLine();
                    Console.WriteLine("  note: some blackouts will outlast that threshold. The server only clears a");
                    Console.WriteLine("        session's disconnected flag on a fresh login, so a client that merely goes");
                    Console.WriteLine("        quiet for longer than this can never come back: it stops being broadcast to,");
                    Console.WriteLine("        never sees another frame number, and a frame gap is the only thing that");
                    Console.WriteLine("        triggers a repair. Those episodes are reported as never recovered, and that");
                    Console.WriteLine("        is the framework's behaviour, not a fault in this harness.");
                }
            }
            Console.WriteLine();

            // Clients held back to join mid-run. They are the probe for what a client that joins
            // after a value was last written can and cannot learn about the world.
            int initialCount = config.clientCount - config.joinLateCount;
            LiveSet liveSet = new LiveSet();

            // ---- phase 1: connect and log in -------------------------------------------------
            Console.Write("connecting ");
            for (int i = 0; i < initialCount; i++)
            {
                await BringUpAsync(clients[i], liveSet, cancellation.Token).ConfigureAwait(false);
                if (config.loginStaggerMilliseconds > 0)
                {
                    await DelaySafeAsync(config.loginStaggerMilliseconds / 1000.0, cancellation.Token).ConfigureAwait(false);
                }
            }
            List<VirtualClient> live = liveSet.Snapshot();
            Console.WriteLine(live.Count + "/" + initialCount + " logged in");
            if (live.Count < 2)
            {
                Console.Error.WriteLine("error: fewer than two clients logged in, so there is nothing to cross check.");
                Console.Error.WriteLine("       is the server running on " + config.serverAddress + ":" + config.serverPort + "?");
                foreach (VirtualClient client in clients)
                {
                    client.Close();
                }
                return 2;
            }

            // ---- phase 2: spawn one avatar each ----------------------------------------------
            shared.Phase = Phase.Spawning;
            Console.Write("spawning ");
            foreach (VirtualClient client in live)
            {
                client.RequestSpawn();
                if (config.spawnStaggerMilliseconds > 0)
                {
                    await DelaySafeAsync(config.spawnStaggerMilliseconds / 1000.0, cancellation.Token).ConfigureAwait(false);
                }
            }
            int settled = await WaitForSpawnsAsync(live, config, cancellation.Token).ConfigureAwait(false);
            Console.WriteLine(settled + "/" + live.Count + " clients see all " + live.Count + " avatars");

            // ---- phase 3: load ---------------------------------------------------------------
            shared.Phase = Phase.Load;
            Task lateJoins = config.joinLateCount > 0
                ? JoinLateAsync(clients, initialCount, liveSet, config, cancellation.Token)
                : Task.CompletedTask;
            // Victims are drawn from the clients that started the run, never from the late
            // joiners, which do not exist yet when the schedule is drawn up.
            Task kills = config.killCount > 0
                ? RunKillsAsync(live, liveSet, config, cancellation.Token)
                : Task.CompletedTask;
            LoadWindow loadWindow = await RunLoadAsync(liveSet, config, cancellation.Token).ConfigureAwait(false);
            await lateJoins.ConfigureAwait(false);
            await kills.ConfigureAwait(false);
            live = liveSet.Snapshot();

            // ---- phase 4: quiesce ------------------------------------------------------------
            shared.Phase = Phase.Quiesce;
            Console.WriteLine();
            Console.WriteLine("quiescing for " + config.quiesceSeconds.ToString("0.#", CultureInfo.InvariantCulture)
                + "s (clients keep their sessions alive but stop moving)");
            await DelaySafeAsync(config.quiesceSeconds, cancellation.Token).ConfigureAwait(false);

            // ---- phase 5: stop and verify ----------------------------------------------------
            shared.Phase = Phase.Finished;
            cancellation.Cancel();
            // Awaiting every receive loop is also the memory barrier the final sweep relies on:
            // after this point no other thread is touching any client's observed state.
            await Task.WhenAll(liveSet.SnapshotLoops()).ConfigureAwait(false);
            foreach (VirtualClient client in clients)
            {
                client.Close();
            }

            VerificationResult verification = Verify(live, shared, config);
            bool failed = Report.Print(clients, live, verification, config, loadWindow);
            if (config.jsonPath != null)
            {
                Report.WriteJson(config.jsonPath, clients, live, verification, config, loadWindow);
                Console.WriteLine("json report written to " + config.jsonPath);
            }
            return failed ? 1 : 0;
        }

        /// <summary>
        /// Connects, logs in, and starts the receive loop. The loop starts before the client
        /// spawns anything, so it cannot miss the creation broadcasts that follow.
        /// </summary>
        private static async Task<bool> BringUpAsync(VirtualClient client, LiveSet liveSet, CancellationToken cancellationToken)
        {
            bool ok = await client.ConnectAndLoginAsync(cancellationToken).ConfigureAwait(false);
            if (!ok)
            {
                return false;
            }
            liveSet.Add(client, client.StartLoop(cancellationToken));
            return true;
        }

        /// <summary>
        /// Brings up the held-back clients once the run is already under way. They have missed
        /// every value that was last written before they arrived, and nothing in the framework
        /// sends a joining client a snapshot, so the cross check can tell what that costs.
        /// </summary>
        private static async Task JoinLateAsync(List<VirtualClient> clients, int firstIndex, LiveSet liveSet, Config config, CancellationToken cancellationToken)
        {
            await DelaySafeAsync(config.joinLateAfterSeconds, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            int joined = 0;
            for (int i = firstIndex; i < clients.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                if (await BringUpAsync(clients[i], liveSet, cancellationToken).ConfigureAwait(false))
                {
                    clients[i].RequestSpawn();
                    joined++;
                }
                if (config.spawnStaggerMilliseconds > 0)
                {
                    await DelaySafeAsync(config.spawnStaggerMilliseconds / 1000.0, cancellationToken).ConfigureAwait(false);
                }
            }
            Console.WriteLine("  (" + joined + " late clients joined at "
                + config.joinLateAfterSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s)");
        }

        /// <summary>
        /// Schedules the reconnection episodes and runs them.
        ///
        /// Each victim gets its own task rather than all of them running from one queue, so two
        /// episodes whose windows overlap really do overlap. Several clients recovering at once
        /// is the interesting case: SyncEntitiesToSingleClient sends one packet per entity, all
        /// inside a single server tick, so simultaneous returns land as one burst.
        /// </summary>
        private static async Task RunKillsAsync(List<VirtualClient> candidates, LiveSet liveSet, Config config, CancellationToken cancellationToken)
        {
            Random random = new Random(config.killSeed);
            double latestKill = config.loadSeconds - config.killForMaxSeconds - config.killSettleSeconds;

            // Fisher-Yates over the candidates, so each client is picked at most once.
            List<VirtualClient> pool = new List<VirtualClient>(candidates);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                VirtualClient swap = pool[i];
                pool[i] = pool[j];
                pool[j] = swap;
            }

            int victimCount = Math.Min(config.killCount, pool.Count);
            List<Task> running = new List<Task>(victimCount);
            for (int i = 0; i < victimCount; i++)
            {
                VirtualClient victim = pool[i];
                double killAt = config.killNotBeforeSeconds + random.NextDouble() * (latestKill - config.killNotBeforeSeconds);
                double awayFor = config.killForMinSeconds + random.NextDouble() * (config.killForMaxSeconds - config.killForMinSeconds);
                running.Add(RunOneEpisodeAsync(victim, liveSet, config, killAt, awayFor, cancellationToken));
            }
            await Task.WhenAll(running).ConfigureAwait(false);
        }

        private static async Task RunOneEpisodeAsync(VirtualClient victim, LiveSet liveSet, Config config, double killAtSeconds, double awaySeconds, CancellationToken cancellationToken)
        {
            await DelaySafeAsync(killAtSeconds, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (!victim.HasEntity)
            {
                // Nothing to measure: it never got as far as owning an avatar, so there is no
                // authority to lose and nothing for the others to have been watching.
                Console.WriteLine("  [kill] client " + victim.Index + " skipped, it never owned an avatar");
                return;
            }

            double thresholdSeconds = config.serverDisconnectThresholdSeconds;
            ReconnectEpisode episode = await victim.KillAsync(config.killMode, awaySeconds, thresholdSeconds).ConfigureAwait(false);
            Console.WriteLine("  [kill] player " + episode.playerID + " " + config.killMode.ToString().ToLowerInvariant()
                + " for " + awaySeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s"
                + (episode.crossedServerThreshold ? " (past the server's " + thresholdSeconds.ToString("0.0", CultureInfo.InvariantCulture) + "s threshold)" : " (under the threshold)"));

            await DelaySafeAsync(awaySeconds, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                episode.outcome = "run ended while the client was still away";
                return;
            }

            bool back = await victim.ResumeAsync(liveSet.Snapshot(), cancellationToken).ConfigureAwait(false);
            if (back && victim.Loop != null)
            {
                liveSet.AddLoop(victim.Loop);
            }
            if (!back)
            {
                Console.WriteLine("  [back] player " + episode.playerID + " FAILED: " + episode.outcome);
                return;
            }

            await victim.AwaitRecoveryAsync(config.recoverTimeoutSeconds, cancellationToken).ConfigureAwait(false);
            Console.WriteLine("  [back] player " + episode.playerID + " "
                + (episode.Recovered
                    ? "view restored in " + episode.RecoveryMs.ToString("0", CultureInfo.InvariantCulture) + " ms"
                    : episode.outcome));
        }

        private static async Task<int> WaitForSpawnsAsync(List<VirtualClient> live, Config config, CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(config.spawnSettleTimeoutSeconds);
            while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
            {
                int settled = 0;
                foreach (VirtualClient client in live)
                {
                    if (client.ObservedEntityCount >= live.Count)
                    {
                        settled++;
                    }
                }
                if (settled == live.Count)
                {
                    return settled;
                }
                await DelaySafeAsync(0.2, cancellationToken).ConfigureAwait(false);
            }
            int finalSettled = 0;
            foreach (VirtualClient client in live)
            {
                if (client.ObservedEntityCount >= live.Count)
                {
                    finalSettled++;
                }
            }
            return finalSettled;
        }

        private static async Task<LoadWindow> RunLoadAsync(LiveSet liveSet, Config config, CancellationToken cancellationToken)
        {
            List<VirtualClient> atStartClients = liveSet.Snapshot();
            ClientStats atStart = Report.Aggregate(atStartClients);
            // Per-client baselines, so the observed sync rate can be taken from a client that
            // was present for the whole window rather than diluted by one that joined late.
            Dictionary<VirtualClient, long> framesAtStart = new Dictionary<VirtualClient, long>();
            foreach (VirtualClient client in atStartClients)
            {
                framesAtStart.Add(client, client.stats.syncFrameBeginCount);
            }
            DateTime start = DateTime.UtcNow;
            DateTime end = start.AddSeconds(config.loadSeconds);
            long previousIn = atStart.datagramsReceived;
            long previousOut = atStart.datagramsSent;
            DateTime previousSampleTime = start;

            Console.WriteLine();
            Console.WriteLine("  elapsed   in pkt/s  out pkt/s   in MiB/s   mean lag  max lag  gaps");
            while (DateTime.UtcNow < end && !cancellationToken.IsCancellationRequested)
            {
                await DelaySafeAsync(1.0, cancellationToken).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                double window = (now - previousSampleTime).TotalSeconds;
                if (window <= 0)
                {
                    continue;
                }
                long totalIn = 0, totalOut = 0, totalInBytes = 0, lagSum = 0, lagSamples = 0, lagMax = 0, gaps = 0;
                foreach (VirtualClient client in liveSet.Snapshot())
                {
                    ClientStats stats = client.stats;
                    totalIn += stats.datagramsReceived;
                    totalOut += stats.datagramsSent;
                    totalInBytes += stats.bytesReceived;
                    lagSum += stats.lagSum;
                    lagSamples += stats.lagSamples;
                    gaps += stats.syncFrameGapCount;
                    if (stats.lagMax > lagMax)
                    {
                        lagMax = stats.lagMax;
                    }
                }
                double meanLag = lagSamples > 0 ? (double)lagSum / lagSamples : 0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:0.0}s  {1,9:0}  {2,9:0}  {3,9:0.00}  {4,9:0.00}  {5,7}  {6,4}",
                    (now - start).TotalSeconds,
                    (totalIn - previousIn) / window,
                    (totalOut - previousOut) / window,
                    (totalInBytes - atStart.bytesReceived) / (now - start).TotalSeconds / (1024.0 * 1024.0),
                    meanLag,
                    lagMax,
                    gaps));
                previousIn = totalIn;
                previousOut = totalOut;
                previousSampleTime = now;
            }

            List<VirtualClient> atEndClients = liveSet.Snapshot();
            ClientStats atEnd = Report.Aggregate(atEndClients);
            LoadWindow measured = new LoadWindow();
            measured.seconds = (DateTime.UtcNow - start).TotalSeconds;
            measured.clientCount = atEndClients.Count;
            measured.datagramsIn = atEnd.datagramsReceived - atStart.datagramsReceived;
            measured.datagramsOut = atEnd.datagramsSent - atStart.datagramsSent;
            measured.bytesIn = atEnd.bytesReceived - atStart.bytesReceived;
            measured.bytesOut = atEnd.bytesSent - atStart.bytesSent;
            measured.syncFrames = atEnd.syncFrameBeginCount - atStart.syncFrameBeginCount;
            measured.stateUpdates = atEnd.stateUpdatesSent - atStart.stateUpdatesSent;
            measured.entityData = atEnd.entityDataReceived - atStart.entityDataReceived;
            foreach (VirtualClient client in atEndClients)
            {
                long baseline;
                long delta = client.stats.syncFrameBeginCount - (framesAtStart.TryGetValue(client, out baseline) ? baseline : 0);
                if (delta > measured.maxSyncFramesForOneClient)
                {
                    measured.maxSyncFramesForOneClient = delta;
                }
            }
            return measured;
        }

        private static async Task DelaySafeAsync(double seconds, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// The cross check. For every ordered pair of distinct players, compare what the observer
        /// ended up believing about the owner's avatar against what the owner actually published.
        ///
        /// This runs after the load has stopped and after a quiesce window, so there is no
        /// legitimate reason for a view to still be behind. Anything short of the owner's final
        /// sequence number is state the observer can no longer recover: with dirty-only sync the
        /// server resends nothing, and the only repair path in the framework is triggered by a
        /// gap in sync frame numbers, not by a lost data packet.
        /// </summary>
        private static VerificationResult Verify(List<VirtualClient> live, SharedState shared, Config config)
        {
            VerificationResult result = new VerificationResult();

            foreach (VirtualClient observer in live)
            {
                foreach (VirtualClient owner in live)
                {
                    if (ReferenceEquals(observer, owner) || !owner.HasEntity)
                    {
                        continue;
                    }
                    result.pairsChecked++;
                    int truthSequence = owner.Sequence;

                    ObservedEntity entity;
                    if (!observer.observed.TryGetValue(owner.EntityRuntimeID, out entity))
                    {
                        result.missingEntity++;
                        result.AddSample("player " + observer.PlayerID + " never saw player " + owner.PlayerID
                            + "'s avatar (entity " + owner.EntityRuntimeID + ") exist at all");
                        continue;
                    }

                    if (entity.authorityPlayerID != owner.PlayerID)
                    {
                        result.authorityMismatch++;
                        result.AddSample("player " + observer.PlayerID + " believes entity " + owner.EntityRuntimeID
                            + " belongs to player " + entity.authorityPlayerID + ", not " + owner.PlayerID);
                    }

                    if (!entity.hasTransform || !entity.hasJumpState)
                    {
                        result.noDataAtAll++;
                        result.AddSample("player " + observer.PlayerID + " saw player " + owner.PlayerID
                            + "'s avatar created but never received its "
                            + (entity.hasTransform ? "jump state" : "transform"));
                    }
                    else
                    {
                        int observedSequence = (int)entity.jumpPower;
                        if (observedSequence != truthSequence)
                        {
                            long shortfall = truthSequence - observedSequence;
                            result.diverged++;
                            result.divergedSequenceShortfallTotal += shortfall;
                            if (shortfall > result.divergedSequenceShortfallMax)
                            {
                                result.divergedSequenceShortfallMax = shortfall;
                            }
                            result.AddSample("player " + observer.PlayerID + " is stuck at update " + observedSequence
                                + " of player " + owner.PlayerID + ", which published " + truthSequence
                                + " (" + shortfall + " behind, permanently)");
                        }
                        else if (entity.positionX != Sim.PositionX(owner.PlayerID, observedSequence)
                            || entity.positionY != Sim.PositionY(owner.PlayerID, observedSequence)
                            || entity.positionZ != Sim.PositionZ(owner.PlayerID, observedSequence)
                            || entity.rotationY != Sim.RotationY(observedSequence)
                            || entity.isChargingJump != Sim.IsChargingJump(observedSequence))
                        {
                            result.corruptFinalState++;
                            result.AddSample("player " + observer.PlayerID + " holds a state for player " + owner.PlayerID
                                + " at update " + observedSequence + " that player " + owner.PlayerID + " never published"
                                + " (position " + Format(entity.positionX) + "," + Format(entity.positionY) + "," + Format(entity.positionZ)
                                + " expected " + Format(Sim.PositionX(owner.PlayerID, observedSequence)) + ","
                                + Format(Sim.PositionY(owner.PlayerID, observedSequence)) + ","
                                + Format(Sim.PositionZ(owner.PlayerID, observedSequence)) + ")");
                        }
                        else
                        {
                            result.converged++;
                        }
                    }

                    // The respawn position is written once and never again, which makes it the
                    // probe for values a client can never catch up on.
                    if (!entity.hasRespawn)
                    {
                        uint publishFrame = Volatile.Read(ref shared.respawnPublishSyncFrame[owner.PlayerID]);
                        // The owner records the frame it last saw, and the server broadcasts a
                        // tick or two later, so only a clearly later join counts as "could not
                        // possibly have received it".
                        bool joinedLate = observer.SawFirstSyncFrame && observer.FirstSyncFrameNumber > publishFrame + 2;
                        if (joinedLate)
                        {
                            result.respawnMissingJoinedLate++;
                        }
                        else
                        {
                            result.respawnMissingLostUpdate++;
                            result.AddSample("player " + observer.PlayerID + " never received player " + owner.PlayerID
                                + "'s respawn position although it was connected when it was published");
                        }
                    }
                    else if (entity.revivePositionX != Sim.RevivePositionX(owner.PlayerID)
                        || entity.revivePositionY != Sim.RevivePositionY(owner.PlayerID)
                        || entity.revivePositionZ != Sim.RevivePositionZ(owner.PlayerID))
                    {
                        result.respawnMismatch++;
                        result.AddSample("player " + observer.PlayerID + " holds a wrong respawn position for player "
                            + owner.PlayerID);
                    }
                    else
                    {
                        result.respawnConverged++;
                    }
                }
            }
            return result;
        }

        internal static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
