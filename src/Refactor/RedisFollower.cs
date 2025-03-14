using System.Net.Sockets;
using System.Net;

namespace codecrafters_redis.src
{
    class RedisFollower : RedisServer
    {
        private string _leaderHost;
        private int _leaderPort;
        private IPEndPoint _leaderIPEndpoint;
        private int _followerOffset = 0;


        public RedisFollower(string? dir, string? dbName, int port, string leaderHost, int leaderPort) :
            base(dir, dbName, port, "follower")
        {
            _leaderHost = leaderHost;
            _leaderPort = leaderPort;
            _leaderIPEndpoint = _GetIPEndPoint(leaderHost, leaderPort);
        }

        public override void StartServer()
        {
            var handshakeThread = new Thread(() => _SendHandshakeToMaster());
            handshakeThread.Start();

            base.StartServer();
        }

        protected override void HandleSetCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            double? millisecondsToExpire = command.GetExpiry();

            var expiryArgAsDateTime = _DateAsString(millisecondsToExpire);
            var key = command.GetKey();
            var value = command.GetValue();
            _storage.Set(key, new StoredValue(value, expiryArgAsDateTime));


            if (_CheckIfConnectionIsFromLeader(socket))
            {
                //if a slave recieves a set from a master 
                // dont need a response but we track the number of bytes
                _followerOffset += command.bytes;
                return;
            }

            SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
        }

        protected override void HandleKeysCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            HandleUnrecognizedComamnd(socket);
        }

        protected override string GenerateBulkStringForInfoComamnd() => $"role:slave\r\n";

        protected override void HandleReplconfCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var firstArg = command.arguments[1];

            if (firstArg == "GETACK")
            {
                SendResponse(ResponseHandler.ArrayResponse(["REPLCONF", "ACK", _followerOffset.ToString()]), socket);

                if (_CheckIfConnectionIsFromLeader(socket))
                {
                    // we update the slave offset tracking after we send
                    _followerOffset += command.bytes;
                }
            }
            else if (firstArg == "capa")
            {
                SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            }
            else
            {
                SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            }
        }

        protected override void HandlePsyncCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            HandleUnrecognizedComamnd(socket);
        }

        protected override void HandlePingCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            if (_CheckIfConnectionIsFromLeader(socket))
            {
                //silently process ping if from master
                _followerOffset += command.bytes;
            }
            else
            {
                SendResponse(ResponseHandler.SimpleResponse(Constants.PONG), socket);
            }
        }

        protected override void HandleWaitCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            HandleUnrecognizedComamnd(socket);
        }

        private void _SendHandshakeToMaster()
        {
            using var client = new TcpClient();
            var endpoint = _leaderIPEndpoint;
            client.Connect(endpoint);
            using NetworkStream stream = client.GetStream();

            byte[] response = new byte[1024];

            // PING
            //var ping = "*1\r\n$4\r\nPING\r\n";
            SendResponse(ResponseHandler.ArrayResponse([Constants.PING]), stream.Socket);

            //+PONG\r\n
            stream.Read(response, 0, 7);

            //REPLCONF PART 1
            var listeningPort = _listeningPort.ToString();
            var listeningPortToSend = $"*3\r\n$8\r\nREPLCONF\r\n$14\r\nlistening-port\r\n${listeningPort.Length}\r\n{listeningPort}\r\n";
            SendResponse(ResponseHandler.ArrayResponse(["REPLCONF", "listening-port", listeningPort]), stream.Socket);

            //+OK\r\n
            stream.Read(response, 6, 5);

            //REPLCONF PART 2
            //var capabilities = "*3\r\n$8\r\nREPLCONF\r\n$4\r\ncapa\r\n$6\r\npsync2\r\n";
            SendResponse(ResponseHandler.ArrayResponse(["REPLCONF", "capa", "psync2"]), stream.Socket);

            //+OK\r\n
            stream.Read(response, 11, 5);

            //PSYNC
            //var psync = "*3\r\n$5\r\nPSYNC\r\n$1\r\n?\r\n$2\r\n-1\r\n";
            SendResponse(ResponseHandler.ArrayResponse(["PSYNC", "?", "-1"]), stream.Socket);

            //+FULLRESYNC 75cd7bc10c49047e0d163660f3b90625b1af31dc 0

            stream.Read(response, 16, 54);

            //file
            stream.Read(response, 70, 95);

            var setSocket = stream.Socket;

            _HandleClient(setSocket);
        }

        private bool _CheckIfConnectionIsFromLeader(Socket connection) => connection.RemoteEndPoint.GetHashCode() == _leaderIPEndpoint.GetHashCode();
    }
}
