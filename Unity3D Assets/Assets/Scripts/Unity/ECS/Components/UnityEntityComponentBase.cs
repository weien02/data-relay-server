using DedicatedServer.Framework.Client.ECS;
using UnityEngine;

namespace DedicatedServer.UnityFramework.ECS.Components
{
    public abstract class UnityEntityComponentBase : EntityComponentBase
    {
        public const string GameObjectName_UnityEntityManager = "EntityManager";
        private UnityEntity _ownerGameObject;
        protected UnityEntity OwnerGameObject => this._ownerGameObject;

        public override void Initialize()
        {
            this._ownerGameObject = GameObject.Find(UnityEntityComponentBase.GameObjectName_UnityEntityManager).GetComponent<UnityEntityManager>().GetEntity(this.OwnerRuntimeID);
        }

        public override void ReadTick() { }
    }
}
