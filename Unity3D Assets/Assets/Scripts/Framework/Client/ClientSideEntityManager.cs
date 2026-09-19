using DedicatedServer.Framework.Client.ECS;
using DedicatedServer.Framework.Client.ECS.Parallel;
using DedicatedServer.Framework.ECS;
using DedicatedServer.Framework.Networking;
using System;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Client
{
    using PlayerID = Byte;
    public class ClientSideEntityManager : ITickable
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<ClientSideEntityManager>();
        private readonly GameClient _gameClient;
        private readonly DictionaryList<uint, ClientSideEntity> _entities;
        private readonly DictionaryList<uint, ClientSideEntity> _entitiesWithAuthority;
        private readonly DictionaryList<uint, ClientSideEntity> _entitiesWithoutAuthority;
        private bool _isRecoveringReconnectedLocalPlayerEntityAuthority;
        private readonly HashSet<uint> _reconnectedLocalPlayerEntityRuntimeIDsWithAppliedInitialServerSync;
        public delegate void CreateNetworkEntityHandlerDelegate(uint entityRuntimeID, byte[] staticCustomDataBuffer, int staticCustomDataLength);
        public delegate void DeleteNetworkEntityHandlerDelegate(uint entityRuntimeID);
        private CreateNetworkEntityHandlerDelegate _createNetworkEntityHandler;
        private DeleteNetworkEntityHandlerDelegate _deleteNetworkEntityHandler;
        public CreateNetworkEntityHandlerDelegate CreateNetworkEntityHandler { set { this._createNetworkEntityHandler = value; } }
        public DeleteNetworkEntityHandlerDelegate DeleteNetworkEntityHandler { set { this._deleteNetworkEntityHandler = value; } }
        private readonly byte[] _staticCustomDataBuffer;
        private const int StaticCustomDataBuffer_Length = 10240;
        private readonly HashSet<uint> _syncEntityDataToServerBuffer;

        private readonly List<Action> _childThreadWriteTickWorkItems;
        private readonly List<ClientSideEntity> _entitiesWithChildThreadWriteTick;

        internal ClientSideEntityManager(GameClient gameClient)
        {
            this._gameClient = gameClient;
            this._entities = new DictionaryList<uint, ClientSideEntity>();
            this._entitiesWithAuthority = new DictionaryList<uint, ClientSideEntity>();
            this._entitiesWithoutAuthority = new DictionaryList<uint, ClientSideEntity>();
            this._isRecoveringReconnectedLocalPlayerEntityAuthority = false;
            this._reconnectedLocalPlayerEntityRuntimeIDsWithAppliedInitialServerSync = new HashSet<uint>();
            this._staticCustomDataBuffer = new byte[StaticCustomDataBuffer_Length];
            this._syncEntityDataToServerBuffer = new HashSet<uint>();
            this._childThreadWriteTickWorkItems = new List<Action>();
            this._entitiesWithChildThreadWriteTick = new List<ClientSideEntity>();
        }

        public void RequestCreateNetworkEntityWithAuthority(int registryID, EntityAuthorityType authorityType, byte[] staticCustomData)
        {
            using(SentPacket packet = this._gameClient.GetSentPacket(ClientToServerPacketTypes.CreateEntityWithAuthorityRequest))
            {
                packet.WriteInt32(registryID);
                packet.WriteByte((byte)authorityType);
                int staticCustomDataLength = staticCustomData.Length;
                packet.WriteInt32(staticCustomDataLength);
                packet.WriteBytes(staticCustomData, 0, staticCustomDataLength);
                this._gameClient.SendPacketToServer(packet);
            }
            _logger.Log("Requested to create network entity with authority");
        }

        public void RequestAcquireLocalPlayerEntityAuthorities()
        {
            this._isRecoveringReconnectedLocalPlayerEntityAuthority = true;
            this._reconnectedLocalPlayerEntityRuntimeIDsWithAppliedInitialServerSync.Clear();
            using (SentPacket packet = this._gameClient.GetSentPacket(ClientToServerPacketTypes.AcquireLocalPlayerEntityAuthorityRequest))
            {
                this._gameClient.SendPacketToServer(packet);
            }
            _logger.Log("Requested to acquire local player entity authorities");
        }

        public void RequestDeleteNetworkEntity(uint runtimeID)
        {
            ClientSideEntity entity = this._entities.Get(runtimeID);
            if (entity is null)
            {
                _logger.LogWarning("Ignoring delete request for unknown runtime id " + runtimeID);
                return;
            }
            if (!entity.HasAuthority)
            {
                _logger.LogWarning("Ignoring delete request for entity without local authority. runtime id = " + runtimeID);
                return;
            }
            using (SentPacket packet = this._gameClient.GetSentPacket(ClientToServerPacketTypes.DeleteEntityRequest))
            {
                packet.WriteUInt32(runtimeID);
                this._gameClient.SendPacketToServer(packet);
            }
            _logger.Log("Requested to delete network entity " + runtimeID);
        }

        internal void CreateNetworkEntity(ReceivedPacket packet)
        {
            int staticCustomDataLength;
            uint runtimeID = packet.ReadUInt32();
            if (this._entities.ContainsKey(runtimeID))
            {
                _logger.LogWarning("The entity with the runtime id " + runtimeID + " already exists.");
                return;
            }
            EntityAuthorityType authorityType;
            ClientSideEntity entity = ClientSideEntity.FromCreationInformation(runtimeID, this._gameClient.PlayerID, packet, this._staticCustomDataBuffer, out staticCustomDataLength, out authorityType);
            this._entities.Add(entity.RuntimeID, entity);
            if (authorityType != EntityAuthorityType.NoAuthority)
            {
                this.OnBecomeEntityAuthority(entity, true, authorityType);
            }
            else
            {
                this.OnUnbecomeEntityAuthority(entity, true);
            }
            this._createNetworkEntityHandler(entity.RuntimeID, this._staticCustomDataBuffer, staticCustomDataLength);
            entity.Initialize();
        }

        internal void DeleteNetworkEntity(ReceivedPacket packet)
        {
            uint runtimeID = packet.ReadUInt32();
            ClientSideEntity entity = this._entities.Get(runtimeID);
            if (entity is null)
            {
                _logger.LogWarning("Ignoring delete entity packet for unknown runtime id " + runtimeID);
                return;
            }

            if (entity.HasAuthority)
            {
                this._entitiesWithAuthority.Remove(runtimeID);
            }
            else
            {
                this._entitiesWithoutAuthority.Remove(runtimeID);
            }
            this._entities.Remove(runtimeID);
            this._reconnectedLocalPlayerEntityRuntimeIDsWithAppliedInitialServerSync.Remove(runtimeID);
            this.RemoveEntityDataFromSyncBuffer(runtimeID);
            this._deleteNetworkEntityHandler?.Invoke(runtimeID);
            _logger.Log("Deleted client entity " + runtimeID);
        }

        private void OnBecomeEntityAuthority(ClientSideEntity entity, bool isNewEntity, EntityAuthorityType authorityType)
        {
            if (!isNewEntity)
            {
                this._entitiesWithoutAuthority.Remove(entity.RuntimeID);
            }
            this._entitiesWithAuthority.Add(entity.RuntimeID, entity);
            entity.SetAuthorityType(authorityType);
            _logger.Log("OnBecomeEntityAuthority " + entity.RuntimeID);
        }

        private void OnUnbecomeEntityAuthority(ClientSideEntity entity, bool isNewEntity)
        {
            if (!isNewEntity)
            {
                this._entitiesWithAuthority.Remove(entity.RuntimeID);
            }
            this._entitiesWithoutAuthority.Add(entity.RuntimeID, entity);
            entity.SetAuthorityType(EntityAuthorityType.NoAuthority);
            this.RemoveEntityDataFromSyncBuffer(entity.RuntimeID);
            _logger.Log("OnUnbecomeEntityAuthority " + entity.RuntimeID);
        }

        private ClientSideEntity GetEntity(uint runtimeID)
        {
            ClientSideEntity entity = this._entities.Get(runtimeID);
            if(entity is null)
            {
                throw new Exception("There is no entity with the runtime id " + runtimeID);
            }
            return entity;
        }

        internal void SyncEntityDataFromServer(ReceivedPacket packet)
        {
            uint runtimeID = packet.ReadUInt32();
            ClientSideEntity entity = this._entities.Get(runtimeID);
            if (entity is null)
            {
                _logger.LogWarning("Ignoring sync entity data for unknown runtime id " + runtimeID + ". The entity creation packet may still be catching up.");
                return;
            }
            PlayerID authority = packet.ReadByte();
            EntityAuthorityType authorityType = (EntityAuthorityType)packet.ReadByte();

            bool shouldApplyInitialServerSyncForReconnectedLocalPlayerEntity = this._isRecoveringReconnectedLocalPlayerEntityAuthority
                && this._gameClient.IsSelf(authority)
                && authorityType == EntityAuthorityType.Full_LocalPlayer
                && !this._reconnectedLocalPlayerEntityRuntimeIDsWithAppliedInitialServerSync.Contains(runtimeID);

            if(!entity.HasAuthority && this._gameClient.IsSelf(authority))
            {
                this.OnBecomeEntityAuthority(entity, false, authorityType);
            }
            else if(entity.HasAuthority && !this._gameClient.IsSelf(authority))
            {
                this.OnUnbecomeEntityAuthority(entity, false);
            }

            // This is for reconnected players who gained back the authority of their own player avatars.
            // The player need to gain the authority for the player avatar's data after one read tick.
            // Otherwise, if the player gains the authority of the player avatar entity immediately after reconnecting,
            // the player avatar entity will reset to its initial state, since the reconnected player will use the initial entity data and to overwrite the data in the server. 
            if (shouldApplyInitialServerSyncForReconnectedLocalPlayerEntity)
            {
                ushort receivedDataStoreCount = entity.EntityDataContainer.ReadFromPacket(packet, false);
                if (receivedDataStoreCount > 0)
                {
                    entity.ReadTick();
                    this._reconnectedLocalPlayerEntityRuntimeIDsWithAppliedInitialServerSync.Add(runtimeID);
                }
                return;
            }

            if (entity.HasAuthority)
            {
                return;
            }
            entity.EntityDataContainer.ReadFromPacket(packet, false);
        }

        
        private void WriteEntityDataToSyncBuffer(ClientSideEntity entity)
        {
            this._syncEntityDataToServerBuffer.Add(entity.RuntimeID);
        }

        internal void SyncEntityDataToServer()
        {
            foreach (uint runtimeID in this._syncEntityDataToServerBuffer)
            {
                ClientSideEntity entity = this._entitiesWithAuthority.Get(runtimeID);
                if (entity is null)
                {
                    continue;
                }

                SentPacket packet = this._gameClient.GetSentPacket(ClientToServerPacketTypes.SyncEntityData);
                packet.WriteUInt32(entity.RuntimeID);
                if (!entity.EntityDataContainer.WriteToPacket(packet, !Configurations.GlobalConfigurations.EnableDirtyOnlySync))
                {
                    packet.DisposeImmediately();
                    continue;
                }
                this._gameClient.SendPacketToServer(packet);
            }
            this._syncEntityDataToServerBuffer.Clear();
        }

        private void RemoveEntityDataFromSyncBuffer(uint runtimeID)
        {
            this._syncEntityDataToServerBuffer.Remove(runtimeID);
        }

        private void LogParallelWriteTickReport(ParallelWorkReport parallelWorkReport)
        {
            if (parallelWorkReport is null)
            {
                _logger.Log("Parallel authoritative write tick did not run because no child-thread work item was queued.");
                return;
            }

            if (parallelWorkReport.WorkItemCount == 0)
            {
                _logger.Log("Parallel authoritative write tick ran 0 child-thread work items.");
                return;
            }

            string message = string.Format(
                "Parallel authoritative write tick ran {0} child-thread work item(s) on {1} child thread(s) in {2} total.",
                parallelWorkReport.WorkItemCount,
                parallelWorkReport.ThreadCount,
                LoggerUtil.FormatElapsedTime(parallelWorkReport.TotalElapsedTime));
            foreach (ParallelWorkThreadReport threadReport in parallelWorkReport.ThreadReports)
            {
                message += string.Format(
                    " [thread {0}: {1} work item(s), {2}]",
                    threadReport.threadID,
                    threadReport.workItemCount,
                    LoggerUtil.FormatElapsedTimeFromTicks(threadReport.elapsedTicks));
            }
            _logger.Log(message);
        }

        public void Tick(float deltaTime)
        {
            Configurations configurations = Configurations.GlobalConfigurations;

            // The parallelization work flow is here.

            // This runs in the main thread.
            this._childThreadWriteTickWorkItems.Clear();
            this._entitiesWithChildThreadWriteTick.Clear();
            foreach(ClientSideEntity entity in this._entitiesWithAuthority)
            {
                entity.WriteTick_MainThread(deltaTime); // main thread write tick
                if (entity.EnqueueWriteTick_ChildThread(deltaTime, this._childThreadWriteTickWorkItems))
                {
                    // Copy entity data from main entity data container into both read-only and write-only buffers.
                    entity.PrepareWriteTickEntityDataContainers();
                    // Gather the works for worker threads to run parallel later.
                    this._entitiesWithChildThreadWriteTick.Add(entity);
                }
            }

            bool commitChildThreadWriteTickResults = true;
            ParallelWorkReport parallelWorkReport = null;
            try
            {
                if (this._childThreadWriteTickWorkItems.Count > 0)
                {
                    // Run parallel work in worker threads. The main thread will block here to wait for all worker threads to finish.
                    ParallelWorkRunner.Run(this._childThreadWriteTickWorkItems, configurations.ParallelWriteTickWorkerThreadLimit, configurations.EnableParallelWriteTickLogging, out parallelWorkReport);
                }
            }
            catch
            {
                // Do not commit the data from write-only buffer into main entity data container if any exception was thrown during parallelization
                commitChildThreadWriteTickResults = false;
                throw;
            }
            finally
            {
                // Commit entity data from the write-only buffer into the main entity data container.
                foreach (ClientSideEntity entity in this._entitiesWithChildThreadWriteTick)
                {
                    entity.FinalizeWriteTick_ChildThread(commitChildThreadWriteTickResults);
                }
            }

            foreach (ClientSideEntity entity in this._entitiesWithChildThreadWriteTick)
            {
                // The read tick to read data out from entity data container and write the data into the game engine.
                // It uses the same read tick as the read tick to read entity data received from server to game engine for non-authoritative entities.
                entity.ReadTickAfterChildThreadWrite();
            }

            // Report, for debugging and verification.
            if (configurations.EnableParallelWriteTickLogging)
            {
                this.LogParallelWriteTickReport(parallelWorkReport);
            }

            // end of parallelization work flow


            // write entity data of authoritative entities to synchronize buffer
            // the data in this buffer will be sent to the server when the client receives "sync frame begin" packet from the server.
            foreach (ClientSideEntity entity in this._entitiesWithAuthority)
            {
                if (!configurations.EnableDirtyOnlySync || entity.EntityDataContainer.IsDirty)
                {
                    this.WriteEntityDataToSyncBuffer(entity);
                }
            }

            // Read tick for non-authoritative entities
            // Why this is called in every client frame rather than only in sync frame?
            // This is because, the entity state of the non-authoritative entities may be accidentially changed by external factors such as game engine's physics engine.
            // This read tick in every client frame is to make sure that the non-authoritative entities in the scene will always follow the data from the server in every client frame.
            foreach(ClientSideEntity entity in this._entitiesWithoutAuthority)
            {
                entity.ReadTick();
            }
        }

        public bool HasAuthority(uint runtimeID)
        {
            ClientSideEntity entity = this.GetEntity(runtimeID);
            return entity.HasAuthority;
        }

        public bool IsTemporaryAuthorityBackup(uint runtimeID)
        {
            ClientSideEntity entity = this.GetEntity(runtimeID);
            return entity.IsTemporaryAuthorityBackup;
        }

        internal bool CanBecomeBackupAuthorityFor(uint runtimeID)
        {
            ClientSideEntity entity;
            try
            {
                entity = this.GetEntity(runtimeID);
            }
            catch
            {
                return false;
            }
            if (entity.HasAuthority)
            {
                return false;
            }
            return true;
        }
    }
}
