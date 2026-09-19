using DedicatedServer.Framework.ECS;

namespace DedicatedServer.Demo.JumpingGame.EntityData
{
    public class AvatarJumpStateData : EntityDataStoreBase
    {
        private const ushort DataStoreIndex_AvatarJumpState = 1000;

        private readonly NetworkSingle _jumpPower;
        private readonly NetworkBoolean _isChargingJump;

        public AvatarJumpStateData() : base(DataStoreIndex_AvatarJumpState)
        {
            this._jumpPower = this.AddNetworkValue(new NetworkSingle(0, this));
            this._isChargingJump = this.AddNetworkValue(new NetworkBoolean(1, this));
        }

        public float GetJumpPower()
        {
            return this._jumpPower.Get();
        }

        public void SetJumpPower(float jumpPower)
        {
            this._jumpPower.Set(jumpPower);
        }

        public bool IsChargingJump()
        {
            return this._isChargingJump.Get();
        }

        public void SetIsChargingJump(bool isChargingJump)
        {
            this._isChargingJump.Set(isChargingJump);
        }
    }
}
