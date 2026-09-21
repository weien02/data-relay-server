using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace StressTest
{
    internal enum Phase
    {
        Connecting,
        Spawning,
        Load,
        Quiesce,
        Finished,
    }

    /// <summary>
    /// How a client is taken off the network for a reconnection episode.
    ///
    /// The two are not interchangeable, and which one is valid depends on how long the client
    /// stays away. The server only clears Session.IsConnected from Session.Connect(), which is
    /// reached from ReconnectSession, which is reached only from the login handler. Heartbeats
    /// never revive a session. So once a blackout outlasts DisconnectThresholdSeconds the
    /// server stops broadcasting to that session, the client stops seeing SyncFrameBegin, and
    /// because a frame number gap is the only repair trigger it never asks for anything again.
    /// It goes permanently deaf and mute while the server believes nothing is wrong.
    /// </summary>
    internal enum KillMode
    {
        /// <summary>Close the socket and log in again with the same client id, as a restarted process would.</summary>
        Rejoin,
        /// <summary>Keep the socket, stop sending, discard everything that arrives. A wifi drop.</summary>
        Blackout,
    }

    /// <summary>
    /// One kill-and-return of one client, with a timestamp at every step so the intervals
    /// between them can be reported separately.
    ///
    /// The headline interval is resumed to viewRestored: what recovery itself costs, with the
    /// arbitrary choice of how long the client stayed away divided out. killed to viewRestored
    /// is what a player would actually feel.
    /// </summary>
    internal sealed class ReconnectEpisode
    {
        public int clientIndex;
        public byte playerID;
        public KillMode mode;
        public double awaySecondsRequested;
        /// <summary>True when the client stayed away long enough for the server to drop the session.</summary>
        public bool crossedServerThreshold;

        public double killedAtMs = -1;
        public double resumedAtMs = -1;
        public double loginResponseAtMs = -1;
        public double syncLostRequestAtMs = -1;
        public double syncLostResponseAtMs = -1;
        public double viewRestoredAtMs = -1;
        /// <summary>When some other client saw the server ask for a backup authority for this player.</summary>
        public double serverNoticedAtMs = -1;

        public int targetsTotal;
        public int targetsOutstandingAtEnd;
        /// <summary>The owners whose state never came back. Naming them is what makes a partial recovery diagnosable.</summary>
        public readonly List<byte> stillStalePlayers = new List<byte>();
        public bool loginSaidRecoverEntities;
        public bool playerIDChanged;

        /// <summary>Entity data packets and bytes taken to put the view back together.</summary>
        public long entityDataDuringRecovery;
        public long bytesDuringRecovery;

        public string outcome = "not started";

        public bool Recovered => this.viewRestoredAtMs >= 0;

        /// <summary>The headline figure: how long the return itself took.</summary>
        public double RecoveryMs => this.Recovered ? this.viewRestoredAtMs - this.resumedAtMs : -1;

        /// <summary>What the player experiences, including the time spent away.</summary>
        public double OutageMs => this.Recovered ? this.viewRestoredAtMs - this.killedAtMs : -1;

        /// <summary>The literal "one round trip" the report claims, for the snapshot handshake alone.</summary>
        public double SnapshotRoundTripMs =>
            this.syncLostRequestAtMs >= 0 && this.syncLostResponseAtMs >= this.syncLostRequestAtMs
                ? this.syncLostResponseAtMs - this.syncLostRequestAtMs
                : -1;
    }

    /// <summary>
    /// What one observer currently believes about one entity. There is one of these per
    /// (observer, entity) pair, and it is only ever touched by that observer's receive loop,
    /// so the whole harness runs without a lock on the hot path.
    ///
    /// Stores are tracked separately because the server syncs dirty data only: a single
    /// SyncEntityData packet carries just the stores that changed, so a view is assembled
    /// from several packets over time.
    /// </summary>
    internal sealed class ObservedEntity
    {
        public uint runtimeID;
        public byte authorityPlayerID;
        public byte authorityType;
        public uint creationSyncFrame;

        public bool hasTransform;
        public float positionX, positionY, positionZ;
        public float rotationX, rotationY, rotationZ;

        public bool hasJumpState;
        public float jumpPower;
        public bool isChargingJump;

        public bool hasRespawn;
        public float revivePositionX, revivePositionY, revivePositionZ;

        /// <summary>Highest sequence number this observer has ever decoded for the entity, or -1.</summary>
        public int lastSequence = -1;
        public long updateCount;
        public uint lastUpdateSyncFrame;
    }

    /// <summary>
    /// Everything shared between virtual clients. Written by one owner and read by many
    /// observers, so every field is either immutable after setup or accessed through Volatile.
    /// Indexed by player id, which the server allocates from a byte and never reuses.
    /// </summary>
    internal sealed class SharedState
    {
        public const int MaximumPlayerCount = 256;

        private volatile int _phase = (int)Phase.Connecting;
        public Phase Phase
        {
            get { return (Phase)this._phase; }
            set { this._phase = (int)value; }
        }

        /// <summary>The newest sequence number each owner has published. The ground truth.</summary>
        public readonly int[] publishedSequence = new int[MaximumPlayerCount];
        /// <summary>The runtime id of each owner's entity, or -1 if it has not been created yet.</summary>
        public readonly long[] ownedEntityRuntimeID = new long[MaximumPlayerCount];
        /// <summary>The sync frame in which each owner published its respawn data, the one write that is never repeated.</summary>
        public readonly uint[] respawnPublishSyncFrame = new uint[MaximumPlayerCount];
        public readonly bool[] playerIDInUse = new bool[MaximumPlayerCount];

        /// <summary>
        /// One monotonic clock for the whole run, so timestamps taken on different client
        /// threads can be subtracted from each other.
        /// </summary>
        public readonly Stopwatch clock = Stopwatch.StartNew();

        /// <summary>
        /// When any client saw the server ask for a backup authority for this player, in
        /// Stopwatch ticks, or 0 if it never did.
        ///
        /// This is the only way the harness can observe the moment the server itself decided a
        /// session was dead: the ask is broadcast to everyone else, so the victim's own silence
        /// is timed from outside. First writer wins, since the earliest observation is the one
        /// closest to when the server actually made the decision.
        /// </summary>
        public readonly long[] backupAskedAtTicks = new long[MaximumPlayerCount];

        public SharedState()
        {
            for (int i = 0; i < MaximumPlayerCount; i++)
            {
                this.ownedEntityRuntimeID[i] = -1;
            }
        }

        public int GetPublishedSequence(byte playerID)
        {
            return Volatile.Read(ref this.publishedSequence[playerID]);
        }

        public double Milliseconds => this.clock.Elapsed.TotalMilliseconds;

        public static double TicksToMilliseconds(long ticks)
        {
            return ticks * 1000.0 / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// A point-in-time copy of the aggregate counters, so a rate can be measured over the load
    /// window alone instead of over the whole process lifetime, which also covers connecting
    /// and spawning.
    /// </summary>
    internal sealed class LoadWindow
    {
        public double seconds;
        public long datagramsIn, datagramsOut, bytesIn, bytesOut;
        public long syncFrames, stateUpdates, entityData;
        /// <summary>Frames seen by the single client that saw the most, i.e. one present for the whole window.</summary>
        public long maxSyncFramesForOneClient;
        public int clientCount;

        public double PerSecond(long count)
        {
            return this.seconds > 0 ? count / this.seconds : 0;
        }
    }

    internal sealed class ClientStats
    {
        public const int LagBucketCount = 12;

        public long datagramsReceived, datagramsSent, bytesReceived, bytesSent;
        public long syncFrameBeginCount, syncFrameGapCount, syncFrameGapTotal;
        public long syncLostRequests, syncLostResponses, joinRepairs;
        public long entityCreationsReceived, entityDeletionsReceived, entityDataReceived;
        public long dataBeforeCreation, unknownDataStore, parseFailures;
        public long backupAuthorityRequests;
        public long stateUpdatesSent;
        /// <summary>Datagrams thrown away unparsed because the client was blacked out.</summary>
        public long blackoutDatagramsDropped;

        // Anomalies detected live, as samples arrive.
        public long corruptSamples, misattributedSamples, regressedSamples;

        // Staleness of each accepted sample, in the owner's sequence numbers. Bucket i holds
        // lags in [2^(i-1), 2^i); bucket 0 holds a lag of exactly 0 (perfectly fresh).
        public readonly long[] lagBuckets = new long[LagBucketCount];
        public long lagSamples, lagSum, lagMax;

        public void RecordLag(int lag)
        {
            if (lag < 0)
            {
                // The owner's counter is read without synchronisation against its send, so a
                // sample can momentarily look newer than the truth. Clamp rather than discard.
                lag = 0;
            }
            this.lagSamples++;
            this.lagSum += lag;
            if (lag > this.lagMax)
            {
                this.lagMax = lag;
            }
            // Bucket b holds [2^(b-1), 2^b), so bucket 1 is exactly one update behind.
            int bucket = 0;
            if (lag > 0)
            {
                bucket = 1;
                int upperExclusive = 2;
                while (lag >= upperExclusive && bucket < LagBucketCount - 1)
                {
                    upperExclusive <<= 1;
                    bucket++;
                }
            }
            this.lagBuckets[bucket]++;
        }
    }

    /// <summary>
    /// One simulated user: its own UDP socket, its own session, and one entity it holds
    /// Full_LocalPlayer authority over.
    ///
    /// It mirrors the real Unity client's cadence deliberately. State is published in response
    /// to the server's SyncFrameBegin, not on a timer of its own, because that is what
    /// GameClient.SyncFrameBeginPacketHandler does, and a heartbeat goes out on every sync frame
    /// because the server drops a session that stays silent for DisconnectThresholdSeconds.
    /// </summary>
    internal sealed class VirtualClient
    {
        private readonly Config _config;
        private readonly SharedState _shared;
        private readonly int _index;
        private readonly uint _clientID;

        private Socket _socket;
        private readonly ByteWriter _writer = new ByteWriter(Wire.MaximumDatagramLength);
        private readonly byte[] _receiveBuffer = new byte[Wire.MaximumDatagramLength];

        private int _connectionID = Wire.ConnectionID_Connecting;
        private ushort _sessionID;
        private byte _playerID;
        private bool _loggedIn;

        private uint _myEntityRuntimeID;
        private bool _hasMyEntity;
        private bool _publishedRespawn;
        private int _sequence;
        private int _syncFramesSincePublish;

        private uint _syncFrameNumber;
        private bool _sawFirstSyncFrame;
        private uint _firstSyncFrameNumber;
        // Recorded once for the life of the client and never again, so a rejoin does not move it.
        // The respawn check in Verify excuses a missing write-once value only for an observer that
        // genuinely arrived after it was published; a reconnected client was there at the time and
        // is supposed to get the value back in its recovery snapshot, so it stays held to that bar.
        private bool _firstSyncFrameEverRecorded;
        private bool _isSyncLost;
        private bool _loginSaidRecoverEntities;

        // ---- reconnection episodes --------------------------------------------------------
        private CancellationTokenSource _loopCancellation;
        private Task _loop;
        /// <summary>1 while blacked out. Read on the receive loop, written by the orchestrator.</summary>
        private int _blackout;
        /// <summary>The episode currently in its recovery window, or null.</summary>
        private ReconnectEpisode _activeEpisode;
        /// <summary>1 while a recovery window is open and targets are being ticked off.</summary>
        private int _recoveryActive;
        /// <summary>Per owner, the sequence this client must see again before its view counts as restored. -1 = not a target.</summary>
        private readonly int[] _recoveryRequiredSequence = new int[SharedState.MaximumPlayerCount];
        private int _recoveryOutstanding;
        private long _recoveryEntityDataAtStart;
        private long _recoveryBytesAtStart;
        /// <summary>Stopwatch ticks at which the view came back, or 0. Polled from another thread.</summary>
        private long _recoveryRestoredTicks;

        public readonly List<ReconnectEpisode> episodes = new List<ReconnectEpisode>();

        /// <summary>The receive loop currently running, so a replacement started by a rejoin can be awaited with the rest.</summary>
        public Task Loop => this._loop;

        /// <summary>
        /// This observer's view of every entity it knows about, keyed by runtime id. Only the
        /// receive loop touches it while the run is in progress; the verifier reads it after
        /// every loop has completed.
        /// </summary>
        public readonly Dictionary<uint, ObservedEntity> observed = new Dictionary<uint, ObservedEntity>();
        public readonly ClientStats stats = new ClientStats();

        // Mirrors observed.Count so the orchestrator can watch spawn progress without reading a
        // dictionary that is being mutated on another thread.
        private int _observedEntityCount;
        public int ObservedEntityCount => Volatile.Read(ref this._observedEntityCount);

        public int Index => this._index;
        public uint ClientID => this._clientID;
        public byte PlayerID => this._playerID;
        public ushort SessionID => this._sessionID;
        public bool LoggedIn => this._loggedIn;
        public bool HasEntity => this._hasMyEntity;
        public uint EntityRuntimeID => this._myEntityRuntimeID;
        public int Sequence => this._sequence;
        public uint FirstSyncFrameNumber => this._firstSyncFrameNumber;
        public bool SawFirstSyncFrame => this._sawFirstSyncFrame;
        public string FailureReason { get; private set; }

        public VirtualClient(Config config, SharedState shared, int index, uint clientID)
        {
            this._config = config;
            this._shared = shared;
            this._index = index;
            this._clientID = clientID;
        }

        /// <summary>
        /// Performs the connecting handshake and logs in. Returns once the server has allocated
        /// a session, so the caller can stagger joins and know when every client is really in.
        /// </summary>
        public async Task<bool> ConnectAndLoginAsync(CancellationToken cancellationToken)
        {
            IPEndPoint serverEndPoint = new IPEndPoint(this._config.serverAddress, this._config.serverPort);
            this._socket = new Socket(serverEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            this._socket.ReceiveBufferSize = this._config.socketReceiveBufferBytes;
            this._socket.SendBufferSize = 1 << 18;
            // Connecting a UDP socket is safe here: the server always answers from its listening
            // port, and it lets the receive loop use the cheaper Receive overloads.
            this._socket.Connect(serverEndPoint);

            if (!await this.HandshakeAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }
            return await this.LoginAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Starts the receive loop under a cancellation source of this client's own, linked to
        /// the run's. A reconnection episode has to stop one client's loop without stopping
        /// everyone else's, which the shared token cannot do.
        /// </summary>
        public Task StartLoop(CancellationToken runCancellationToken)
        {
            this._loopCancellation = CancellationTokenSource.CreateLinkedTokenSource(runCancellationToken);
            this._loop = Task.Run(() => this.RunAsync(this._loopCancellation.Token));
            return this._loop;
        }

        /// <summary>
        /// Cancels this client's receive loop, waits for it to unwind, and closes the socket.
        /// Waiting matters: everything that follows touches state the loop owns.
        /// </summary>
        private async Task StopLoopAndCloseAsync()
        {
            try
            {
                this._loopCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            Task loop = this._loop;
            if (loop != null)
            {
                try
                {
                    await loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            this._loop = null;
            this.Close();
        }

        private async Task<bool> HandshakeAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < this._config.handshakeAttempts; attempt++)
            {
                this.Send(this._writer.BeginHandshake());
                int length = await this.ReceiveWithTimeoutAsync(this._config.handshakeTimeoutMilliseconds, cancellationToken).ConfigureAwait(false);
                if (this.TryReadHandshakeResponse(length))
                {
                    return true;
                }
            }
            this.FailureReason = "no response to the connecting handshake";
            return false;
        }

        // Split out of HandshakeAsync because a ref struct cannot live across an await.
        private bool TryReadHandshakeResponse(int length)
        {
            if (length < sizeof(int) + sizeof(int))
            {
                return false;
            }
            ByteReader reader = new ByteReader(new ReadOnlySpan<byte>(this._receiveBuffer, 0, length));
            int connectionID = reader.ReadInt32();
            if (connectionID != Wire.ConnectionID_Connecting)
            {
                return false;
            }
            this._connectionID = reader.ReadInt32();
            return true;
        }

        private async Task<bool> LoginAsync(CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < this._config.handshakeAttempts; attempt++)
            {
                ByteWriter writer = this._writer.Begin(this._connectionID, 0, C2S.LoginRequest);
                writer.WriteUInt32(this._clientID);
                this.Send(writer);

                // The login response is queued and flushed at the end of a server tick, so a
                // sync frame packet can overtake it. Keep reading until it shows up.
                long deadline = Environment.TickCount64 + this._config.handshakeTimeoutMilliseconds;
                while (Environment.TickCount64 < deadline)
                {
                    int remaining = (int)(deadline - Environment.TickCount64);
                    int length = await this.ReceiveWithTimeoutAsync(remaining, cancellationToken).ConfigureAwait(false);
                    if (length <= 0)
                    {
                        break;
                    }
                    if (this.TryReadLoginResponse(length))
                    {
                        return true;
                    }
                }
            }
            this.FailureReason = "no login response";
            return false;
        }

        // Split out of LoginAsync because a ref struct cannot live across an await.
        private bool TryReadLoginResponse(int length)
        {
            if (length < sizeof(int) + sizeof(ushort))
            {
                return false;
            }
            ByteReader reader = new ByteReader(new ReadOnlySpan<byte>(this._receiveBuffer, 0, length));
            reader.ReadInt32();
            S2C packetType = (S2C)reader.ReadUInt16();
            if (packetType != S2C.LoginResponse)
            {
                // A sync frame packet can overtake the login response, because the response is
                // queued and only flushed at the end of the server's tick. Handle it and wait.
                this.HandlePacket(packetType, ref reader);
                return false;
            }
            this._sessionID = reader.ReadUInt16();
            this._playerID = reader.ReadByte();
            // False for a fresh client id. On a rejoin it is the server confirming it matched the
            // client id to an existing session; if it comes back false there, the server failed to
            // recognise the reconnect and has issued a brand new player instead.
            this._loginSaidRecoverEntities = reader.ReadBoolean();
            this._loggedIn = true;
            Volatile.Write(ref this._shared.playerIDInUse[this._playerID], true);
            return true;
        }

        /// <summary>
        /// Takes this client off the network and opens an episode. In blackout mode the socket
        /// stays open and the receive loop keeps draining it, discarding everything, so the OS
        /// buffer does not hold a backlog that would be replayed as a burst of stale packets on
        /// return. In rejoin mode the loop is stopped and the socket closed outright.
        /// </summary>
        public async Task<ReconnectEpisode> KillAsync(KillMode mode, double awaySeconds, double serverThresholdSeconds)
        {
            ReconnectEpisode episode = new ReconnectEpisode();
            episode.clientIndex = this._index;
            episode.playerID = this._playerID;
            episode.mode = mode;
            episode.awaySecondsRequested = awaySeconds;
            episode.crossedServerThreshold = awaySeconds >= serverThresholdSeconds;
            episode.killedAtMs = this._shared.Milliseconds;
            episode.outcome = "away";
            this.episodes.Add(episode);
            this._activeEpisode = episode;

            if (mode == KillMode.Blackout)
            {
                Volatile.Write(ref this._blackout, 1);
            }
            else
            {
                await this.StopLoopAndCloseAsync().ConfigureAwait(false);
            }
            return episode;
        }

        /// <summary>
        /// Brings the client back and starts the recovery window. Returns false if it could not
        /// get back in at all, with the reason recorded on the episode.
        ///
        /// The recovery targets are fixed here rather than chased as they move: every other
        /// player's view is required to reach the sequence that player had already published at
        /// this instant. Owners keep publishing during recovery, so a moving target would never
        /// be met on a busy server and the measurement would never terminate.
        /// </summary>
        public async Task<bool> ResumeAsync(List<VirtualClient> liveClients, CancellationToken cancellationToken)
        {
            ReconnectEpisode episode = this._activeEpisode;
            if (episode == null)
            {
                return false;
            }
            byte previousPlayerID = this._playerID;
            episode.resumedAtMs = this._shared.Milliseconds;

            if (episode.mode == KillMode.Rejoin)
            {
                this.ResetForRejoin();
                this.ArmRecoveryWatch(episode, liveClients);
                bool loggedIn = await this.ConnectAndLoginAsync(cancellationToken).ConfigureAwait(false);
                if (!loggedIn)
                {
                    episode.outcome = "could not log in again: " + (this.FailureReason ?? "unknown");
                    this.DisarmRecoveryWatch(episode);
                    return false;
                }
                episode.loginResponseAtMs = this._shared.Milliseconds;
                episode.loginSaidRecoverEntities = this._loginSaidRecoverEntities;
                episode.playerIDChanged = this._playerID != previousPlayerID;
                if (episode.playerIDChanged)
                {
                    // The server did not match the client id to the old session and handed out a
                    // new player. Nothing downstream is comparable after that.
                    episode.outcome = "server issued a new player id (" + previousPlayerID + " to " + this._playerID + ")";
                    this.DisarmRecoveryWatch(episode);
                    return false;
                }
                // Not awaited on purpose: the loop runs for the rest of the client's life. The
                // caller picks the task up from the Loop property and registers it for the
                // final wait.
                _ = this.StartLoop(cancellationToken);
                // What the Unity client does on a login that reports recoverable entities: take
                // the avatar back before anything else. Without it the entity stays parked
                // mid-transfer and this client never regains the right to publish.
                if (this._loginSaidRecoverEntities)
                {
                    this.SendAcquireLocalPlayerEntityAuthority();
                }
            }
            else
            {
                this.ArmRecoveryWatch(episode, liveClients);
                // Published after the targets, so the loop cannot start ticking them off against
                // a half-built target set.
                Volatile.Write(ref this._blackout, 0);
            }

            episode.outcome = "recovering";
            return true;
        }

        /// <summary>
        /// Clears everything a restarted process would lose, and keeps everything it would not.
        ///
        /// The sequence counter is deliberately kept: it is this client's published history, and
        /// restarting it at zero would make every other observer see the owner's state go
        /// backwards, which the verifier would correctly report as an out-of-order update.
        /// </summary>
        private void ResetForRejoin()
        {
            this.observed.Clear();
            Volatile.Write(ref this._observedEntityCount, 0);
            this._syncFrameNumber = 0;
            this._sawFirstSyncFrame = false;
            this._isSyncLost = false;
            this._hasMyEntity = false;
            this._myEntityRuntimeID = 0;
            this._connectionID = Wire.ConnectionID_Connecting;
            this._syncFramesSincePublish = 0;
            this._loggedIn = false;
        }

        private void ArmRecoveryWatch(ReconnectEpisode episode, List<VirtualClient> liveClients)
        {
            int outstanding = 0;
            for (int i = 0; i < SharedState.MaximumPlayerCount; i++)
            {
                this._recoveryRequiredSequence[i] = -1;
            }
            foreach (VirtualClient other in liveClients)
            {
                if (ReferenceEquals(other, this) || !other.HasEntity)
                {
                    continue;
                }
                int required = this._shared.GetPublishedSequence(other.PlayerID);
                if (required < Sim.FirstSequence)
                {
                    // That owner has never published anything, so there is nothing to restore
                    // and no sample would ever satisfy the target.
                    continue;
                }
                this._recoveryRequiredSequence[other.PlayerID] = required;
                outstanding++;
            }
            episode.targetsTotal = outstanding;
            this._recoveryOutstanding = outstanding;
            this._recoveryEntityDataAtStart = this.stats.entityDataReceived;
            this._recoveryBytesAtStart = this.stats.bytesReceived;
            Volatile.Write(ref this._recoveryRestoredTicks, 0);
            if (outstanding == 0)
            {
                // Nothing to wait for. Count it restored immediately rather than timing out.
                Volatile.Write(ref this._recoveryRestoredTicks, this._shared.clock.ElapsedTicks);
                Volatile.Write(ref this._recoveryActive, 0);
                return;
            }
            Volatile.Write(ref this._recoveryActive, 1);
        }

        private void DisarmRecoveryWatch(ReconnectEpisode episode)
        {
            Volatile.Write(ref this._recoveryActive, 0);
            episode.targetsOutstandingAtEnd = this._recoveryOutstanding;
            episode.stillStalePlayers.Clear();
            for (int i = 0; i < SharedState.MaximumPlayerCount; i++)
            {
                if (this._recoveryRequiredSequence[i] >= 0)
                {
                    episode.stillStalePlayers.Add((byte)i);
                }
            }
        }

        /// <summary>
        /// Waits for the view to come back, or gives up. Returns the closed episode.
        ///
        /// A timeout is not a formality. If the server's SyncLostResponseBegin is lost, the
        /// client leaves _isSyncLost set and ignores every frame from then on, with no retry
        /// anywhere, so it never recovers at all. That outcome has to be reported as its own
        /// bucket rather than averaged away or waited on forever.
        /// </summary>
        public async Task<ReconnectEpisode> AwaitRecoveryAsync(double timeoutSeconds, CancellationToken cancellationToken)
        {
            ReconnectEpisode episode = this._activeEpisode;
            if (episode == null)
            {
                return null;
            }
            double deadline = this._shared.Milliseconds + timeoutSeconds * 1000.0;
            while (this._shared.Milliseconds < deadline && !cancellationToken.IsCancellationRequested)
            {
                long restoredTicks = Volatile.Read(ref this._recoveryRestoredTicks);
                if (restoredTicks != 0)
                {
                    episode.viewRestoredAtMs = SharedState.TicksToMilliseconds(restoredTicks);
                    episode.outcome = "recovered";
                    break;
                }
                try
                {
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            this.DisarmRecoveryWatch(episode);
            episode.entityDataDuringRecovery = this.stats.entityDataReceived - this._recoveryEntityDataAtStart;
            episode.bytesDuringRecovery = this.stats.bytesReceived - this._recoveryBytesAtStart;

            if (!episode.Recovered && episode.outcome == "recovering")
            {
                bool sawNothingBack = episode.syncLostResponseAtMs < 0;
                if (episode.mode == KillMode.Blackout && episode.crossedServerThreshold)
                {
                    // Expected, and a finding rather than a harness fault: the server dropped the
                    // session while the client was quiet, and only a fresh login can revive it.
                    episode.outcome = "never recovered: blacked out past the server's disconnect threshold, so the session was dropped and nothing can revive it but a new login";
                }
                else if (sawNothingBack)
                {
                    episode.outcome = "never recovered: asked for a snapshot and got no answer (the client stays sync-lost forever, with no retry)";
                }
                else
                {
                    episode.outcome = "timed out with " + this._recoveryOutstanding + " of " + episode.targetsTotal + " players still stale";
                }
            }

            long backupTicks = Volatile.Read(ref this._shared.backupAskedAtTicks[episode.playerID]);
            if (backupTicks != 0)
            {
                episode.serverNoticedAtMs = SharedState.TicksToMilliseconds(backupTicks);
            }

            this._activeEpisode = null;
            return episode;
        }

        private void SendAcquireLocalPlayerEntityAuthority()
        {
            this.Send(this._writer.Begin(this._connectionID, this._sessionID, C2S.AcquireLocalPlayerEntityAuthorityRequest));
        }

        /// <summary>
        /// Asks the server to create this client's avatar. The request is fire and forget; the
        /// entity is confirmed when the resulting SyncEntityCreation broadcast comes back.
        /// </summary>
        public void RequestSpawn()
        {
            ByteWriter writer = this._writer.Begin(this._connectionID, this._sessionID, C2S.CreateEntityWithAuthorityRequest);
            writer.WriteInt32(this._config.registryID);
            writer.WriteByte((byte)AuthorityType.Full_LocalPlayer);
            // The Unity client sends the registry id as its static custom data, so the same four
            // bytes go on the wire here and the packet sizes match a real client's.
            writer.WriteInt32(sizeof(int));
            writer.WriteInt32(this._config.registryID);
            this.Send(writer);
        }

        /// <summary>
        /// The receive loop. Everything after login happens here, driven entirely by what the
        /// server sends, which is what makes the timing representative of a real client.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int length;
                    try
                    {
                        length = await this._socket
                            .ReceiveAsync(new Memory<byte>(this._receiveBuffer), SocketFlags.None, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        // An ICMP port-unreachable surfaces here on Windows when the server has
                        // gone away. Keep going; the run summary will show the silence.
                        continue;
                    }
                    this.ProcessDatagram(length);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        // Split out of RunAsync because a ref struct cannot live across an await.
        private void ProcessDatagram(int length)
        {
            if (length < sizeof(int) + sizeof(ushort))
            {
                return;
            }
            if (Volatile.Read(ref this._blackout) != 0)
            {
                // Drained and discarded, not left in the socket buffer. A real client that has
                // lost its link never receives these at all, so holding them and replaying the
                // backlog on return would invent a burst the network would never deliver.
                this.stats.blackoutDatagramsDropped++;
                return;
            }
            this.stats.datagramsReceived++;
            this.stats.bytesReceived += length;

            ByteReader reader = new ByteReader(new ReadOnlySpan<byte>(this._receiveBuffer, 0, length));
            reader.ReadInt32();     // connection id
            S2C packetType = (S2C)reader.ReadUInt16();
            try
            {
                this.HandlePacket(packetType, ref reader);
            }
            catch (ArgumentOutOfRangeException)
            {
                this.stats.parseFailures++;
            }
            catch (IndexOutOfRangeException)
            {
                this.stats.parseFailures++;
            }
        }

        private void HandlePacket(S2C packetType, ref ByteReader reader)
        {
            switch (packetType)
            {
                case S2C.SyncFrameBegin:
                    this.HandleSyncFrameBegin(ref reader);
                    break;
                case S2C.SyncLostResponseBegin:
                    this.HandleSyncLostResponseBegin(ref reader);
                    break;
                case S2C.SyncEntityCreation:
                    this.HandleSyncEntityCreation(ref reader);
                    break;
                case S2C.SyncEntityDeletion:
                    this.HandleSyncEntityDeletion(ref reader);
                    break;
                case S2C.SyncEntityData:
                    this.HandleSyncEntityData(ref reader);
                    break;
                case S2C.AskForBackupEntityAuthority:
                    this.HandleAskForBackupEntityAuthority(ref reader);
                    break;
                case S2C.LoginResponse:
                    // A duplicate of a login already handled during setup.
                    break;
                default:
                    break;
            }
        }

        private void HandleSyncFrameBegin(ref ByteReader reader)
        {
            uint syncFrameNumber = reader.ReadUInt32();
            this.stats.syncFrameBeginCount++;

            // The heartbeat goes out first, exactly as ClientSideFrameSynchronizationController
            // does. Going quiet for DisconnectThresholdSeconds gets the session dropped,
            // which would show up as packet loss that the server did not actually cause.
            this.SendHeartbeat();

            bool isFirstFrame = !this._sawFirstSyncFrame;
            if (isFirstFrame)
            {
                this._sawFirstSyncFrame = true;
                if (!this._firstSyncFrameEverRecorded)
                {
                    this._firstSyncFrameEverRecorded = true;
                    this._firstSyncFrameNumber = syncFrameNumber;
                }
            }

            // Deliberately no special case for the first frame. The real client's frame counter
            // starts at zero, so joining a server that is already at frame N reads as a gap and
            // pulls a full snapshot. That self-repair on join is a real and load-bearing part of
            // the framework's behaviour, and baselining on the first frame seen would hide it.
            if (!this._isSyncLost)
            {
                if (syncFrameNumber - this._syncFrameNumber > 1)
                {
                    // A gap in frame numbers is the only trigger the framework has for asking
                    // the server to resend state. Nothing detects a lost data packet on its own.
                    if (isFirstFrame)
                    {
                        // Joining a server that is already running, not a gap in the stream.
                        this.stats.joinRepairs++;
                    }
                    else
                    {
                        this.stats.syncFrameGapCount++;
                        this.stats.syncFrameGapTotal += syncFrameNumber - this._syncFrameNumber;
                    }
                    this._isSyncLost = true;
                    this.stats.syncLostRequests++;
                    ByteWriter writer = this._writer.Begin(this._connectionID, this._sessionID, C2S.SyncLostRequest);
                    writer.WriteUInt32(this._syncFrameNumber);
                    this.Send(writer);
                    ReconnectEpisode recovering = this._activeEpisode;
                    if (recovering != null && recovering.syncLostRequestAtMs < 0)
                    {
                        recovering.syncLostRequestAtMs = this._shared.Milliseconds;
                    }
                }
                else
                {
                    this._syncFrameNumber = syncFrameNumber;
                }
            }

            if (this._shared.Phase == Phase.Load && this._hasMyEntity)
            {
                this._syncFramesSincePublish++;
                if (this._syncFramesSincePublish >= this._config.publishEverySyncFrames)
                {
                    this._syncFramesSincePublish = 0;
                    this.PublishState();
                }
            }
        }

        private void HandleSyncLostResponseBegin(ref ByteReader reader)
        {
            uint lastSyncFrameNumber = reader.ReadUInt32();
            this._syncFrameNumber = lastSyncFrameNumber;
            this._isSyncLost = false;
            this.stats.syncLostResponses++;
            ReconnectEpisode recovering = this._activeEpisode;
            if (recovering != null && recovering.syncLostResponseAtMs < 0)
            {
                recovering.syncLostResponseAtMs = this._shared.Milliseconds;
            }
        }

        private void HandleSyncEntityCreation(ref ByteReader reader)
        {
            uint syncFrameNumber = reader.ReadUInt32();
            uint runtimeID = reader.ReadUInt32();
            reader.ReadInt32();                             // registry id
            byte authorityPlayerID = reader.ReadByte();
            byte authorityType = reader.ReadByte();
            int staticCustomDataLength = reader.ReadInt32();
            reader.Skip(staticCustomDataLength);
            this.stats.entityCreationsReceived++;

            ObservedEntity entity;
            if (!this.observed.TryGetValue(runtimeID, out entity))
            {
                entity = new ObservedEntity();
                entity.runtimeID = runtimeID;
                entity.creationSyncFrame = syncFrameNumber;
                this.observed.Add(runtimeID, entity);
                Volatile.Write(ref this._observedEntityCount, this.observed.Count);
            }
            entity.authorityPlayerID = authorityPlayerID;
            entity.authorityType = authorityType;

            if (authorityPlayerID == this._playerID && !this._hasMyEntity)
            {
                this._myEntityRuntimeID = runtimeID;
                this._hasMyEntity = true;
                Volatile.Write(ref this._shared.ownedEntityRuntimeID[this._playerID], runtimeID);
            }
        }

        private void HandleSyncEntityDeletion(ref ByteReader reader)
        {
            reader.ReadUInt32();                            // sync frame number
            uint runtimeID = reader.ReadUInt32();
            this.stats.entityDeletionsReceived++;
            if (this.observed.Remove(runtimeID))
            {
                Volatile.Write(ref this._observedEntityCount, this.observed.Count);
            }
        }

        private void HandleSyncEntityData(ref ByteReader reader)
        {
            uint syncFrameNumber = reader.ReadUInt32();
            uint runtimeID = reader.ReadUInt32();
            byte authorityPlayerID = reader.ReadByte();
            byte authorityType = reader.ReadByte();
            this.stats.entityDataReceived++;

            ObservedEntity entity;
            if (!this.observed.TryGetValue(runtimeID, out entity))
            {
                // The real client drops this too. It happens when a data packet overtakes the
                // creation packet for the same entity.
                this.stats.dataBeforeCreation++;
                return;
            }
            entity.authorityPlayerID = authorityPlayerID;
            entity.authorityType = authorityType;

            ushort dataStoreCount = reader.ReadUInt16();
            bool touchedTransform = false;
            bool touchedJumpState = false;
            for (int i = 0; i < dataStoreCount; i++)
            {
                ushort dataStoreIndex = reader.ReadUInt16();
                byte valueCount = reader.ReadByte();
                switch (dataStoreIndex)
                {
                    case DataStoreIndex.Transform:
                        this.ReadTransformData(ref reader, entity, valueCount);
                        touchedTransform = true;
                        break;
                    case DataStoreIndex.AvatarJumpState:
                        this.ReadJumpStateData(ref reader, entity, valueCount);
                        touchedJumpState = true;
                        break;
                    case DataStoreIndex.AvatarRespawn:
                        this.ReadRespawnData(ref reader, entity, valueCount);
                        break;
                    default:
                        // Value widths are not self describing, so an unknown store means the
                        // rest of the packet cannot be walked safely.
                        this.stats.unknownDataStore++;
                        return;
                }
            }

            entity.updateCount++;
            entity.lastUpdateSyncFrame = syncFrameNumber;

            if (entity.runtimeID == this._myEntityRuntimeID)
            {
                // The real client ignores server data for entities it has authority over.
                return;
            }
            if (touchedTransform || touchedJumpState)
            {
                this.VerifySample(entity);
            }
        }

        private void ReadTransformData(ref ByteReader reader, ObservedEntity entity, byte valueCount)
        {
            for (int i = 0; i < valueCount; i++)
            {
                byte valueIndex = reader.ReadByte();
                float value = reader.ReadSingle();
                switch (valueIndex)
                {
                    case 0: entity.positionX = value; break;
                    case 1: entity.positionY = value; break;
                    case 2: entity.positionZ = value; break;
                    case 3: entity.rotationX = value; break;
                    case 4: entity.rotationY = value; break;
                    case 5: entity.rotationZ = value; break;
                    default: break;     // scale, which this harness never writes
                }
            }
            entity.hasTransform = true;
        }

        private void ReadJumpStateData(ref ByteReader reader, ObservedEntity entity, byte valueCount)
        {
            for (int i = 0; i < valueCount; i++)
            {
                byte valueIndex = reader.ReadByte();
                if (valueIndex == 0)
                {
                    entity.jumpPower = reader.ReadSingle();
                }
                else
                {
                    entity.isChargingJump = reader.ReadBoolean();
                }
            }
            entity.hasJumpState = true;
        }

        private void ReadRespawnData(ref ByteReader reader, ObservedEntity entity, byte valueCount)
        {
            for (int i = 0; i < valueCount; i++)
            {
                byte valueIndex = reader.ReadByte();
                float value = reader.ReadSingle();
                switch (valueIndex)
                {
                    case 0: entity.revivePositionX = value; break;
                    case 1: entity.revivePositionY = value; break;
                    default: entity.revivePositionZ = value; break;
                }
            }
            entity.hasRespawn = true;
        }

        /// <summary>
        /// Checks a sample the moment it arrives, while the owner is still moving. Only the
        /// checks that hold at any instant live here; whether a view has converged can only be
        /// judged once the load has stopped, and that is done in the final sweep.
        /// </summary>
        private void VerifySample(ObservedEntity entity)
        {
            if (!entity.hasTransform || !entity.hasJumpState)
            {
                return;
            }
            int sequence = (int)entity.jumpPower;
            if (sequence < Sim.FirstSequence)
            {
                // The server side entity still holds its freshly created zeroes.
                return;
            }
            byte owner = entity.authorityPlayerID;

            if (entity.positionX != Sim.PositionX(owner, sequence)
                || entity.positionY != Sim.PositionY(owner, sequence)
                || entity.positionZ != Sim.PositionZ(owner, sequence)
                || entity.rotationY != Sim.RotationY(sequence)
                || entity.isChargingJump != Sim.IsChargingJump(sequence))
            {
                // The state is not a state its owner ever held. Either two stores from
                // different updates have been mixed, or the payload has been corrupted.
                int claimedOwner = Sim.OwnerFromPositionX(entity.positionX, sequence);
                if (claimedOwner != owner && claimedOwner >= 0 && claimedOwner < SharedState.MaximumPlayerCount)
                {
                    this.stats.misattributedSamples++;
                }
                else
                {
                    this.stats.corruptSamples++;
                }
            }

            if (sequence < entity.lastSequence)
            {
                this.stats.regressedSamples++;
            }
            else
            {
                entity.lastSequence = sequence;
            }

            this.stats.RecordLag(this._shared.GetPublishedSequence(owner) - sequence);
            this.NoteRecoveryProgress(owner, sequence);
        }

        /// <summary>
        /// Ticks one owner off the recovery target list. Called only from the receive loop, which
        /// is the sole owner of the target array, so the counting needs no synchronisation; only
        /// the finished timestamp is published for the waiting orchestrator to read.
        /// </summary>
        private void NoteRecoveryProgress(byte owner, int sequence)
        {
            if (Volatile.Read(ref this._recoveryActive) == 0)
            {
                return;
            }
            int required = this._recoveryRequiredSequence[owner];
            if (required < 0 || sequence < required)
            {
                return;
            }
            this._recoveryRequiredSequence[owner] = -1;
            this._recoveryOutstanding--;
            if (this._recoveryOutstanding <= 0)
            {
                Volatile.Write(ref this._recoveryActive, 0);
                Volatile.Write(ref this._recoveryRestoredTicks, this._shared.clock.ElapsedTicks);
            }
        }

        private void HandleAskForBackupEntityAuthority(ref ByteReader reader)
        {
            reader.ReadUInt32();        // runtime id
            byte authorityBeingReplaced = reader.ReadByte();
            // The one moment the harness can observe from outside: the server has just decided
            // that player's session is dead. First observation wins, since it is the closest to
            // when the decision was actually made.
            Interlocked.CompareExchange(
                ref this._shared.backupAskedAtTicks[authorityBeingReplaced],
                this._shared.clock.ElapsedTicks,
                0);
            // Deliberately not answered. Taking over another player's entity would move its
            // authority to this client and invalidate the ownership model the verifier relies
            // on. The count is reported instead, because it is a direct signal that the server
            // considered some session dead during the run.
            this.stats.backupAuthorityRequests++;
        }

        /// <summary>
        /// Publishes the next state. Transform and jump state always change together, so they
        /// are always dirty together and always travel in the same packet. That is what makes a
        /// view holding a transform and a jump power from different updates a real fault rather
        /// than an artefact of dirty-only sync.
        /// </summary>
        private void PublishState()
        {
            int sequence = this._sequence + 1;
            this._sequence = sequence;

            ByteWriter writer = this._writer.Begin(this._connectionID, this._sessionID, C2S.SyncEntityData);
            writer.WriteUInt32(this._myEntityRuntimeID);

            bool publishRespawn = !this._publishedRespawn;
            writer.WriteUInt16((ushort)(publishRespawn ? 3 : 2));

            writer.WriteUInt16(DataStoreIndex.Transform);
            writer.WriteByte(4);
            writer.WriteNetworkSingle(0, Sim.PositionX(this._playerID, sequence));
            writer.WriteNetworkSingle(1, Sim.PositionY(this._playerID, sequence));
            writer.WriteNetworkSingle(2, Sim.PositionZ(this._playerID, sequence));
            writer.WriteNetworkSingle(4, Sim.RotationY(sequence));

            writer.WriteUInt16(DataStoreIndex.AvatarJumpState);
            writer.WriteByte(2);
            writer.WriteNetworkSingle(0, Sim.JumpPower(sequence));
            writer.WriteNetworkBoolean(1, Sim.IsChargingJump(sequence));

            if (publishRespawn)
            {
                writer.WriteUInt16(DataStoreIndex.AvatarRespawn);
                writer.WriteByte(3);
                writer.WriteNetworkSingle(0, Sim.RevivePositionX(this._playerID));
                writer.WriteNetworkSingle(1, Sim.RevivePositionY(this._playerID));
                writer.WriteNetworkSingle(2, Sim.RevivePositionZ(this._playerID));
                this._publishedRespawn = true;
                Volatile.Write(ref this._shared.respawnPublishSyncFrame[this._playerID], this._syncFrameNumber);
            }

            this.Send(writer);
            this.stats.stateUpdatesSent++;
            // Published last, so an observer can never see a sequence the truth does not
            // already account for. This only ever makes the reported lag conservative.
            Volatile.Write(ref this._shared.publishedSequence[this._playerID], sequence);
        }

        private void SendHeartbeat()
        {
            this.Send(this._writer.Begin(this._connectionID, this._sessionID, C2S.ClientFrameSyncHeartbeat));
        }

        private void Send(ByteWriter writer)
        {
            if (Volatile.Read(ref this._blackout) != 0)
            {
                // Silence in both directions. The heartbeat stopping is what eventually makes the
                // server declare the session dead.
                return;
            }
            try
            {
                this._socket.Send(writer.Buffer, 0, writer.Length, SocketFlags.None);
                this.stats.datagramsSent++;
                this.stats.bytesSent += writer.Length;
            }
            catch (SocketException)
            {
                // A full send buffer or an ICMP error. The gap it leaves is what the report is
                // measuring, so it is swallowed here rather than tearing the client down.
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task<int> ReceiveWithTimeoutAsync(int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            if (timeoutMilliseconds <= 0)
            {
                return 0;
            }
            using (CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(timeoutMilliseconds);
                try
                {
                    return await this._socket
                        .ReceiveAsync(new Memory<byte>(this._receiveBuffer), SocketFlags.None, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return 0;
                }
                catch (SocketException)
                {
                    return 0;
                }
            }
        }

        public void Close()
        {
            try
            {
                this._socket?.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
