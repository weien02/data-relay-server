using DedicatedServer.Framework.Networking;
using System.Collections;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Client
{
    public class ClientSideFrameSynchronizationController
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<ClientSideFrameSynchronizationController>();

        private uint _syncFrameNumber;
        public uint SyncFrameNumber => this._syncFrameNumber;
        private GameClient _gameClient;
        private bool _isSyncLost;

        internal ClientSideFrameSynchronizationController(GameClient gameClient)
        {
            this._gameClient = gameClient;
            this._isSyncLost = false;
        }

        internal void BeginSyncFrame(uint syncFrameNumber)
        {
            //_logger.Log("Begin sync frame " + syncFrameNumber);
            this.SendFrameSyncHeartbeatPacket();
            if (this._isSyncLost)
            {
                return;
            }
            if (syncFrameNumber - this._syncFrameNumber > 1)
            {
                this._isSyncLost = true;
                _logger.Log(string.Format("Sync lost between sync frames {0} and {1}, requesting lost frames", this._syncFrameNumber, syncFrameNumber));
                using (SentPacket packet = this._gameClient.GetSentPacket(ClientToServerPacketTypes.SyncLostRequest))
                {
                    packet.WriteUInt32(this._syncFrameNumber);
                    this._gameClient.SendPacketToServer(packet);
                }
            }
            else
            {
                this._syncFrameNumber = syncFrameNumber;
            }
        }

        internal void BeginSyncLostFrames(uint lastSyncFrameNumber)
        {
            _logger.Log(string.Format("Begin sync lost frames from {0} to {1}", this._syncFrameNumber, lastSyncFrameNumber));
            this._syncFrameNumber = lastSyncFrameNumber;
            this._isSyncLost = false;
        }

        private void SendFrameSyncHeartbeatPacket()
        {
            using(SentPacket packet = this._gameClient.GetSentPacket(ClientToServerPacketTypes.ClientFrameSyncHeartbeat))
            {
                this._gameClient.SendPacketToServer(packet);
            }
        }
    }
}
