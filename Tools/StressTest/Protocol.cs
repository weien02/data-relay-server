using System;
using System.Buffers.Binary;

namespace StressTest
{
    /// <summary>
    /// Packet types sent from a client to the server.
    /// The values are the ordinals of DedicatedServer.Framework.Networking.ClientToServerPacketTypes
    /// and have to stay in step with it.
    /// </summary>
    internal enum C2S : ushort
    {
        LoginRequest = 0,
        CreateEntityWithAuthorityRequest = 1,
        DeleteEntityRequest = 2,
        AcquireLocalPlayerEntityAuthorityRequest = 3,
        SyncLostRequest = 4,
        SyncEntityData = 5,
        ClientFrameSyncHeartbeat = 6,
        EntityAuthorityBackupCandidateReply = 7,
        CustomPacket = 8,
    }

    /// <summary>
    /// Packet types sent from the server to a client.
    /// The values are the ordinals of DedicatedServer.Framework.Networking.ServerToClientPacketTypes
    /// and have to stay in step with it.
    /// </summary>
    internal enum S2C : ushort
    {
        LoginResponse = 0,
        SyncFrameBegin = 1,
        SyncLostResponseBegin = 2,
        SyncEntityCreation = 3,
        SyncEntityDeletion = 4,
        SyncEntityData = 5,
        AskForBackupEntityAuthority = 6,
        CustomPacket = 7,
    }

    internal enum AuthorityType : byte
    {
        NoAuthority = 0,
        Full_LocalPlayer = 1,
        Full = 2,
        TemporaryBackup = 3,
    }

    /// <summary>
    /// The data store indices registered for registry id 0 by the JumpingGame demo.
    /// </summary>
    internal static class DataStoreIndex
    {
        public const ushort Transform = 0;      // UnityFramework.ECS.Data.DataIndex.TransformData
        public const ushort AvatarJumpState = 1000;
        public const ushort AvatarRespawn = 1001;
    }

    internal static class Wire
    {
        public const int ConnectionID_Connecting = -1;
        /// <summary>Packet.PacketLength_Maximum, plus the connection id the transport prepends.</summary>
        public const int MaximumDatagramLength = 1024 + sizeof(int);
    }

    /// <summary>
    /// Builds a datagram in exactly the layout the server parses:
    ///     [connectionID : int32][sessionID : uint16][packetType : uint16][body]
    /// The server reads with BitConverter, which is little endian on every platform this
    /// project targets, so every field is written little endian explicitly.
    /// One writer is reused per virtual client, so sending never allocates.
    /// </summary>
    internal sealed class ByteWriter
    {
        private readonly byte[] _buffer;
        private int _position;

        public ByteWriter(int capacity)
        {
            this._buffer = new byte[capacity];
        }

        public byte[] Buffer => this._buffer;
        public int Length => this._position;

        public ByteWriter Begin(int connectionID, ushort sessionID, C2S packetType)
        {
            this._position = 0;
            this.WriteInt32(connectionID);
            this.WriteUInt16(sessionID);
            this.WriteUInt16((ushort)packetType);
            return this;
        }

        /// <summary>
        /// The connecting handshake is the one datagram with no packet body at all:
        /// four bytes holding UdpConnection.ConnectionID_Connecting.
        /// </summary>
        public ByteWriter BeginHandshake()
        {
            this._position = 0;
            this.WriteInt32(Wire.ConnectionID_Connecting);
            return this;
        }

        public void WriteByte(byte value)
        {
            this._buffer[this._position] = value;
            this._position += sizeof(byte);
        }

        public void WriteBoolean(bool value)
        {
            this.WriteByte((byte)(value ? 1 : 0));
        }

        public void WriteUInt16(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(this._buffer.AsSpan(this._position), value);
            this._position += sizeof(ushort);
        }

        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(this._buffer.AsSpan(this._position), value);
            this._position += sizeof(int);
        }

        public void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(this._buffer.AsSpan(this._position), value);
            this._position += sizeof(uint);
        }

        public void WriteSingle(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(this._buffer.AsSpan(this._position), value);
            this._position += sizeof(float);
        }

        /// <summary>
        /// A network value inside a data store is written as [valueIndex : byte][value].
        /// </summary>
        public void WriteNetworkSingle(byte valueIndex, float value)
        {
            this.WriteByte(valueIndex);
            this.WriteSingle(value);
        }

        public void WriteNetworkBoolean(byte valueIndex, bool value)
        {
            this.WriteByte(valueIndex);
            this.WriteBoolean(value);
        }
    }

    /// <summary>
    /// Reads a datagram received from the server. The layout is
    ///     [connectionID : int32][packetType : uint16][body]
    /// Note that the server over-sends by four bytes (GameNetworkingConnectionBase.SendPacketImmediately
    /// adds sizeof(int) to the length it hands to SendData), so a received datagram carries four
    /// trailing bytes past the end of the real packet. Everything here is parsed field by field and
    /// never from the datagram length, so those bytes are simply ignored.
    /// </summary>
    internal ref struct ByteReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public ByteReader(ReadOnlySpan<byte> data)
        {
            this._data = data;
            this._position = 0;
        }

        public int Remaining => this._data.Length - this._position;

        public byte ReadByte()
        {
            byte value = this._data[this._position];
            this._position += sizeof(byte);
            return value;
        }

        public bool ReadBoolean()
        {
            return this.ReadByte() != 0;
        }

        public ushort ReadUInt16()
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(this._data.Slice(this._position));
            this._position += sizeof(ushort);
            return value;
        }

        public int ReadInt32()
        {
            int value = BinaryPrimitives.ReadInt32LittleEndian(this._data.Slice(this._position));
            this._position += sizeof(int);
            return value;
        }

        public uint ReadUInt32()
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(this._data.Slice(this._position));
            this._position += sizeof(uint);
            return value;
        }

        public float ReadSingle()
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(this._data.Slice(this._position));
            this._position += sizeof(float);
            return value;
        }

        public void Skip(int byteCount)
        {
            this._position += byteCount;
        }
    }

    /// <summary>
    /// The state every virtual player publishes, as a pure function of (playerID, seq).
    ///
    /// Every value is chosen to be exactly representable as a 32 bit float (whole numbers and
    /// halves well under 2^24), so a value that survives the round trip through the server
    /// compares bit-exact against the value that was sent. That is what lets the verifier assert
    /// equality rather than "close enough", and it is what makes a mismatch unambiguous evidence
    /// of a real fault rather than float drift.
    ///
    /// Two things are deliberately encoded into the state itself:
    ///   - the sequence number, in AvatarJumpStateData.jumpPower, so any observed sample says
    ///     which of the owner's updates it came from;
    ///   - the owner's player id, in the position, so a sample that has been attributed to the
    ///     wrong entity can be detected from its contents alone.
    /// </summary>
    internal static class Sim
    {
        /// <summary>
        /// Sequence numbers start at 1. A freshly created server side entity holds all zeroes,
        /// so jumpPower == 0 unambiguously means "the owner's first update has not been applied
        /// yet" rather than a real sample.
        /// </summary>
        public const int FirstSequence = 1;

        public static float PositionX(byte playerID, int seq)
        {
            return playerID * 1000.0f + (seq % 512);
        }

        public static float PositionY(byte playerID, int seq)
        {
            return (seq % 97) * 0.5f;
        }

        public static float PositionZ(byte playerID, int seq)
        {
            return -(playerID * 1000.0f) - (seq % 337);
        }

        public static float RotationY(int seq)
        {
            return seq % 360;
        }

        public static float JumpPower(int seq)
        {
            return seq;
        }

        public static bool IsChargingJump(int seq)
        {
            return (seq & 1) == 0;
        }

        // The respawn position is written once, right after the entity is created, and never
        // again. Dirty-only sync means it is broadcast in exactly one sync frame, which makes it
        // the probe for "a value a client can never recover after missing it".
        public static float RevivePositionX(byte playerID)
        {
            return playerID;
        }

        public static float RevivePositionY(byte playerID)
        {
            return 42.0f;
        }

        public static float RevivePositionZ(byte playerID)
        {
            return -(float)playerID;
        }

        /// <summary>
        /// Recovers the player id a position claims to belong to. Used to detect a sample that
        /// arrived under the wrong entity: the offset term is always below 512 and the player
        /// term is a multiple of 1000, so the two never overlap.
        /// </summary>
        public static int OwnerFromPositionX(float positionX, int seq)
        {
            return (int)MathF.Round((positionX - (seq % 512)) / 1000.0f);
        }
    }
}
