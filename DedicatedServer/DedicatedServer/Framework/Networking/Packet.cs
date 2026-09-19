using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace DedicatedServer.Framework.Networking
{
    public abstract class Packet : IObjectPoolItem, IDisposable
    {
        public const int PacketLength_Maximum = 1024;

        private bool _isDisposed;
        protected bool IsDisposed => this._isDisposed;
        protected readonly byte[] data;
        private int _nextReadIndex;
        public int NextReadIndex => this._nextReadIndex;
        protected int length;
        public int Length => this.length;

        protected Packet()
        {
            this.data = new byte[PacketLength_Maximum];
            this._nextReadIndex = 0;
            this._isDisposed = true;
        }

        private void CheckCanRead(int length)
        {
            this.CheckIsNotDisposed();
            if(this._nextReadIndex + length - 1 >= this.length)
            {
                throw new IndexOutOfRangeException("Packet read index out of range: next read index = " + this._nextReadIndex + ", length = " + this.length);
            }
        }

        protected void CheckIsNotDisposed()
        {
            if (this._isDisposed)
            {
                throw new Exception("The packet is disposed");
            }
        }

        private void CheckIsDisposed()
        {
            if (!this._isDisposed)
            {
                throw new Exception("The packet is not disposed");
            }
        }

        protected void SetData(byte[] bytes, int startIndex, int length)
        {
            Array.Copy(bytes, startIndex, this.data, 0, length);
            this.length = length;
        }

        public byte ReadByte()
        {
            this.CheckCanRead(sizeof(byte));
            byte result = this.data[this._nextReadIndex];
            this._nextReadIndex += sizeof(byte);
            return result;
        }

        public void ReadBytes(byte[] buffer, int startIndex, int length)
        {
            this.CheckCanRead(sizeof(byte) * length);
            Array.Copy(this.data, this._nextReadIndex, buffer, startIndex, length);
            this._nextReadIndex += length;
        }

        public bool ReadBoolean()
        {
            return this.ReadByte() != 0;
        }

        public char ReadChar()
        {
            return (char)this.ReadByte();
        }

        public short ReadInt16()
        {
            this.CheckCanRead(sizeof(short));
            short result = BitConverter.ToInt16(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(short);
            return result;
        }

        public ushort ReadUInt16()
        {
            this.CheckCanRead(sizeof(ushort));
            ushort result = BitConverter.ToUInt16(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(ushort);
            return result;
        }

        public int ReadInt32()
        {
            this.CheckCanRead(sizeof(int));
            int result = BitConverter.ToInt32(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(int);
            return result;
        }

        public uint ReadUInt32()
        {
            this.CheckCanRead(sizeof(uint));
            uint result = BitConverter.ToUInt32(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(uint);
            return result;
        }

        public long ReadInt64()
        {
            this.CheckCanRead(sizeof(long));
            long result = BitConverter.ToInt64(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(long);
            return result;
        }

        public ulong ReadUInt64()
        {
            this.CheckCanRead(sizeof(ulong));
            ulong result = BitConverter.ToUInt64(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(ulong);
            return result;
        }

        public float ReadSingle()
        {
            this.CheckCanRead(sizeof(float));
            float result = BitConverter.ToSingle(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(float);
            return result;
        }

        public double ReadDouble()
        {
            this.CheckCanRead(sizeof(double));
            double result = BitConverter.ToDouble(this.data, this._nextReadIndex);
            this._nextReadIndex += sizeof(double);
            return result;
        }

        public string ReadString(bool utf8 = false)
        {
            int stringByteCount = this.ReadUInt16();
            this.CheckCanRead(stringByteCount);
            Encoding encoding = utf8 ? Encoding.UTF8 : Encoding.ASCII;
            string str = encoding.GetString(this.data, this._nextReadIndex, stringByteCount);
            this._nextReadIndex += stringByteCount;
            return str;
        }

        public void ChangeReadIndex(int amount)
        {
            this._nextReadIndex += amount;
        }

        //public abstract Packet CreateInstance();

        internal virtual void OnRecycled()
        {
            this.CheckIsDisposed();
            this._isDisposed = false;
            this.length = 0;
            this._nextReadIndex = 0;
        }

        public virtual void Dispose()
        {
            this.CheckIsNotDisposed();
            this._isDisposed = true;
        }
    }

    public sealed class ReceivedPacket : Packet
    {

        public ReceivedPacket() : base() { }

        internal void SetReceivedData(byte[] data, int startIndex, int length)
        {
            this.SetData(data, startIndex, length);
        }

        /*public override Packet CreateInstance()
        {
            return new ReceivedPacket();
        }*/

        public override void Dispose()
        {
            base.Dispose();
            PacketPool.DisposePacket(this);
        }
    }

    public sealed class SentPacket : Packet
    {
        private int _nextWriteIndex;
        public int NextWriteIndex => this._nextWriteIndex;
        private int _disposeAfterSendCount;

        public SentPacket() : base()
        {
            this._disposeAfterSendCount = int.MaxValue;
        }

        internal byte[] GetDataForSendingPacket()
        {
            this.CheckIsNotDisposed();
            this._disposeAfterSendCount--;
            if (this._disposeAfterSendCount <= 0)
            {
                this.DisposeImmediately();
            }
            return this.data;
        }

        private void CheckCanWrite(int length)
        {
            this.CheckIsNotDisposed();
            if(this._nextWriteIndex + length > Packet.PacketLength_Maximum)
            {
                throw new Exception("Trying to write too much data: next write index = " + this._nextWriteIndex + ", length = " + length + ", PacketLength_Maximum = " + Packet.PacketLength_Maximum);
            }
            this.length += length;
        }

        public void WriteByte(byte data)
        {
            this.CheckCanWrite(sizeof(byte));
            this.data[this._nextWriteIndex] = data;
            this._nextWriteIndex += sizeof(byte);
        }

        public void WriteBytes(byte[] data, int startIndex, int length)
        {
            this.CheckCanWrite(length);
            Array.Copy(data, startIndex, this.data, this._nextWriteIndex, length);
            this._nextWriteIndex += length;
        }

        public void WriteBoolean(bool data)
        {
            this.WriteByte((byte)(data ? 1 : 0));
        }

        public void WriteInt16(short data)
        {
            this.CheckCanWrite(sizeof(short));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(short)), data);
            this._nextWriteIndex += sizeof(short);
        }

        public void WriteUInt16(ushort data)
        {
            this.CheckCanWrite(sizeof(ushort));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(ushort)), data);
            this._nextWriteIndex += sizeof(ushort);
        }

        public void WriteInt32(int data)
        {
            this.CheckCanWrite(sizeof(int));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(int)), data);
            this._nextWriteIndex += sizeof(int);
        }

        public void WriteUInt32(uint data)
        {
            this.CheckCanWrite(sizeof(uint));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(uint)), data);
            this._nextWriteIndex += sizeof(uint);
        }

        public void WriteInt64(long data)
        {
            this.CheckCanWrite(sizeof(long));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(long)), data);
            this._nextWriteIndex += sizeof(long);
        }

        public void WriteSingle(float data)
        {
            this.CheckCanWrite(sizeof(float));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(float)), data);
            this._nextWriteIndex += sizeof(float);
        }

        public void WriteDouble(double data)
        {
            this.CheckCanWrite(sizeof(double));
            BitConverter.TryWriteBytes(new Span<byte>(this.data, this._nextWriteIndex, sizeof(double)), data);
            this._nextWriteIndex += sizeof(double);
        }

        public void WriteString(string data, bool utf8 = false)
        {
            Encoding encoding = utf8 ? Encoding.UTF8 : Encoding.ASCII;
            int stringByteCount = encoding.GetByteCount(data);
            if(stringByteCount > ushort.MaxValue)
            {
                throw new Exception("The string '" + data + "' is too long to write into the packet");
            }
            this.CheckCanWrite(sizeof(ushort) + stringByteCount);
            this.WriteUInt16((ushort)stringByteCount);
            encoding.GetBytes(data, 0, data.Length, this.data, this._nextWriteIndex);
            this._nextWriteIndex += stringByteCount;
        }

        public void SetNextWriteIndex(int index)
        {
            this._nextWriteIndex = index;
        }

        /*public override Packet CreateInstance()
        {
            return new SentPacket();
        }*/

        public override void Dispose()
        {
            if (this._disposeAfterSendCount <= 0)
            {
                this.DisposeImmediately();
            }
        }

        public void Dispose(int disposeAfterSendCount)
        {
            this._disposeAfterSendCount = disposeAfterSendCount;
        }

        public void DisposeImmediately()
        {
            base.Dispose();
            PacketPool.DisposePacket(this);
        }

        internal override void OnRecycled()
        {
            base.OnRecycled();
            this.SetNextWriteIndex(0);
            this._disposeAfterSendCount = int.MaxValue;
        }
    }

    public static class PacketPool
    {
        private static readonly ObjectPool<ReceivedPacket> _receivedPacketPool = new ObjectPool<ReceivedPacket>();
        private static readonly ObjectPool<SentPacket> _sentPacketPool = new ObjectPool<SentPacket>();

        public static ReceivedPacket GetReceivedPacket()
        {
            ReceivedPacket packet = _receivedPacketPool.Get();
            packet.OnRecycled();
            return packet;
        }

        public static SentPacket GetSentPacket()
        {
            SentPacket packet = _sentPacketPool.Get();
            packet.OnRecycled();
            return packet;
        }

        public static void DisposePacket(ReceivedPacket receivedPacket)
        {
            _receivedPacketPool.DisposeIntoObjectPool(receivedPacket);
        }

        public static void DisposePacket(SentPacket sentPacket)
        {
            _sentPacketPool.DisposeIntoObjectPool(sentPacket);
        }
    }
}
