using DedicatedServer.Framework.ECS;
using DedicatedServer.Framework.Networking;
using System;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Server
{
    using PlayerID = Byte;
    internal class ServerSideEntity : EntityBase
    {
        private class AuthorityPlayer
        {
            public readonly PlayerID playerID;
            private EntityAuthorityType _authorityType;
            public EntityAuthorityType AuthorityType => this._authorityType;

            public AuthorityPlayer(PlayerID playerID)
            {
                this.playerID = playerID;
                this._authorityType = EntityAuthorityType.NoAuthority;
            }

            public void SetAuthorityType(EntityAuthorityType authorityType)
            {
                this._authorityType = authorityType;
            }
        }

        private byte[] _staticCustomData;
        private uint _creationFrameNumber;
        public uint CreationFrameNumber => this._creationFrameNumber;
        private uint _lastModifiedFrameNumber;
        public uint LastModifiedFrameNumber => this._lastModifiedFrameNumber;
        private PlayerID _effectiveAuthorityPlayerID;
        public PlayerID EffectiveAuthorityPlayerID => this._effectiveAuthorityPlayerID;
        private EntityAuthorityType _effectiveAuthorityPlayerType;
        public EntityAuthorityType EffectiveAuthorityPlayerType => this._effectiveAuthorityPlayerType;
        // the authority list
        // using this list, the server can know whether the effective authority is a backup or not
        private Dictionary<PlayerID, AuthorityPlayer> _authorityPlayers;
        public bool IsTransferringEntityAuthority { get; set; }
        private bool _shouldForceFullDataSync;
        private uint _forceFullDataSyncFrameNumber;

        // entity tombstone data
        // see more information in ServerSideEntityManager.cs
        private bool _isDeleted;
        public bool IsDeleted => this._isDeleted;
        private uint _deletedSyncFrameNumber;
        public uint DeletedSyncFrameNumber => this._deletedSyncFrameNumber;

        internal ServerSideEntity(int registryID, uint runtimeID, uint creationFrameNumber, byte[] staticCustomData, PlayerID initialAuthorityPlayerID, EntityAuthorityType initialAuthorityPlayerType) : base(registryID, runtimeID)
        {
            this._staticCustomData = staticCustomData;
            this._creationFrameNumber = creationFrameNumber;
            this._lastModifiedFrameNumber = this._creationFrameNumber;
            this._authorityPlayers = new Dictionary<PlayerID, AuthorityPlayer>();
            this._shouldForceFullDataSync = false;
            this._forceFullDataSyncFrameNumber = 0;
            this._isDeleted = false;
            this._deletedSyncFrameNumber = 0;
            this.SetEntityAuthorityInformation(initialAuthorityPlayerID, initialAuthorityPlayerType);
            this.SetEffectiveAuthorityPlayer(initialAuthorityPlayerID, creationFrameNumber);
            this.IsTransferringEntityAuthority = false;
        }

        internal void WriteCreationInformation(SentPacket packet)
        {
            this.AssertIsNotDeleted();
            packet.WriteUInt32(this.RuntimeID);
            packet.WriteInt32(this.RegistryID);
            packet.WriteByte(this._effectiveAuthorityPlayerID);
            packet.WriteByte((byte)this._effectiveAuthorityPlayerType);
            int staticCustomDataLength = this._staticCustomData.Length;
            packet.WriteInt32(staticCustomDataLength);
            packet.WriteBytes(this._staticCustomData, 0, staticCustomDataLength);
        }

        internal void ReadFromPacket(uint syncFrameNumber, ReceivedPacket packet)
        {
            this.AssertIsNotDeleted();
            EntityDataContainer entityDataContainer = this.EntityDataContainer;
            entityDataContainer.ReadFromPacket(packet, true);
            if (entityDataContainer.IsDirty)
            {
                this._lastModifiedFrameNumber = syncFrameNumber;
            }
        }

        internal void WriteToPacket(SentPacket packet, bool writeFullData)
        {
            this.AssertIsNotDeleted();
            packet.WriteUInt32(this.RuntimeID);
            packet.WriteByte(this._effectiveAuthorityPlayerID);
            packet.WriteByte((byte)this._effectiveAuthorityPlayerType);
            EntityDataContainer entityDataContainer = this.EntityDataContainer;
            entityDataContainer.WriteToPacket(packet, writeFullData);
        }

        internal void WriteDeletionInformation(SentPacket packet)
        {
            packet.WriteUInt32(this.RuntimeID);
        }

        internal void SetEntityAuthorityInformation(PlayerID playerID, EntityAuthorityType entityAuthorityType)
        {
            this.AssertIsNotDeleted();
            AuthorityPlayer authorityPlayer;
            if(!this._authorityPlayers.TryGetValue(playerID, out authorityPlayer))
            {
                authorityPlayer = new AuthorityPlayer(playerID);
                this._authorityPlayers.Add(playerID, authorityPlayer);
            }
            authorityPlayer.SetAuthorityType(entityAuthorityType);
        }

        internal EntityAuthorityType GetPlayerAuthorityType(PlayerID playerID)
        {
            if (this._authorityPlayers is null)
            {
                return EntityAuthorityType.NoAuthority;
            }
            AuthorityPlayer authorityPlayer;
            if(!this._authorityPlayers.TryGetValue(playerID, out authorityPlayer))
            {
                return EntityAuthorityType.NoAuthority;
            }
            return authorityPlayer.AuthorityType;
        }

        internal bool CanPlayerAcquireLocalPlayerAuthority(PlayerID playerID)
        {
            if (this._isDeleted)
            {
                return false;
            }
            return this.GetPlayerAuthorityType(playerID) == EntityAuthorityType.Full_LocalPlayer;
        }

        internal bool CanPlayerDelete(PlayerID playerID)
        {
            if (this._isDeleted)
            {
                return false;
            }
            return this._effectiveAuthorityPlayerType != EntityAuthorityType.NoAuthority && this._effectiveAuthorityPlayerID == playerID;
        }

        internal bool ShouldForceFullDataSyncInFrame(uint syncFrameNumber)
        {
            return this._shouldForceFullDataSync && this._forceFullDataSyncFrameNumber == syncFrameNumber;
        }

        internal void SetEffectiveAuthorityPlayer(PlayerID playerID, uint syncFrameNumber)
        {
            this.AssertIsNotDeleted();
            EntityAuthorityType entityAuthorityType = this._authorityPlayers[playerID].AuthorityType;
            this._effectiveAuthorityPlayerID = playerID;
            this._effectiveAuthorityPlayerType = entityAuthorityType;
            this._lastModifiedFrameNumber = syncFrameNumber;
            this._shouldForceFullDataSync = true;
            this._forceFullDataSyncFrameNumber = syncFrameNumber;
        }

        internal void ConvertToDeletedEntityTombstone(uint deletedSyncFrameNumber)
        {
            this._isDeleted = true;
            this._deletedSyncFrameNumber = deletedSyncFrameNumber;
            this._staticCustomData = null;
            this._creationFrameNumber = 0;
            this._lastModifiedFrameNumber = 0;
            this._effectiveAuthorityPlayerID = 0;
            this._effectiveAuthorityPlayerType = EntityAuthorityType.NoAuthority;
            this._shouldForceFullDataSync = false;
            this._forceFullDataSyncFrameNumber = 0;
            this.IsTransferringEntityAuthority = false;
            this._authorityPlayers?.Clear();
            this._authorityPlayers = null;
            this.SetRegistryID(-1);
            this.ReleaseEntityDataContainer();
        }

        private void AssertIsNotDeleted()
        {
            if (this._isDeleted)
            {
                throw new InvalidOperationException("The server side entity " + this.RuntimeID + " is already deleted and only keeps tombstone data.");
            }
        }
    }
}
