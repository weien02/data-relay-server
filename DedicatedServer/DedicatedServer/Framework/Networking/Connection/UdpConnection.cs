using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace DedicatedServer.Framework.Networking.Connection
{
    // I know this is not reliable.
    // But since a reliable networking channel is not the scope of this project, and UDP is easier in mearsuring network traffic with a program locally.
    // Let's assume this is reliable.
    public class UdpConnection : GameNetworkingConnectionBase
    {
        public const int ConnectionID_Connecting = -1;
        private UdpClient _udpClient;
        private byte[] _sendBuffer;
        private IPEndPoint _remoteEndPoint;
        private readonly int _connectionID;

        public UdpConnection(int connectionID, UdpClient udpClient, IPEndPoint remoteEndPoint)
        {
            this._connectionID = connectionID;
            this._udpClient = udpClient;
            this._sendBuffer = new byte[Packet.PacketLength_Maximum + sizeof(int)];
            this._remoteEndPoint = remoteEndPoint;
            BitConverter.TryWriteBytes(this._sendBuffer, this._connectionID);
        }

        public override void Tick(float deltaTime)
        {
            
        }

        public override void Dispose()
        {

        }

        protected override void SendData(byte[] data, int startIndex, int length)
        {
            Array.Copy(data, startIndex, this._sendBuffer, sizeof(int), length);
            this._udpClient.Send(this._sendBuffer, sizeof(int) + length, this._remoteEndPoint);
        }

        public void ReceiveData(byte[] buffer, int startIndex, int length)
        {
            Array.Copy(buffer, startIndex, this.receiveBuffer, 0, length);
            this.EnqueueReceivedPacket(0, length);
        }
    }

    public class UdpListener : GameNetworkingListenerBase
    {
        private ushort _port;
        private UdpClient _udpClient;
        private readonly DictionaryList<int, UdpConnection> _clients;

        public UdpListener()
        {
            this._clients = new DictionaryList<int, UdpConnection>();
        }

        public override void Listen(ushort port)
        {
            this._port = port;
            this._udpClient = new UdpClient(this._port);
        }

        public override void Tick(float deltaTime)
        {
            if(this._udpClient is null)
            {
                return;
            }
            while(this._udpClient.Available > 0)
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, this._port);
                byte[] data = null;
                try
                {
                    data = this._udpClient.Receive(ref remoteEndPoint);
                }
                catch
                {
                    continue;
                }
                int connectionID = BitConverter.ToInt32(data);
                if(connectionID == UdpConnection.ConnectionID_Connecting)
                {
                    int allocatedConnectionID = this._clients.Count;
                    UdpConnection c = new UdpConnection(allocatedConnectionID, this._udpClient, remoteEndPoint);
                    this._clients.Add(allocatedConnectionID, c);
                    byte[] buffer = new byte[sizeof(int) + sizeof(int)];
                    BitConverter.TryWriteBytes(buffer, UdpConnection.ConnectionID_Connecting);
                    BitConverter.TryWriteBytes(new Span<byte>(buffer, sizeof(int), sizeof(int)), allocatedConnectionID);
                    this._udpClient.Send(buffer, buffer.Length, remoteEndPoint);
                    this.EnqueueNewClient(c);
                    continue;
                }
                UdpConnection client = this._clients.Get(connectionID);
                client.ReceiveData(data, sizeof(int), data.Length - sizeof(int));
            }
        }

        public override void Dispose()
        {
            this._udpClient?.Dispose();
            foreach(UdpConnection client in this._clients)
            {
                client.Dispose();
            }
        }
    }

    public class UdpConnector : GameNetworkingConnectorBase
    {
        private static readonly LoggerUtil _logger = LoggerUtil.GetLogger<UdpConnector>();
        private UdpClient _udpClient;
        private int _connectionID;
        private UdpConnection _connection;
        private IPEndPoint _remoteEndPoint;

        public override void Connect(IPEndPoint remoteEndPoint)
        {
            this._udpClient = new UdpClient();
            this._remoteEndPoint = remoteEndPoint;
            this._udpClient.Send(BitConverter.GetBytes(UdpConnection.ConnectionID_Connecting), sizeof(int), this._remoteEndPoint);
            this._connectionID = UdpConnection.ConnectionID_Connecting;
        }

        public override void Tick(float deltaTime)
        {
            if(this._udpClient is null)
            {
                return;
            }
            while(this._udpClient.Available > 0)
            {
                byte[] data = this._udpClient.Receive(ref this._remoteEndPoint);
                int connectionID = BitConverter.ToInt32(data);
                if(connectionID != this._connectionID)
                {
                    _logger.LogWarning("unknown connection id " + connectionID);
                    continue;
                }
                if(connectionID == UdpConnection.ConnectionID_Connecting)
                {
                    int allocatedConnectionID = BitConverter.ToInt32(data, sizeof(int));
                    this._connectionID = allocatedConnectionID;
                    this._connection = new UdpConnection(this._connectionID, this._udpClient, this._remoteEndPoint);
                    this.onConnected(this._connection);
                    continue;
                }
                this._connection.ReceiveData(data, sizeof(int), data.Length - sizeof(int));
            }
        }

        public override void Dispose()
        {
            this._udpClient?.Dispose();
            this._connection?.Dispose();
        }
    }
}
