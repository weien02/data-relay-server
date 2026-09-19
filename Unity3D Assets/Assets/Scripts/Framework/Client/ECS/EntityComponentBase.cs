using DedicatedServer.Framework.ECS;
using System;

namespace DedicatedServer.Framework.Client.ECS
{
    public abstract class EntityComponentBase
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<EntityComponentBase>();
        private ClientSideEntity _owner;
        private EntityDataContainer _readOnlyWriteTickEntityDataContainer;
        private EntityDataContainer _writeOnlyWriteTickEntityDataContainer;
        public uint OwnerRuntimeID => this._owner.RuntimeID;

        public EntityComponentBase()
        {
            this._owner = null;
            this._readOnlyWriteTickEntityDataContainer = null;
            this._writeOnlyWriteTickEntityDataContainer = null;
        }

        internal void SetOwner(ClientSideEntity owner)
        {
            _logger.Assert(this._owner is null, "this._owner is null");
            this._owner = owner;
        }

        protected T GetEntityDataStore<T>() where T : EntityDataStoreBase
        {
            return this._owner.EntityDataContainer.GetEntityDataStore<T>();
        }

        protected T GetReadOnlyEntityDataStore<T>() where T : EntityDataStoreBase
        {
            return this._readOnlyWriteTickEntityDataContainer.GetEntityDataStore<T>();
        }

        protected T GetWriteOnlyEntityDataStore<T>() where T : EntityDataStoreBase
        {
            return this._writeOnlyWriteTickEntityDataContainer.GetEntityDataStore<T>();
        }

        public virtual bool HasChildThreadWriteTick => false;

        /// <summary>
        /// The developers need to implement this. This can be used to initialize the initial values of entity data.
        /// </summary>
        public abstract void Initialize();

        /// <summary>
        /// The developers need to implement this.
        /// </summary>
        public abstract void ReadTick();

        /// <summary>
        /// The developers need to implement this.
        /// </summary>
        /// <param name="deltaTime"></param>
        public virtual void WriteTick_MainThread(float deltaTime) { }

        /// <summary>
        /// The developers need to implement this.
        /// </summary>
        /// <param name="deltaTime"></param>
        public virtual void WriteTick_ChildThread(float deltaTime) { }

        internal void SetWriteTickEntityDataContainers(EntityDataContainer readOnlyWriteTickEntityDataContainer, EntityDataContainer writeOnlyWriteTickEntityDataContainer)
        {
            this._readOnlyWriteTickEntityDataContainer = readOnlyWriteTickEntityDataContainer;
            this._writeOnlyWriteTickEntityDataContainer = writeOnlyWriteTickEntityDataContainer;
        }

        internal void DereferenceWriteTickEntityDataContainers()
        {
            this._readOnlyWriteTickEntityDataContainer = null;
            this._writeOnlyWriteTickEntityDataContainer = null;
        }
    }
}
