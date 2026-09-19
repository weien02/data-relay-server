using System;

namespace DedicatedServer.Framework.ECS
{
    public interface INetworkValue
    {
        byte Index { get; }
        bool IsDirty { get; }
        int SerializedByteCount { get; }
        void CopyFrom(INetworkValue source);
        int WriteToPacket(byte[] buffer, int startIndex);
        void ReadFromPacket(byte[] buffer, int startIndex, bool markDirty);
    }

    public abstract class NetworkValue<T> : INetworkValue where T : struct
    {
        private readonly byte _index;
        public byte Index => this._index;
        protected T value;
        private bool _isDirty;
        public bool IsDirty => this._isDirty;
        public abstract int SerializedByteCount { get; }
        private EntityDataStoreBase _entityDataStoreBase;

        public NetworkValue(byte index, EntityDataStoreBase entityDataStoreBase)
        {
            this._index = index;
            this._entityDataStoreBase = entityDataStoreBase;
            this.value = default(T);
            this._isDirty = false;
        }

        public T Get()
        {
            return this.value;
        }

        public void Set(T val)
        {
            if(this.value.Equals(val))
            {
                return;
            }
            this.value = val;
            this._isDirty = true;
            this._entityDataStoreBase.MarkDirty();
        }

        public void CopyFrom(INetworkValue source)
        {
            NetworkValue<T> typedSource = source as NetworkValue<T>;
            if (typedSource is null)
            {
                throw new InvalidOperationException("Cannot copy network value from a different network value type.");
            }

            this.value = typedSource.value;
            this._isDirty = typedSource._isDirty;
        }

        protected abstract void SerializeToByteArray(byte[] array, int startIndex);

        public int WriteToPacket(byte[] buffer, int index)
        {
            buffer[index] = this._index;
            this.SerializeToByteArray(buffer, index + 1);
            return 1 + this.SerializedByteCount;
        }

        protected abstract void DeserializeFromByteArray(byte[] array, int startIndex);

        public void ReadFromPacket(byte[] buffer, int startIndex, bool markDirty)
        {
            T originalValue = this.value;
            this.DeserializeFromByteArray(buffer, startIndex);
            if(markDirty && !this.value.Equals(originalValue))
            {
                this._isDirty = true;
            }
        }
    }

    public class NetworkBoolean : NetworkValue<bool>
    {
        public override int SerializedByteCount => sizeof(bool);

        public NetworkBoolean(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            array[startIndex] = (byte)(this.Get() ? 1 : 0);
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = array[startIndex] == 0 ? false : true;
        }
    }

    public class NetworkByte : NetworkValue<byte>
    {
        public override int SerializedByteCount => sizeof(byte);

        public NetworkByte(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            array[startIndex] = this.Get();
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = array[startIndex];
        }
    }

    public class NetworkChar : NetworkValue<char>
    {
        public override int SerializedByteCount => sizeof(char);

        public NetworkChar(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            array[startIndex] = (byte)this.Get();
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = (char)array[startIndex];
        }
    }

    public class NetworkInt16 : NetworkValue<short>
    {
        public override int SerializedByteCount => sizeof(short);

        public NetworkInt16(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(short)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToInt16(array, startIndex);
        }
    }

    public class NetworkUInt16 : NetworkValue<ushort>
    {
        public override int SerializedByteCount => sizeof(ushort);

        public NetworkUInt16(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(ushort)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToUInt16(array, startIndex);
        }
    }

    public class NetworkInt32 : NetworkValue<int>
    {
        public override int SerializedByteCount => sizeof(int);

        public NetworkInt32(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(int)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToInt32(array, startIndex);
        }
    }

    public class NetworkUInt32 : NetworkValue<uint>
    {
        public override int SerializedByteCount => sizeof(uint);

        public NetworkUInt32(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(uint)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToUInt32(array, startIndex);
        }
    }

    public class NetworkInt64 : NetworkValue<long>
    {
        public override int SerializedByteCount => sizeof(long);

        public NetworkInt64(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(long)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToInt64(array, startIndex);
        }
    }

    public class NetworkUInt64 : NetworkValue<ulong>
    {
        public override int SerializedByteCount => sizeof(ulong);

        public NetworkUInt64(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(ulong)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToUInt64(array, startIndex);
        }
    }

    public class NetworkSingle : NetworkValue<float>
    {
        public override int SerializedByteCount => sizeof(float);

        public NetworkSingle(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(float)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToSingle(array, startIndex);
        }
    }

    public class NetworkDouble : NetworkValue<double>
    {
        public override int SerializedByteCount => sizeof(double);

        public NetworkDouble(byte index, EntityDataStoreBase entityDataStoreBase) : base(index, entityDataStoreBase) { }

        protected override void SerializeToByteArray(byte[] array, int startIndex)
        {
            BitConverter.TryWriteBytes(new Span<byte>(array, startIndex, sizeof(double)), this.Get());
        }

        protected override void DeserializeFromByteArray(byte[] array, int startIndex)
        {
            this.value = BitConverter.ToDouble(array, startIndex);
        }
    }
}
