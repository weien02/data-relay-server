using DedicatedServer.Demo.JumpingGame.EntityData;
using DedicatedServer.UnityFramework.ECS.Components;
using UnityEngine;

namespace DedicatedServer.Demo.JumpingGame.EntityComponent
{
    public class AvatarInputComponent : UnityEntityComponentBase
    {
        private const float JumpingPower_Minimum = 3.0f;
        private const float JumpingPower_Maximum = 20.0f;
        private const float JumpingPower_SecondsToReachMaximum = 2.0f;

        private AvatarJumpStateData _jumpStateData;

        public override void Initialize()
        {
            base.Initialize();
            this._jumpStateData = this.GetEntityDataStore<AvatarJumpStateData>();
        }

        public override void WriteTick_MainThread(float deltaTime)
        {
            if (!this.OwnerGameObject.HasAuthority() || this.OwnerGameObject.IsTemporaryBackupAuthority())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                this._jumpStateData.SetJumpPower(JumpingPower_Minimum);
                this._jumpStateData.SetIsChargingJump(true);
            }

            if (Input.GetMouseButton(0) && this._jumpStateData.IsChargingJump())
            {
                float nextJumpPower = this._jumpStateData.GetJumpPower() + deltaTime * (JumpingPower_Maximum - JumpingPower_Minimum) / JumpingPower_SecondsToReachMaximum;
                if (nextJumpPower > JumpingPower_Maximum)
                {
                    nextJumpPower = JumpingPower_Minimum;
                }
                this._jumpStateData.SetJumpPower(nextJumpPower);
            }

            if (Input.GetMouseButtonUp(0) && this._jumpStateData.IsChargingJump())
            {
                this.OwnerGameObject.Publish(new AvatarJumpReleasedEvent(this._jumpStateData.GetJumpPower()));
                this._jumpStateData.SetIsChargingJump(false);
            }
        }
    }
}
