using System;

namespace DedicatedServer.Framework.Server
{
    using ClientID = UInt32;
    using PlayerID = Byte;
    public class GameRoom
    {
        public const int PlayerCount_Maximum = byte.MaxValue;
        private byte _playerCount;
        private DictionaryList<PlayerID, ServersidePlayer> _players;

        public GameRoom()
        {
            this._players = new DictionaryList<PlayerID, ServersidePlayer>();
        }

        public PlayerID AddPlayer(ClientID clientID)
        {
            PlayerID playerID = this.GeneratePlayerID();
            ServersidePlayer player = new ServersidePlayer(clientID, playerID);
            this._players.Add(playerID, player);
            return playerID;
        }

        private PlayerID GeneratePlayerID()
        {
            if(this._playerCount + 1 > byte.MaxValue)
            {
                throw new Exception("Cannot generate player ID");
            }
            byte result = this._playerCount;
            this._playerCount++;
            return result;
        }
    }

    public class ServersidePlayer
    {
        public readonly ClientID clientID;
        public readonly PlayerID playerID;

        public ServersidePlayer(ClientID clientID, PlayerID playerID)
        {
            this.clientID = clientID;
            this.playerID = playerID;
        }
    }
}
