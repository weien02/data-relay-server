using System;
using System.Collections.Generic;
using System.Net;

namespace DedicatedServer.Framework.Networking
{
    public abstract class GameNetworkingConnectionBase : ITickable, IDisposable
    {
        public const int ReceiveBuffer_Length = 10240;

        protected readonly byte[] receiveBuffer;
        private readonly byte[] _sendBuffer;
        private readonly Queue<ReceivedPacket> _receivedPacketQueue;
        private readonly Queue<SentPacket> _sentPacketQueue;

        public GameNetworkingConnectionBase()
        {
            this.receiveBuffer = new byte[ReceiveBuffer_Length];
            this._sendBuffer = new byte[sizeof(int) + Packet.PacketLength_Maximum];
            this._receivedPacketQueue = new Queue<ReceivedPacket>();
            this._sentPacketQueue = new Queue<SentPacket>();
        }

        protected void EnqueueReceivedPacket(int startIndex, int length)
        {
            ReceivedPacket packet = PacketPool.GetReceivedPacket();
            packet.SetReceivedData(this.receiveBuffer, startIndex, length);
            this._receivedPacketQueue.Enqueue(packet);
        }

        public ReceivedPacket GetNextReceivedPacket()
        {
            ReceivedPacket packet;
            if(this._receivedPacketQueue.TryDequeue(out packet))
            {
                return packet;
            }
            return null;
        }

        protected abstract void SendData(byte[] data, int startIndex, int length);

        public void SendPacketImmediately(SentPacket packet)
        {
            byte[] packetData = packet.GetDataForSendingPacket();
            this.SendData(packetData, 0, sizeof(int) + packet.Length);
        }

        public void QueuedSendPacket(SentPacket packet)
        {
            this._sentPacketQueue.Enqueue(packet);
        }

        public void SendAllPacketsInQueue()
        {
            SentPacket packet;
            while(this._sentPacketQueue.TryDequeue(out packet))
            {
                this.SendPacketImmediately(packet);
            }
        }

        public abstract void Tick(float deltaTime);

        public abstract void Dispose();

        /*public void SetSessionID(int sessionID)
        {
            this._sessionID = sessionID;
        }*/
    }

    public abstract class GameNetworkingListenerBase : ITickable, IDisposable
    {
        private Queue<GameNetworkingConnectionBase> _newClientQueue;

        public GameNetworkingListenerBase()
        {
            this._newClientQueue = new Queue<GameNetworkingConnectionBase>();
        }

        public abstract void Listen(ushort port);

        protected void EnqueueNewClient(GameNetworkingConnectionBase client)
        {
            this._newClientQueue.Enqueue(client);
        }

        public GameNetworkingConnectionBase GetNextNewClient()
        {
            GameNetworkingConnectionBase client;
            if(this._newClientQueue.TryDequeue(out client))
            {
                return client;
            }
            return null;
        }

        public abstract void Tick(float deltaTime);

        public abstract void Dispose();
    }

    public abstract class GameNetworkingConnectorBase : ITickable, IDisposable
    {
        protected Action<GameNetworkingConnectionBase> onConnected;
        public Action<GameNetworkingConnectionBase> OnConnected { set { this.onConnected = value; } }

        public GameNetworkingConnectorBase()
        {
        }

        public abstract void Connect(IPEndPoint remoteEndPoint);

        public abstract void Tick(float deltaTime);

        public abstract void Dispose();
    }
}
