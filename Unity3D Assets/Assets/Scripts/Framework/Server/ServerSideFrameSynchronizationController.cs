using DedicatedServer.Framework.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Server
{
    public class ServerSideFrameSynchronizationController : ITickable
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<ServerSideFrameSynchronizationController>();

        private bool _sync;
        private int _syncRatePerSecond;
        private float _syncInterval;
        private float _syncTimer;
        private uint _syncFrameNumber;
        public uint SyncFrameNumber => this._syncFrameNumber;
        private GameServer _gameServer;

        internal ServerSideFrameSynchronizationController(GameServer gameServer)
        {
            this._sync = false;
            this._gameServer = gameServer;
        }

        public void StartFrameSync()
        {
            this._syncRatePerSecond = Configurations.GlobalConfigurations.SyncRatePerSecond;
            this._syncInterval = 1.0f / this._syncRatePerSecond;
            this._syncTimer = 0;
            this._syncFrameNumber = 0;
            this._sync = true;
        }

        public void Tick(float deltaTime)
        {
            if (!this._sync)
            {
                return;
            }
            if(deltaTime > this._syncInterval)
            {
                _logger.LogWarning("The delta time is larger than the sync interval. This may cause the server to sync multiple frames in the current tick. delta time = " + deltaTime + ", sync interval = " + this._syncInterval);
            }
            this._syncTimer += deltaTime;
            while(this._syncTimer >= this._syncInterval)
            {
                this.SyncToClients();
                this.BeginNextSyncFrame(deltaTime);
            }
        }

        private void Sync()
        {
            this.SyncToClients();
        }

        private void SyncToClients()
        {
            this._gameServer.serverSideEntityManager.SyncCurrentFrameToAllClients();
        }

        private void BeginNextSyncFrame(float deltaTime)
        {
            this._syncFrameNumber++;
            this._syncTimer -= deltaTime;
            SentPacket packet = this._gameServer.GetSentPacket(ServerToClientPacketTypes.SyncFrameBegin);
            packet.WriteUInt32(this._syncFrameNumber);
            this._gameServer.BroadcastPacket(packet);
        }

        internal void BeginSyncLostFramesForSingleClient(Session session, uint clientCurrentSyncFrameNumber)
        {
            SentPacket packet = this._gameServer.GetSentPacket(ServerToClientPacketTypes.SyncLostResponseBegin);
            packet.WriteUInt32(this._syncFrameNumber);
            this._gameServer.SendToSingleClient(session, packet);
            this._gameServer.serverSideEntityManager.SyncEntitiesToSingleClient(session, clientCurrentSyncFrameNumber);
        }
    }
}
