using DedicatedServer.Framework.ECS;
#if UNITY_2017_1_OR_NEWER
using UnityEngine;
#endif

namespace DedicatedServer.UnityFramework.ECS.Data
{
    public class TransformData : EntityDataStoreBase
    {
        private readonly NetworkSingle _positionX;
        private readonly NetworkSingle _positionY;
        private readonly NetworkSingle _positionZ;
        private readonly NetworkSingle _rotationX;
        private readonly NetworkSingle _rotationY;
        private readonly NetworkSingle _rotationZ;
        private readonly NetworkSingle _scaleX;
        private readonly NetworkSingle _scaleY;
        private readonly NetworkSingle _scaleZ;

        public TransformData() : base((ushort)DataIndex.TransformData)
        {
            this._positionX = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(0, this));
            this._positionY = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(1, this));
            this._positionZ = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(2, this));
            this._rotationX = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(3, this));
            this._rotationY = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(4, this));
            this._rotationZ = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(5, this));
            this._scaleX = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(6, this));
            this._scaleY = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(7, this));
            this._scaleZ = this.AddNetworkValue<NetworkSingle>(new NetworkSingle(8, this));
        }

#if UNITY_2017_1_OR_NEWER
        public Vector3 GetPosition()
        {
            return new Vector3(this._positionX.Get(), this._positionY.Get(), this._positionZ.Get());
        }

        public void SetPosition(Vector3 position)
        {
            this._positionX.Set(position.x);
            this._positionY.Set(position.y);
            this._positionZ.Set(position.z);
        }

        public Vector3 GetRotation()
        {
            return new Vector3(this._rotationX.Get(), this._rotationY.Get(), this._rotationZ.Get());
        }

        public void SetRotation(Vector3 rotation)
        {
            this._rotationX.Set(rotation.x);
            this._rotationY.Set(rotation.y);
            this._rotationZ.Set(rotation.z);
        }

        public Vector3 GetScale()
        {
            return new Vector3(this._scaleX.Get(), this._scaleY.Get(), this._scaleZ.Get());
        }

        public void SetScale(Vector3 scale)
        {
            this._scaleX.Set(scale.x);
            this._scaleY.Set(scale.y);
            this._scaleZ.Set(scale.z);
        }
#endif
    }
}
