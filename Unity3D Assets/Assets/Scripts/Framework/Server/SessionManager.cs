using DedicatedServer.Framework.Networking;
using System;
using System.Collections;
using System.Collections.Generic;

namespace DedicatedServer.Framework.Server
{
    using PlayerID = Byte;
    using SessionID = UInt16;
    using ClientID = UInt32;

    internal class Session
    {
        private GameNetworkingConnectionBase _connection;
        public readonly SessionID sessionID;
        public readonly ClientID clientID;
        public readonly PlayerID playerID;
        public uint lastHeartbeatFrameNumber;
        private bool _isConnected;
        public bool IsConnected => this._isConnected;

        public Session(SessionID sessionID, ClientID clientID, PlayerID playerID)
        {
            this.sessionID = sessionID;
            this.clientID = clientID;
            this.playerID = playerID;
            this._isConnected = false;
        }

        public void Connect(GameNetworkingConnectionBase connection, uint syncFrameNumber)
        {
            this._connection = connection;
            this._isConnected = true;
            this.lastHeartbeatFrameNumber = syncFrameNumber;
        }

        public bool QueuedSendPacket(SentPacket packet)
        {
            if (!this._isConnected)
            {
                return false;
            }
            this._connection.QueuedSendPacket(packet);
            return true;
        }

        public void Disconnect()
        {
            this._isConnected = false;
        }
    }

    internal class SessionManager {

        private class SessionIDAllocator
        {
            private ushort _number;

            public SessionIDAllocator()
            {
                this._number = 0;
            }

            public SessionID Allocate()
            {
                ushort result = this._number;
                this._number++;
                return result;
            }
        }

        private readonly DictionaryList<SessionID, Session> _sessions;
        private readonly SessionIDAllocator _sessionIDAllocator;

        internal SessionManager()
        {
            this._sessions = new DictionaryList<SessionID, Session>();
            this._sessionIDAllocator = new SessionIDAllocator();
        }

        public ushort StartSession(GameNetworkingConnectionBase connection, ClientID clientID, PlayerID playerID, uint syncFrameNumber)
        {
            SessionID sessionID = this._sessionIDAllocator.Allocate();
            Session session = new Session(sessionID, clientID, playerID);
            session.Connect(connection, syncFrameNumber);
            this._sessions.Add(sessionID, session);
            return sessionID;
        }

        public Session GetSession(SessionID sessionID)
        {
            return this._sessions.Get(sessionID);
        }

        public PlayerID GetPlayerID(SessionID sessionID)
        {
            return this.GetSession(sessionID).playerID;
        }

        internal IEnumerator<Session> GetSessions()
        {
            return this._sessions.GetGenericEnumerator();
        }

        public bool GetSessionID(ClientID clientID, out SessionID sessionID)
        {
            foreach(Session session in this._sessions)
            {
                if(session.clientID == clientID)
                {
                    sessionID = session.sessionID;
                    return true;
                }
            }
            sessionID = 0;
            return false;
        }

        public void ReconnectSession(SessionID sessionID, GameNetworkingConnectionBase connection, uint syncFrameNumber, out PlayerID playerID)
        {
            Session session = this.GetSession(sessionID);
            session.Connect(connection, syncFrameNumber);
            playerID = session.playerID;
        }
    }
}
