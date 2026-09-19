using DedicatedServer.Demo.JumpingGame.EntityData;
using DedicatedServer.UnityFramework.ECS.Components;
using UnityEngine;

namespace DedicatedServer.Demo.JumpingGame.EntityComponent
{
    public class AvatarPresentationComponent : UnityEntityComponentBase
    {
        private const float JumpingPower_Minimum = 3.0f;
        private const float JumpingPower_Maximum = 20.0f;
        private AvatarJumpStateData _jumpStateData;
        private Transform _ownerTransform;
        private Transform _mainCameraAttachPoint;

        public override void Initialize()
        {
            base.Initialize();
            this._jumpStateData = this.GetEntityDataStore<AvatarJumpStateData>();
            this._ownerTransform = this.OwnerGameObject.transform;
            this._mainCameraAttachPoint = GameObject.Find("MainCameraAttachPoint").transform;
        }

        public override void WriteTick_MainThread(float deltaTime)
        {
            if (!this.OwnerGameObject.HasAuthority() || this.OwnerGameObject.IsTemporaryBackupAuthority())
            {
                return;
            }

            if (this._jumpStateData.IsChargingJump())
            {
                float jumpPowerRatio = this._jumpStateData.GetJumpPower() / JumpingPower_Maximum;
                this._ownerTransform.localScale = new Vector3(1.0f, JumpingPower_Minimum / JumpingPower_Maximum + jumpPowerRatio * 0.8f, 1.0f);
            }

            this._mainCameraAttachPoint.position = this._ownerTransform.position;
        }
    }
}
