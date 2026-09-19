namespace DedicatedServer.Framework.ECS
{
    public abstract class EntityBase
    {
        private int _registryID;
        public int RegistryID => this._registryID;
        private uint _runtimeID;
        public uint RuntimeID => this._runtimeID;
        private EntityDataContainer _entityDataContainer;
        public EntityDataContainer EntityDataContainer => this._entityDataContainer;

        public EntityBase(int registryID, uint runtimeID)
        {
            this._registryID = registryID;
            this._runtimeID = runtimeID;
            this._entityDataContainer = EntityDataContainer.Create(EntityRegistry.GetDataStores(this._registryID));
        }

        public void SetRuntimeID(uint runtimeID)
        {
            this._runtimeID = runtimeID;
        }

        protected void SetRegistryID(int registryID)
        {
            this._registryID = registryID;
        }

        protected void ReleaseEntityDataContainer()
        {
            this._entityDataContainer = null;
        }
    }

    public enum EntityAuthorityType : byte
    {
        NoAuthority,
        Full_LocalPlayer,
        Full,
        TemporaryBackup,
    }
}
