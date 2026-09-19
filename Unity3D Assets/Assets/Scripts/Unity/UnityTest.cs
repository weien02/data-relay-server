using DedicatedServer.Framework;
using DedicatedServer.Framework.Client;
using DedicatedServer.Framework.Networking;
using DedicatedServer.Framework.Server;
using DedicatedServer.UnityFramework;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A scripts for testing networking connection in a very very early stage of development.
/// </summary>
public class UnityTest : MonoBehaviour
{
    private static LoggerUtil _logger = LoggerUtil.GetLogger<UnityTest>();
    private GameServer _server;
    private GameClient _client;

    // Start is called before the first frame update
    void Awake()
    {
        LoggerUtil.SetOutputMethod(UnityLogger.OutputMethod);

        _logger.Log("Awake");

        /*this._server = new GameServer();
        this._client = new GameClient();

        this._server.StartServer(5000);
        this._client.Connect("127.0.0.1", 5000);*/

        /*using(SentPacket packet = PacketPool.GetSentPacket())
        {
            packet.WriteString("test");
            packet.WriteInt32(0);
            this._client.Send(packet);
        }*/
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) && this._server is null)
        {
            this._server = new GameServer();
            this._server.StartServer(5000);
        }
        if(Input.GetKeyDown(KeyCode.Alpha2) && this._client is null)
        {
            this._client = new GameClient(0);
            this._client.Connect("127.0.0.1", 5000);
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            using(SentPacket packet = PacketPool.GetSentPacket())
            {
                packet.WriteString("client to server test");
                packet.WriteInt32(0);
                this._client.SendPacketToServer(packet);
            }
        }
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            using(SentPacket packet = PacketPool.GetSentPacket())
            {
                packet.WriteString("server to client test");
                packet.WriteInt32(0);
                this._server.Test_SendToFirstClient(packet);
            }
        }
        float deltaTime = Time.deltaTime;
        this._server?.Tick(deltaTime);
        this._client?.Tick(deltaTime);
        if(!(this._client is null))
        {
            ReceivedPacket packet = this._client.GetNextReceivedPacket();
            if(!(packet is null))
            {
                using (packet)
                {
                    _logger.Log("client: " + packet.ReadString() + packet.ReadInt32());
                }
            }
        }
        if (!(this._server is null))
        {
            ReceivedPacket packet = this._server.GetNextReceivedPacket();
            if (!(packet is null))
            {
                using (packet)
                {
                    _logger.Log("server: " + packet.ReadString() + packet.ReadInt32());
                }
            }
        }
    }

    private void OnDestroy()
    {
        _logger.Log("OnDestroy");
        _logger.LogException(() => this._server.Dispose());
        _logger.LogException(() => this._client.Dispose());
        _logger.Log("OnDestroy done");
    }
}
