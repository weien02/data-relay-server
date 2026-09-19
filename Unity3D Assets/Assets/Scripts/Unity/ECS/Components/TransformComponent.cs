using DedicatedServer.UnityFramework.ECS.Data;
using UnityEngine;

namespace DedicatedServer.UnityFramework.ECS.Components
{
    public class TransformComponent : UnityEntityComponentBase
    {
        private TransformData _transformData;
        private Transform _ownerTransform;
        private Vector3 _capturedPosition;
        private Vector3 _capturedRotationEuler;
        private Vector3 _capturedScale;
        public TransformComponent() { }

        public override bool HasChildThreadWriteTick => true;

        public override void Initialize()
        {
            base.Initialize();
            this._transformData = this.GetEntityDataStore<TransformData>();
            this._ownerTransform = this.OwnerGameObject.transform;
        }

        public override void ReadTick()
        {
            TransformSynchronizationUtility.ApplyToTransform(this._transformData, this._ownerTransform);
        }

        public override void WriteTick_MainThread(float deltaTime)
        {
            this._capturedPosition = this._ownerTransform.position;
            this._capturedRotationEuler = this._ownerTransform.rotation.eulerAngles;
            this._capturedScale = this._ownerTransform.localScale;
        }

        public override void WriteTick_ChildThread(float deltaTime)
        {
            TransformData transformData = this.GetWriteOnlyEntityDataStore<TransformData>();
            TransformSynchronizationUtility.WriteToDataStore(transformData, this._capturedPosition, this._capturedRotationEuler, this._capturedScale);
        }
    }
}
