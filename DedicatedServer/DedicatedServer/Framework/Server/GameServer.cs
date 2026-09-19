using DedicatedServer.Framework.ECS;
using DedicatedServer.Framework.Networking;
using DedicatedServer.Framework.Networking.Connection;
using System;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Server
{
    using ClientID = UInt32;
    using PlayerID = Byte;
    using SessionID = UInt16;
    public class GameServer : ITickable, IDisposable
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<GameServer>();

        private readonly Configurations _configurations;
        private GameNetworkingListenerBase _listener;
        private List<GameNetworkingConnectionBase> _clients;
        private readonly Queue<ReceivedPacket> _receiveQueue;
        private readonly Dictionary<ClientToServerPacketTypes, Action<SessionID, PlayerID, ReceivedPacket>> _packetHandlers;

        private readonly GameRoom _gameRoom;
        private readonly SessionManager _sessionManager;
        internal readonly ServerSideEntityManager serverSideEntityManager;
        internal readonly ServerSideFrameSynchronizationController frameSyncController;

        public GameServer()
        {
            this._configurations = Configurations.GlobalConfigurations;
            this._clients = new List<GameNetworkingConnectionBase>();
            this._receiveQueue = new Queue<ReceivedPacket>();
            // packet handlers
            this._packetHandlers = new Dictionary<ClientToServerPacketTypes, Action<SessionID, PlayerID, ReceivedPacket>>();
            this._packetHandlers.Add(ClientToServerPacketTypes.CreateEntityWithAuthorityRequest, this.RequestCreateNetworkEntityWithAuthorityPacketHandler);
            this._packetHandlers.Add(ClientToServerPacketTypes.DeleteEntityRequest, this.DeleteEntityRequestPacketHandler);
            this._packetHandlers.Add(ClientToServerPacketTypes.AcquireLocalPlayerEntityAuthorityRequest, this.AcquireLocalPlayerEntityAuthorityRequestPacketHandler);
            this._packetHandlers.Add(ClientToServerPacketTypes.ClientFrameSyncHeartbeat, this.ClientFrameSyncHeartbeatPacketHandler);
            this._packetHandlers.Add(ClientToServerPacketTypes.SyncLostRequest, this.SyncLostRequestHandler);
            this._packetHandlers.Add(ClientToServerPacketTypes.SyncEntityData, this.SyncEntityDataPacketHandler);
            this._packetHandlers.Add(ClientToServerPacketTypes.EntityAuthorityBackupCandidateReply, this.EntityAuthorityBackupCandidateReplyPacketHandler);
            this._gameRoom = new GameRoom();
            this._sessionManager = new SessionManager();
            this.serverSideEntityManager = new ServerSideEntityManager(this);
            this.frameSyncController = new ServerSideFrameSynchronizationController(this);
        }

        public void StartServer(ushort port)
        {
            this._listener = new UdpListener();
            this._listener.Listen(port);
        }

        public void StartFrameSync()
        {
            this.frameSyncController.StartFrameSync();
        }

        public void Tick(float deltaTime)
        {
            if(this._listener is null)
            {
                return;
            }
            this._listener.Tick(deltaTime);
            GameNetworkingConnectionBase newClient = this._listener.GetNextNewClient();
            while(!(newClient is null))
            {
                this._clients.Add(newClient);
                newClient = this._listener.GetNextNewClient();
            }
            foreach(GameNetworkingConnectionBase client in this._clients)
            {
                client.Tick(deltaTime);
                ReceivedPacket packet = client.GetNextReceivedPacket();
                while (!(packet is null))
                {
                    SessionID sessionID = packet.ReadUInt16();
                    ClientToServerPacketTypes packetType = (ClientToServerPacketTypes)packet.ReadUInt16();
                    if (packetType == ClientToServerPacketTypes.CustomPacket)
                    {
                        this._receiveQueue.Enqueue(packet);
                    }
                    else if (packetType == ClientToServerPacketTypes.LoginRequest)
                    {
                        using (packet)
                        {
                            this.LoginRequestPacketHandler(client, packet);
                        }
                    }
                    else
                    {
                        using (packet)
                        {
                            PlayerID playerID = this._sessionManager.GetPlayerID(sessionID);
                            Action<SessionID, PlayerID, ReceivedPacket> packetHandler;
                            if (!this._packetHandlers.TryGetValue(packetType, out packetHandler))
                            {
                                throw new Exception("There is no packet handler for packet type " + packetType);
                            }
                            packetHandler(sessionID, playerID, packet);
                        }
                    }
                    packet = client.GetNextReceivedPacket();
                }
            }

            uint currentSyncFrameNumber = this.frameSyncController.SyncFrameNumber;
            IEnumerator<Session> sessions = this._sessionManager.GetSessions();
            while (sessions.MoveNext())
            {
                Session session = sessions.Current;
                if(session.IsConnected && currentSyncFrameNumber - session.lastHeartbeatFrameNumber >= this._configurations.DisconnectThersholdFrameCount)
                {
                    _logger.Log("disconnect " + currentSyncFrameNumber + " " + session.lastHeartbeatFrameNumber + " " + this._configurations.DisconnectThersholdFrameCount);
                    session.Disconnect();
                    this.serverSideEntityManager.OnPlayerDisconnected(session.playerID);
                }
            }

            this.frameSyncController.Tick(deltaTime);

            foreach(GameNetworkingConnectionBase client in this._clients)
            {
                client.SendAllPacketsInQueue();
            }
        }

        public ReceivedPacket GetNextReceivedPacket()
        {
            ReceivedPacket packet;
            if(this._receiveQueue.TryDequeue(out packet))
            {
                return packet;
            }
            return null;
        }

        public void Dispose()
        {
            this._listener?.Dispose();
        }

        public void Test_SendToFirstClient(SentPacket packet)
        {
            this._clients[0].SendPacketImmediately(packet);
        }

        private void LoginRequestPacketHandler(GameNetworkingConnectionBase connection, ReceivedPacket packet)
        {
            ClientID clientID = packet.ReadUInt32();
            SessionID sessionID;
            PlayerID playerID;
            bool isSessionReconnect = this._sessionManager.GetSessionID(clientID, out sessionID);
            if (!isSessionReconnect)
            {
                playerID = this._gameRoom.AddPlayer(clientID);
                sessionID = this._sessionManager.StartSession(connection, clientID, playerID, this.frameSyncController.SyncFrameNumber);
                _logger.Log("session started: sessionID = " + sessionID + " clientID = " + clientID + " playerID = " + playerID);
            }
            else
            {
                this._sessionManager.ReconnectSession(sessionID, connection, this.frameSyncController.SyncFrameNumber, out playerID);
                _logger.Log("session reconnected: sessionID = " + sessionID + " clientID = " + clientID + " playerID = " + playerID);
            }
            bool shouldRecoverLocalPlayerEntities = isSessionReconnect && this.serverSideEntityManager.HasRecoverableLocalPlayerEntities(playerID);
            using(SentPacket sentPacket = this.GetSentPacket(ServerToClientPacketTypes.LoginResponse))
            {
                sentPacket.WriteUInt16(sessionID);
                sentPacket.WriteByte(playerID);
                sentPacket.WriteBoolean(shouldRecoverLocalPlayerEntities);
                connection.QueuedSendPacket(sentPacket);
            }
            _logger.Log("Login client id = " + clientID + ", session id = " + sessionID + ", player id = " + playerID + ", should recover local player entities = " + shouldRecoverLocalPlayerEntities);
        }

        private void ClientFrameSyncHeartbeatPacketHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            Session session = this._sessionManager.GetSession(sessionID);
            session.lastHeartbeatFrameNumber = this.frameSyncController.SyncFrameNumber;
        }

        private void SyncLostRequestHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            uint clientCurrentSyncFrameNumber = packet.ReadUInt32();
            Session session = this._sessionManager.GetSession(sessionID);
            this.frameSyncController.BeginSyncLostFramesForSingleClient(session, clientCurrentSyncFrameNumber);
        }

        private void RequestCreateNetworkEntityWithAuthorityPacketHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            int registryID = packet.ReadInt32();
            EntityAuthorityType entityAuthorityType = (EntityAuthorityType)packet.ReadByte();
            int staticCustomDataLength = packet.ReadInt32();
            byte[] staticCustomData = new byte[staticCustomDataLength];
            packet.ReadBytes(staticCustomData, 0, staticCustomDataLength);
            this.serverSideEntityManager.CreateNetworkEntity(registryID, playerID, entityAuthorityType, staticCustomData);
        }

        private void AcquireLocalPlayerEntityAuthorityRequestPacketHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            this.serverSideEntityManager.AcquireLocalPlayerEntityAuthorities(playerID);
        }

        private void DeleteEntityRequestPacketHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            uint runtimeID = packet.ReadUInt32();
            this.serverSideEntityManager.RequestDeleteNetworkEntity(playerID, runtimeID);
        }

        private void SyncEntityDataPacketHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            this.serverSideEntityManager.SyncEntityDataFromClient(packet);
        }

        private void EntityAuthorityBackupCandidateReplyPacketHandler(SessionID sessionID, PlayerID playerID, ReceivedPacket packet)
        {
            uint runtimeID = packet.ReadUInt32();
            PlayerID authorityToReplace = packet.ReadByte();
            bool transferred = this.serverSideEntityManager.TryTransferEntityAuthorityToBackup(runtimeID, authorityToReplace, playerID);
            if(!transferred)
            {
                return;
            }
        }

        internal SentPacket GetSentPacket(ServerToClientPacketTypes packetType)
        {
            SentPacket packet = PacketPool.GetSentPacket();
            packet.WriteUInt16((ushort)packetType);
            return packet;
        }

        internal SentPacket GetSyncPacket(ServerToClientPacketTypes packetType)
        {
            SentPacket packet = this.GetSentPacket(packetType);
            packet.WriteUInt32(this.frameSyncController.SyncFrameNumber);
            return packet;
        }

        internal void BroadcastPacket(SentPacket packet)
        {
            int clientCount = 0;
            IEnumerator<Session> enumerator = this._sessionManager.GetSessions();
            while (enumerator.MoveNext())
            {
                Session session = enumerator.Current;
                if (!session.IsConnected)
                {
                    continue;
                }
                bool canSend = session.QueuedSendPacket(packet);
                if (canSend)
                {
                    clientCount++;
                }
            }
            packet.Dispose(clientCount);
        }

        internal void SendToSingleClient(Session session, SentPacket packet)
        {
            bool canSend = session.QueuedSendPacket(packet);
            if (canSend)
            {
                packet.Dispose(1);
            }
            else
            {
                packet.DisposeImmediately();
            }
        }
    }
}
