using DedicatedServer.Framework.Networking;
using System;

namespace DedicatedServer.Framework.ECS
{
    public abstract class EntityDataStoreBase
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<EntityDataStoreBase>();

        private ushort _index;
        public ushort Index => this._index;
        private bool _isDirty;
        public bool IsDirty => this._isDirty;
        private DictionaryList<byte, INetworkValue> _networkValues;
        private EntityDataContainer _entityDataContainer;
        private readonly byte[] _writeBuffer;
        private readonly byte[] _readBuffer;

        public EntityDataStoreBase(ushort index)
        {
            this._entityDataContainer = null;
            this._index = index;
            this._networkValues = new DictionaryList<byte, INetworkValue>();
            this._writeBuffer = new byte[sizeof(byte) + sizeof(long)];
            this._readBuffer = new byte[sizeof(long)];
        }

        public void MarkDirty()
        {
            this._isDirty = true;
            this._entityDataContainer.MarkDirty();
        }

        public void CopyFrom(EntityDataStoreBase source)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (this.GetType() != source.GetType())
            {
                throw new InvalidOperationException("Cannot copy entity data stores of different types.");
            }
            if (this._networkValues.Count != source._networkValues.Count)
            {
                throw new InvalidOperationException("Cannot copy entity data stores with different numbers of network values.");
            }

            foreach (INetworkValue sourceNetworkValue in source._networkValues)
            {
                INetworkValue targetNetworkValue = this._networkValues.Get(sourceNetworkValue.Index);
                if (targetNetworkValue is null)
                {
                    throw new InvalidOperationException("Cannot copy entity data store because a network value is missing in the target data store.");
                }
                targetNetworkValue.CopyFrom(sourceNetworkValue);
            }

            this.CopyCustomStateFrom(source);
            this._isDirty = source._isDirty;
        }

        protected T AddNetworkValue<T>(T networkValue) where T : INetworkValue
        {
            this._networkValues.Add(networkValue.Index, networkValue);
            return networkValue;
        }

        protected virtual void CopyCustomStateFrom(EntityDataStoreBase source) { }

        public void WriteToPacket(SentPacket packet, bool writeFullData)
        {
            packet.WriteUInt16(this._index);
            int numberOfValueIndex = packet.NextWriteIndex;
            byte numberOfValue = 0;
            packet.WriteByte(numberOfValue);
            foreach(INetworkValue networkValue in this._networkValues)
            {
                if (networkValue.IsDirty || writeFullData)
                {
                    int length = networkValue.WriteToPacket(this._writeBuffer, 0);
                    packet.WriteBytes(this._writeBuffer, 0, length);
                    numberOfValue++;
                }
            }
            int finalWriteIndex = packet.NextWriteIndex;
            packet.SetNextWriteIndex(numberOfValueIndex);
            packet.WriteByte(numberOfValue);
            packet.SetNextWriteIndex(finalWriteIndex);
            this._isDirty = false;
        }

        public void ReadFromPacket(ReceivedPacket packet, bool markDirty)
        {
            byte numberOfValue = packet.ReadByte();
            for(byte i = 0; i < numberOfValue; i++)
            {
                byte valueIndex = packet.ReadByte();
                INetworkValue networkValue = this._networkValues.Get(valueIndex);
                packet.ReadBytes(this._readBuffer, 0, networkValue.SerializedByteCount);
                networkValue.ReadFromPacket(this._readBuffer, 0, markDirty);
                if(markDirty && !this._isDirty && networkValue.IsDirty)
                {
                    this._isDirty = true;
                }
            }
        }

        internal void SetEntityDataContainer(EntityDataContainer entityDataContainer)
        {
            _logger.Assert(this._entityDataContainer is null, "this._entityDataContainer is null");
            this._entityDataContainer = entityDataContainer;
        }
    }
}
