using DedicatedServer.UnityFramework.ECS.Components;

namespace DedicatedServer.Demo.JumpingGame
{
    /// <summary>
    /// This component is for delete a player avatar entity if:
    ///   - the authoritative client is a backup client for the player avatar entity
    ///   - the player that owns the player avatar is disconnected for 30 seconds without reconnecting.
    /// </summary>
    public class AvatarBackupCleanupComponent : UnityEntityComponentBase
    {
        private const float DeleteEntityDelaySeconds_TemporaryBackup = 30.0f;

        private float _temporaryBackupDeleteTimer;
        private bool _hasRequestedDeletionAsTemporaryBackup;

        public override void Initialize()
        {
            base.Initialize();
            this._temporaryBackupDeleteTimer = 0.0f;
            this._hasRequestedDeletionAsTemporaryBackup = false;
        }

        public override void WriteTick_MainThread(float deltaTime)
        {
            if (!this.OwnerGameObject.IsTemporaryBackupAuthority())
            {
                this._temporaryBackupDeleteTimer = 0.0f;
                this._hasRequestedDeletionAsTemporaryBackup = false;
                return;
            }

            this._temporaryBackupDeleteTimer += deltaTime;
            if (!this._hasRequestedDeletionAsTemporaryBackup && this._temporaryBackupDeleteTimer >= DeleteEntityDelaySeconds_TemporaryBackup)
            {
                this._hasRequestedDeletionAsTemporaryBackup = true;
                this.OwnerGameObject.RequestDeleteNetworkEntity();
            }
        }
    }
}
