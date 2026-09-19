using DedicatedServer.Framework.ECS;
using DedicatedServer.Framework.Networking;
using System;
using System.Collections;
using System.Collections.Generic;


namespace DedicatedServer.Framework.Client.ECS
{
    using PlayerID = Byte;
    internal class ClientSideEntity : EntityBase
    {
        private readonly List<EntityComponentBase> _components;
        private readonly List<EntityComponentBase> _componentsWithChildThreadWriteTick;
        private EntityAuthorityType _authorityType;
        public bool HasAuthority => this._authorityType != EntityAuthorityType.NoAuthority;
        public bool IsTemporaryAuthorityBackup => this._authorityType == EntityAuthorityType.TemporaryBackup;
        private EntityDataContainer _readOnlyWriteTickEntityDataContainer;
        private EntityDataContainer _writeOnlyWriteTickEntityDataContainer;
        private bool _shouldReadTickAfterChildThreadWrite;

        private ClientSideEntity(int registryID, uint runtimeID, EntityAuthorityType authorityType) : base(registryID, runtimeID)
        {
            this._authorityType = authorityType;
            this._components = new List<EntityComponentBase>();
            this._componentsWithChildThreadWriteTick = new List<EntityComponentBase>();
            this._readOnlyWriteTickEntityDataContainer = null;
            this._writeOnlyWriteTickEntityDataContainer = null;
            this._shouldReadTickAfterChildThreadWrite = false;
            IEnumerator netComponents = EntityRegistry.GetNetComponents(registryID);
            while (netComponents.MoveNext())
            {
                EntityComponentBase component = (EntityComponentBase)Activator.CreateInstance((Type)netComponents.Current);
                this._components.Add(component);
                if (component.HasChildThreadWriteTick)
                {
                    this._componentsWithChildThreadWriteTick.Add(component);
                }
                component.SetOwner(this);
            }
        }

        internal void Initialize()
        {
            foreach(EntityComponentBase component in this._components)
            {
                component.Initialize();
            }
        }

        internal static ClientSideEntity FromCreationInformation(uint runtimeID, byte selfPlayerID, ReceivedPacket packet, byte[] staticCustomDataBuffer, out int staticCustomDataLength, out EntityAuthorityType entityAuthorityType)
        {
            int registryID = packet.ReadInt32();
            PlayerID authorityPlayerID = packet.ReadByte();
            EntityAuthorityType authorityType = (EntityAuthorityType)packet.ReadByte();
            bool hasAuthority = selfPlayerID == authorityPlayerID;
            authorityType = hasAuthority ? authorityType : EntityAuthorityType.NoAuthority;
            entityAuthorityType = authorityType;
            staticCustomDataLength = packet.ReadInt32();
            packet.ReadBytes(staticCustomDataBuffer, 0, staticCustomDataLength);
            return new ClientSideEntity(registryID, runtimeID, authorityType);
        }

        internal void ReadTick()
        {
            foreach(EntityComponentBase component in this._components)
            {
                component.ReadTick();
            }
        }

        internal void WriteTick_MainThread(float deltaTime)
        {
            foreach(EntityComponentBase component in this._components)
            {
                component.WriteTick_MainThread(deltaTime);
            }
        }

        public void PrepareWriteTickEntityDataContainers()
        {
            if (this._readOnlyWriteTickEntityDataContainer is null)
            {
                this._readOnlyWriteTickEntityDataContainer = this.EntityDataContainer.CreateCopy();
            }
            else
            {
                this._readOnlyWriteTickEntityDataContainer.CopyFrom(this.EntityDataContainer);
            }

            if (this._writeOnlyWriteTickEntityDataContainer is null)
            {
                this._writeOnlyWriteTickEntityDataContainer = this.EntityDataContainer.CreateCopy();
            }
            else
            {
                this._writeOnlyWriteTickEntityDataContainer.CopyFrom(this.EntityDataContainer);
            }
        }

        internal bool EnqueueWriteTick_ChildThread(float deltaTime, List<Action> workItems)
        {
            if (this._componentsWithChildThreadWriteTick.Count == 0)
            {
                return false;
            }
            foreach (EntityComponentBase component in this._componentsWithChildThreadWriteTick)
            {
                EntityComponentBase childThreadComponent = component;
                childThreadComponent.SetWriteTickEntityDataContainers(this._readOnlyWriteTickEntityDataContainer, this._writeOnlyWriteTickEntityDataContainer);
                workItems.Add(() => childThreadComponent.WriteTick_ChildThread(deltaTime));
            }
            return true;
        }

        internal void FinalizeWriteTick_ChildThread(bool commitResults)
        {
            if (this._writeOnlyWriteTickEntityDataContainer is null)
            {
                return;
            }

            // dereference the two buffers from entity components, because these two buffers should never be used by entity components outside parallelization logic
            foreach (EntityComponentBase component in this._componentsWithChildThreadWriteTick)
            {
                component.DereferenceWriteTickEntityDataContainers();
            }
            // copy data from the write-only buffer into the main entity data container (the 'commit' process)
            if (commitResults)
            {
                this.EntityDataContainer.CopyFrom(this._writeOnlyWriteTickEntityDataContainer);
                this._shouldReadTickAfterChildThreadWrite = true;
            }
        }

        internal void ReadTickAfterChildThreadWrite()
        {
            if (!this._shouldReadTickAfterChildThreadWrite)
            {
                return;
            }

            this.ReadTick();
            this._shouldReadTickAfterChildThreadWrite = false;
        }

        internal void SetAuthorityType(EntityAuthorityType authorityType)
        {
            this._authorityType = authorityType;
            this._readOnlyWriteTickEntityDataContainer = null;
            this._writeOnlyWriteTickEntityDataContainer = null;
            this._shouldReadTickAfterChildThreadWrite = false;
        }
    }
}
