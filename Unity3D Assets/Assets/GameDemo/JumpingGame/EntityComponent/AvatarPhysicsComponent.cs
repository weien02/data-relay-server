using DedicatedServer.Demo.JumpingGame.EntityData;
using DedicatedServer.UnityFramework;
using DedicatedServer.UnityFramework.ECS.Components;
using UnityEngine;

namespace DedicatedServer.Demo.JumpingGame.EntityComponent
{
    public class AvatarPhysicsComponent : UnityEntityComponentBase
    {
        private AvatarRespawnData _respawnData;
        private Rigidbody _rigidbody;
        private Transform _ownerTransform;

        public override void Initialize()
        {
            base.Initialize();
            this._respawnData = this.GetEntityDataStore<AvatarRespawnData>();
            this._rigidbody = this.OwnerGameObject.GetComponent<Rigidbody>();
            this._ownerTransform = this.OwnerGameObject.transform;
            this.OwnerGameObject.Subscribe<AvatarJumpReleasedEvent>(this.OnAvatarJumpReleased);
            this.OwnerGameObject.Subscribe<UnityTriggerEnterEvent>(this.OnTriggerEnter);
        }

        private void OnAvatarJumpReleased(AvatarJumpReleasedEvent evt)
        {
            if (!this.OwnerGameObject.HasAuthority() || this.OwnerGameObject.IsTemporaryBackupAuthority())
            {
                return;
            }

            this._rigidbody.velocity = new Vector3(evt.jumpPower, evt.jumpPower, 0.0f);
        }

        private void OnTriggerEnter(UnityTriggerEnterEvent evt)
        {
            if (!this.OwnerGameObject.HasAuthority())
            {
                return;
            }

            Collider other = evt.collider;
            if (other.gameObject.name == "PlatformTrigger")
            {
                this._respawnData.SetRevivePosition(other.transform.parent.position);
                this._rigidbody.velocity = Vector3.zero;
                return;
            }

            if (other.gameObject.name == "Void")
            {
                this._ownerTransform.position = this._respawnData.GetRevivePosition();
            }
        }
    }
}
