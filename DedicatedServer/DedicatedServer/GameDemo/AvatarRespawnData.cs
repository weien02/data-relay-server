using DedicatedServer.Framework.ECS;
#if UNITY_2017_1_OR_NEWER
using UnityEngine;
#endif

namespace DedicatedServer.Demo.JumpingGame
{
    public class AvatarRespawnData : EntityDataStoreBase
    {
        private const ushort DataStoreIndex_AvatarRespawn = 1001;

        private readonly NetworkSingle _revivePositionX;
        private readonly NetworkSingle _revivePositionY;
        private readonly NetworkSingle _revivePositionZ;

        public AvatarRespawnData() : base(DataStoreIndex_AvatarRespawn)
        {
            this._revivePositionX = this.AddNetworkValue(new NetworkSingle(0, this));
            this._revivePositionY = this.AddNetworkValue(new NetworkSingle(1, this));
            this._revivePositionZ = this.AddNetworkValue(new NetworkSingle(2, this));
        }

#if UNITY_2017_1_OR_NEWER
        public Vector3 GetRevivePosition()
        {
            return new Vector3(this._revivePositionX.Get(), this._revivePositionY.Get(), this._revivePositionZ.Get());
        }

        public void SetRevivePosition(Vector3 revivePosition)
        {
            this._revivePositionX.Set(revivePosition.x);
            this._revivePositionY.Set(revivePosition.y);
            this._revivePositionZ.Set(revivePosition.z);
        }
#endif
    }
}
