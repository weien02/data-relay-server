using DedicatedServer.Framework.Networking;
using DedicatedServer.Framework.Networking.Connection;
using System;
using System.Collections.Generic;
using System.Net;

namespace DedicatedServer.Framework.Client
{
    using SessionID = UInt16;
    using PlayerID = Byte;
    public class GameClient : ITickable, IDisposable
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<GameClient>();
        private GameNetworkingConnectorBase _connector;
        private GameNetworkingConnectionBase _connection;
        private readonly uint _clientID;
        private SessionID _sessionID;
        private byte _playerID;
        public byte PlayerID => this._playerID;
        private Action _onConnectedToServer;
        public Action OnConnectedToServer { set { this._onConnectedToServer = value; } }
        public delegate void OnLoggedInDelegate(bool shouldRecoverLocalPlayerEntities);
        private OnLoggedInDelegate _onLoggedIn;
        public OnLoggedInDelegate OnLoggedIn { set { this._onLoggedIn = value; } }
        private readonly Queue<ReceivedPacket> _receiveQueue;
        private readonly Dictionary<ServerToClientPacketTypes, Action<ReceivedPacket>> _receivedPacketHandlers;
        private readonly ClientSideEntityManager _clientSideEntityManager;
        public ClientSideEntityManager ClientSideEntityManager => this._clientSideEntityManager;
        private readonly ClientSideFrameSynchronizationController _clientSideFrameSynchronizationController;

        public GameClient(uint clientID)
        {
            this._clientID = clientID;
            this._clientSideEntityManager = new ClientSideEntityManager(this);
            this._clientSideFrameSynchronizationController = new ClientSideFrameSynchronizationController(this);
            this._receiveQueue = new Queue<ReceivedPacket>();
            this._receivedPacketHandlers = new Dictionary<ServerToClientPacketTypes, Action<ReceivedPacket>>();
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.LoginResponse, this.LoginResponsePacketHandler);
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.SyncFrameBegin, this.SyncFrameBeginPacketHandler);
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.SyncLostResponseBegin, this.SyncLostResponseBeginPacketHandler);
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.SyncEntityCreation, this.SyncEntityCreationPacketHandler);
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.SyncEntityDeletion, this.SyncEntityDeletionPacketHandler);
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.SyncEntityData, this.SyncEntityDataPacketHandler);
            this._receivedPacketHandlers.Add(ServerToClientPacketTypes.AskForBackupEntityAuthority, this.AskForBackupEntityAuthorityPacketHandler);
        }

        public void Connect(string ipAddress, ushort port)
        {
            if(!(this._connector is null))
            {
                this._connector.Dispose();
            }
            this._connector = new UdpConnector();
            this._connector.OnConnected = this.OnConnectedToServer_Internal;
            this._connector.Connect(new IPEndPoint(IPAddress.Parse(ipAddress), port));
        }

        public void Login()
        {
            using(SentPacket packet = this.GetSentPacket(ClientToServerPacketTypes.LoginRequest))
            {
                packet.WriteUInt32(this._clientID);
                this.SendPacketToServer(packet);
            }
            _logger.Log("Start logging in");
        }

        public void Tick(float deltaTime)
        {
            this._connector?.Tick(deltaTime);
            if (!(this._connection is null))
            {
                ReceivedPacket packet = this._connection.GetNextReceivedPacket();
                while (!(packet is null))
                {
                    ServerToClientPacketTypes packetType = (ServerToClientPacketTypes)packet.ReadUInt16();
                    if (packetType == ServerToClientPacketTypes.CustomPacket)
                    {
                        this._receiveQueue.Enqueue(packet);
                    }
                    else
                    {
                        using (packet)
                        {
                            Action<ReceivedPacket> packetHandler;
                            if (!this._receivedPacketHandlers.TryGetValue(packetType, out packetHandler))
                            {
                                throw new Exception("No packet handler for packet type " + packetType);
                            }
                            packetHandler(packet);
                        }
                    }
                    packet = this._connection.GetNextReceivedPacket();
                }
                this._connection.SendAllPacketsInQueue();
            }
        }

        public ReceivedPacket GetNextReceivedPacket()
        {
            ReceivedPacket packet;
            if(!this._receiveQueue.TryDequeue(out packet))
            {
                return null;
            }
            return packet;
        }

        public bool IsSelf(PlayerID playerID)
        {
            return playerID == this.PlayerID;
        }

        public void SendPacketToServer(SentPacket packet)
        {
            this._connection.QueuedSendPacket(packet);
            packet.Dispose(1);
        }

        private void OnConnectedToServer_Internal(GameNetworkingConnectionBase connection)
        {
            this._connection = connection;
            if (!(this._onConnectedToServer is null))
            {
                this._onConnectedToServer();
            }
        }

        public void Dispose()
        {
            this._connector?.Dispose();
        }

        internal SentPacket GetSentPacket(ClientToServerPacketTypes packetType)
        {
            SentPacket packet = PacketPool.GetSentPacket();
            packet.WriteUInt16(this._sessionID);
            packet.WriteUInt16((ushort)packetType);
            return packet;
        }

        private void LoginResponsePacketHandler(ReceivedPacket packet)
        {
            SessionID sessionID = packet.ReadUInt16();
            this._sessionID = sessionID;
            PlayerID playerID = packet.ReadByte();
            this._playerID = playerID;
            bool shouldRecoverLocalPlayerEntities = packet.ReadBoolean();
            _logger.Log("Logged in session id = " + this._sessionID);
            if(!(this._onLoggedIn is null))
            {
                this._onLoggedIn(shouldRecoverLocalPlayerEntities);
            }
        }

        private void SyncFrameBeginPacketHandler(ReceivedPacket packet)
        {
            uint syncFrameNumber = packet.ReadUInt32();
            this._clientSideFrameSynchronizationController.BeginSyncFrame(syncFrameNumber);
            this._clientSideEntityManager.SyncEntityDataToServer();
        }

        private void SyncLostResponseBeginPacketHandler(ReceivedPacket packet)
        {
            uint lastSyncFrameNumber = packet.ReadUInt32();
            this._clientSideFrameSynchronizationController.BeginSyncLostFrames(lastSyncFrameNumber);
        }

        private void SyncEntityCreationPacketHandler(ReceivedPacket packet)
        {
            packet.ReadUInt32();
            this._clientSideEntityManager.CreateNetworkEntity(packet);
        }

        private void SyncEntityDeletionPacketHandler(ReceivedPacket packet)
        {
            packet.ReadUInt32();
            this._clientSideEntityManager.DeleteNetworkEntity(packet);
        }

        private void SyncEntityDataPacketHandler(ReceivedPacket packet)
        {
            uint frameNumber = packet.ReadUInt32();
            this._clientSideEntityManager.SyncEntityDataFromServer(packet);
        }

        private void AskForBackupEntityAuthorityPacketHandler(ReceivedPacket packet)
        {
            uint runtimeID = packet.ReadUInt32();
            PlayerID originalAuthority = packet.ReadByte();
            if (!this._clientSideEntityManager.CanBecomeBackupAuthorityFor(runtimeID))
            {
                return;
            }
            using(SentPacket sentPacket = this.GetSentPacket(ClientToServerPacketTypes.EntityAuthorityBackupCandidateReply))
            {
                sentPacket.WriteUInt32(runtimeID);
                sentPacket.WriteByte(originalAuthority);
                this.SendPacketToServer(sentPacket);
                _logger.Log("sent EntityAuthorityBackupCandidateReply");
            }
        }
    }
}
