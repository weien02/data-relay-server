using DedicatedServer.Framework.ECS;
using DedicatedServer.Framework.Networking;
using System;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Server
{
    using PlayerID = Byte;
    public class ServerSideEntityManager
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<ServerSideEntityManager>();
        private class EntityRuntimeIDAllocator
        {
            private uint _number;

            public EntityRuntimeIDAllocator()
            {
                this._number = 0;
            }

            public uint Allocate()
            {
                uint result = this._number;
                this._number++;
                return result;
            }
        }

        private readonly Configurations _configurations;
        private readonly DictionaryList<uint, ServerSideEntity> _entities; // should ensure entities are ordered in their creation time order (from older to younger)
        private readonly EntityRuntimeIDAllocator _entityRuntimeIDAllocator;
        private readonly List<ServerSideEntity> _entityCreatedInTheCurrentSyncFrame;
        private readonly List<ServerSideEntity> _entityDeletedInTheCurrentSyncFrame;
        // The deleted entities will leave a "tombstone".
        // It only contains the entity id and the sync frame number that the entity is deleted, and all other resources for the entity are disposed.
        // When a client lose sync, the server can use the tombstone to know the entities that are deleted when the client is losing sync, and send the information to the client.
        private readonly List<ServerSideEntity> _deletedEntities;
        private bool HasNewEntitiesToSyncToClients => this._entityCreatedInTheCurrentSyncFrame.Count > 0;
        private bool HasDeletedEntitiesToSyncToClients => this._entityDeletedInTheCurrentSyncFrame.Count > 0;
        private readonly GameServer _gameServer;

        internal ServerSideEntityManager(GameServer gameServer)
        {
            this._configurations = Configurations.GlobalConfigurations;
            this._entities = new DictionaryList<uint, ServerSideEntity>();
            this._entityRuntimeIDAllocator = new EntityRuntimeIDAllocator();
            this._entityCreatedInTheCurrentSyncFrame = new List<ServerSideEntity>();
            this._entityDeletedInTheCurrentSyncFrame = new List<ServerSideEntity>();
            this._deletedEntities = new List<ServerSideEntity>();
            this._gameServer = gameServer;
        }

        internal void CreateNetworkEntity(int registryID, PlayerID initialAuthorityPlayerID, EntityAuthorityType initialAuthorityPlayerType, byte[] staticCustomData)
        {
            _logger.Assert(initialAuthorityPlayerType != EntityAuthorityType.NoAuthority, "initialAuthorityPlayerType != EntityAuthorityType.NoAuthority");
            uint entityRuntimeID = this._entityRuntimeIDAllocator.Allocate();
            ServerSideEntity entity = new ServerSideEntity(registryID, entityRuntimeID, this.GetCurrentSyncFrameNumber(), staticCustomData, initialAuthorityPlayerID, initialAuthorityPlayerType);
            this._entities.Add(entityRuntimeID, entity);
            this._entityCreatedInTheCurrentSyncFrame.Add(entity);
            _logger.Log("Created network entity " + entityRuntimeID + ", registry id = " + registryID + ", initial authority player id = " + initialAuthorityPlayerID + ", initial authority player type = " + initialAuthorityPlayerType);
        }

        private uint GetCurrentSyncFrameNumber()
        {
            return this._gameServer.frameSyncController.SyncFrameNumber;
        }

        private void SyncEntityCreationToAllClients()
        {
            if (!this.HasNewEntitiesToSyncToClients)
            {
                return;
            }
            foreach(ServerSideEntity entity in this._entityCreatedInTheCurrentSyncFrame)
            {
                SentPacket packet = this._gameServer.GetSyncPacket(ServerToClientPacketTypes.SyncEntityCreation);
                entity.WriteCreationInformation(packet);
                this._gameServer.BroadcastPacket(packet);
            }
            this._entityCreatedInTheCurrentSyncFrame.Clear();
        }

        internal void SyncEntityCreationToSingleClient(Session session, uint clientCurrentSyncFrameNumber)
        {
            foreach(ServerSideEntity entity in this._entities)
            {
                if(entity.CreationFrameNumber >= clientCurrentSyncFrameNumber)
                {
                    SentPacket packet = this._gameServer.GetSyncPacket(ServerToClientPacketTypes.SyncEntityCreation);
                    entity.WriteCreationInformation(packet);
                    this._gameServer.SendToSingleClient(session, packet);
                }
            }
        }

        private void SyncEntityDeletionToAllClients()
        {
            if (!this.HasDeletedEntitiesToSyncToClients)
            {
                return;
            }
            foreach (ServerSideEntity deletedEntity in this._entityDeletedInTheCurrentSyncFrame)
            {
                SentPacket packet = this._gameServer.GetSyncPacket(ServerToClientPacketTypes.SyncEntityDeletion);
                deletedEntity.WriteDeletionInformation(packet);
                this._gameServer.BroadcastPacket(packet);
            }
            this._entityDeletedInTheCurrentSyncFrame.Clear();
        }

        internal void SyncEntityDeletionToSingleClient(Session session, uint clientCurrentSyncFrameNumber)
        {
            uint currentSyncFrameNumber = this.GetCurrentSyncFrameNumber();
            foreach (ServerSideEntity deletedEntity in this._deletedEntities)
            {
                if (deletedEntity.DeletedSyncFrameNumber < clientCurrentSyncFrameNumber || deletedEntity.DeletedSyncFrameNumber > currentSyncFrameNumber)
                {
                    continue;
                }
                SentPacket packet = this._gameServer.GetSyncPacket(ServerToClientPacketTypes.SyncEntityDeletion);
                deletedEntity.WriteDeletionInformation(packet);
                this._gameServer.SendToSingleClient(session, packet);
            }
        }

        /// <summary>
        /// This method is for sync data to all clients in every sync frame, or sync data to a client that lose sync.
        /// The server will send the latest sync frame data and the entity creation and deletion data within startFrameNumberInclusive and endFrameNumberInclusive.
        /// When syncFullData = false, the server will only sync dirty data, and this is used for broadcasting data to all clients in every sync frame.
        /// When syncFullData = true, the server will send the full data of the latest sync frame. This is for sync data to a client that lose sync.
        /// </summary>
        /// <param name="startFrameNumberInclusive"></param>
        /// <param name="endFrameNumberInclusive"></param>
        /// <param name="syncFullData"></param>
        /// <returns></returns>
        private List<SentPacket> SyncEntitiesDataInInterval(uint startFrameNumberInclusive, uint endFrameNumberInclusive, bool syncFullData)
        {
            List<SentPacket> packets = new List<SentPacket>();
            uint currentSyncFrameNumber = this.GetCurrentSyncFrameNumber();
            foreach(ServerSideEntity entity in this._entities)
            {
                if(!this._configurations.EnableDirtyOnlySync || (entity.LastModifiedFrameNumber >= startFrameNumberInclusive && entity.LastModifiedFrameNumber <= endFrameNumberInclusive))
                {
                    bool writeFullData = syncFullData
                        || !this._configurations.EnableDirtyOnlySync
                        || entity.ShouldForceFullDataSyncInFrame(currentSyncFrameNumber);
                    SentPacket packet = this._gameServer.GetSyncPacket(ServerToClientPacketTypes.SyncEntityData);
                    entity.WriteToPacket(packet, writeFullData);
                    packets.Add(packet);
                }
            }
            return packets;
        }

        private void SyncEntitiesDataToAllClients()
        {
            uint currentSyncFrame = this.GetCurrentSyncFrameNumber();
            List<SentPacket> packets = this.SyncEntitiesDataInInterval(currentSyncFrame, currentSyncFrame, false);
            foreach(SentPacket packet in packets)
            {
                this._gameServer.BroadcastPacket(packet);
            }
        }

        private void SyncEntitiesDataToSingleClient(Session session, uint clientCurrentSyncFrameNumber)
        {
            List<SentPacket> packets = this.SyncEntitiesDataInInterval(clientCurrentSyncFrameNumber, this.GetCurrentSyncFrameNumber(), true);
            foreach(SentPacket packet in packets)
            {
                this._gameServer.SendToSingleClient(session, packet);
            }
        }

        internal void SyncCurrentFrameToAllClients()
        {
            this.SyncEntityCreationToAllClients();
            this.SyncEntitiesDataToAllClients();
            this.SyncEntityDeletionToAllClients();
        }

        internal void SyncEntitiesToSingleClient(Session session, uint clientCurrentSyncFrameNumber)
        {
            // for sync data to client that lose sync
            this.SyncEntityCreationToSingleClient(session, clientCurrentSyncFrameNumber);
            this.SyncEntitiesDataToSingleClient(session, clientCurrentSyncFrameNumber);
            this.SyncEntityDeletionToSingleClient(session, clientCurrentSyncFrameNumber);
        }

        internal void SyncEntityDataFromClient(ReceivedPacket packet)
        {
            uint runtimeID = packet.ReadUInt32();
            ServerSideEntity entity = this._entities.Get(runtimeID);
            if (entity is null)
            {
                _logger.LogWarning("Ignoring client entity data sync for deleted or unknown runtime id " + runtimeID);
                return;
            }
            entity.ReadFromPacket(this.GetCurrentSyncFrameNumber(), packet);
        }

        private ServerSideEntity GetEntity(uint runtimeID)
        {
            return this._entities.Get(runtimeID);
        }

        internal void OnPlayerDisconnected(PlayerID playerID)
        {
            if (!this._configurations.EnableAutomaticAuthorityTransfer)
            {
                return;
            }
            _logger.Log("Player " + playerID + " is disconnected, preparing entity authority backup");
            List<ServerSideEntity> authorityTransferEntities = new List<ServerSideEntity>();
            foreach(ServerSideEntity entity in this._entities)
            {
                if(entity.EffectiveAuthorityPlayerID == playerID)
                {
                    authorityTransferEntities.Add(entity);
                }
            }
            foreach(ServerSideEntity entity in authorityTransferEntities)
            {
                _logger.Log("Asking for backup entity authority " + entity.RuntimeID);
                entity.IsTransferringEntityAuthority = true;
                SentPacket packet = this._gameServer.GetSentPacket(ServerToClientPacketTypes.AskForBackupEntityAuthority);
                packet.WriteUInt32(entity.RuntimeID);
                packet.WriteByte(entity.EffectiveAuthorityPlayerID);
                this._gameServer.BroadcastPacket(packet);
            }
        }

        internal bool TryTransferEntityAuthorityToBackup(uint runtimeID, PlayerID authorityToReplace, PlayerID backupAuthority)
        {
            ServerSideEntity entity = this.GetEntity(runtimeID);
            if (entity is null)
            {
                return false;
            }
            // the server will receive reply from other candidates after transferring the entity authority to the candidate that the server receives its reply first
            // the reply from candidates will include the player id of the original authority of the entity that requires backup authority.
            // if the current effective authority player id is not equal to the original authority player id in the reply from candidates, this means the authority is already transferred, and the later reply should be ignored by the server.
            if(entity.EffectiveAuthorityPlayerID != authorityToReplace)
            {
                return false;
            }
            entity.SetEntityAuthorityInformation(backupAuthority, EntityAuthorityType.TemporaryBackup);
            entity.SetEffectiveAuthorityPlayer(backupAuthority, this.GetCurrentSyncFrameNumber());
            entity.IsTransferringEntityAuthority = false;
            _logger.Log("entity authority of " + runtimeID + " is transferred from " + authorityToReplace + " to backup " + backupAuthority);
            return true;
        }

        internal bool RequestDeleteNetworkEntity(PlayerID playerID, uint runtimeID)
        {
            ServerSideEntity entity = this.GetEntity(runtimeID);
            if (entity is null)
            {
                _logger.LogWarning("Ignoring delete request for deleted or unknown runtime id " + runtimeID);
                return false;
            }
            if (!entity.CanPlayerDelete(playerID))
            {
                _logger.LogWarning("Player " + playerID + " is not allowed to delete entity " + runtimeID);
                return false;
            }

            this.DeleteEntity(entity, this.GetCurrentSyncFrameNumber());
            return true;
        }

        internal bool HasRecoverableLocalPlayerEntities(PlayerID playerID)
        {
            foreach (ServerSideEntity entity in this._entities)
            {
                if (entity.CanPlayerAcquireLocalPlayerAuthority(playerID))
                {
                    return true;
                }
            }
            return false;
        }

        private void DeleteEntity(ServerSideEntity entity, uint deletionFrameNumber)
        {
            uint runtimeID = entity.RuntimeID;
            this._entities.Remove(runtimeID);
            this._entityCreatedInTheCurrentSyncFrame.Remove(entity);
            entity.ConvertToDeletedEntityTombstone(deletionFrameNumber);

            this._entityDeletedInTheCurrentSyncFrame.Add(entity);
            this._deletedEntities.Add(entity);
            _logger.Log("Deleted network entity " + runtimeID + " at sync frame " + deletionFrameNumber);
        }

        internal void AcquireLocalPlayerEntityAuthorities(PlayerID playerID)
        {
            int transferredEntityCount = 0;
            foreach (ServerSideEntity entity in this._entities)
            {
                if (!entity.CanPlayerAcquireLocalPlayerAuthority(playerID))
                {
                    continue;
                }
                if (entity.EffectiveAuthorityPlayerID == playerID && entity.EffectiveAuthorityPlayerType == EntityAuthorityType.Full_LocalPlayer)
                {
                    continue;
                }

                entity.SetEffectiveAuthorityPlayer(playerID, this.GetCurrentSyncFrameNumber());
                entity.IsTransferringEntityAuthority = false;
                transferredEntityCount++;
                _logger.Log("entity authority of " + entity.RuntimeID + " is acquired back by local player " + playerID);
            }
            if (transferredEntityCount > 0)
            {
                _logger.Log("Acquired back " + transferredEntityCount + " local player entit(ies) for player " + playerID);
            }
        }
    }
}
