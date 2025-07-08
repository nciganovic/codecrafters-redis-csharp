using System.Net.Sockets;

namespace codecrafters_redis.src
{
    class RedisLeader : RedisServer
    {
        private string _leaderReplId;
        private int _leaderReplOffset = 0;
        private List<Socket> _replicas = new List<Socket>();
        private List<Socket> _inSyncReplicas = new List<Socket>();

        public RedisLeader(string? dir, string? dbName, int port) :
            base(dir, dbName, port, "leader")
        {
            _leaderReplId = _GenerateReplicationId();
        }

        public override void StartServer()
        {
            base.StartServer();
        }

        protected override void HandleSetCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            double? millisecondsToExpire = command.GetExpiry();

            var expiryArgAsDateTime = _DateAsString(millisecondsToExpire);
            var key = command.GetKey();
            var value = command.GetValue();
            _storage.Set(key, new StoredValue(value, expiryArgAsDateTime));

            _PropagateSetToReplicas(key, value);

            SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
        }

        protected override void HandleKeysCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var keysParams = command.arguments[1];
            if (keysParams == Constants.ASTERISK_CHAR.ToString())
            {
                _reader.ReadRDSFile();
                var keysArray = _reader.rdsDatabase.Keys.ToList();
                List<string> keyLengthsArray = new List<string>();
                foreach (var item in keysArray)
                {
                    keyLengthsArray.Add(item.Length.ToString());
                }
                var keysAndLengths = keyLengthsArray.Zip(keysArray, (len, key) => $"${len}\r\n{key}\r\n");
                string? responseMiddle = "";

                foreach (var i in keysAndLengths)
                {
                    responseMiddle = $"{responseMiddle}{i}";
                }

                SendResponse($"*{keysArray.Count()}\r\n{responseMiddle}", socket);
            }
            else
            {
                throw new Exception("Unsupported argument to keys");
            }
        }

        protected override string GenerateBulkStringForInfoComamnd() => $"role:master\r\nmaster_repl_offset:{_leaderReplOffset}\r\nmaster_replid:{_leaderReplId}";

        protected override void HandleReplconfCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var firstArg = command.arguments[1];

            if (firstArg == "listening-port")
            {
                _replicas.Add(socket);
                SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            }
            else if (firstArg == "capa")
            {
                SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            }
            else if (firstArg == "ACK")
            {
                int thisAckBytes = Int32.Parse(command.arguments[2]);

                if (thisAckBytes == _leaderReplOffset)
                {
                    _inSyncReplicas.Add(socket);
                }

                _leaderReplOffset += 37;
            }
            else
            {
                SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            }
        }

        protected override void HandlePsyncCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            SendResponse($"+FULLRESYNC {_leaderReplOffset} 0\r\n", socket);
            string emptyRDBFile = "UkVESVMwMDEx+glyZWRpcy12ZXIFNy4yLjD6CnJlZGlzLWJpdHPAQPoFY3RpbWXCbQi8ZfoIdXNlZC1tZW3CsMQQAPoIYW9mLWJhc2XAAP/wbjv+wP9aog==";
            byte[] encodedFile = Convert.FromBase64String(emptyRDBFile);
            SendResponse($"${encodedFile.Length}\r\n", socket);
            socket.Send(encodedFile);
        }

        protected override void HandlePingCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            SendResponse(ResponseHandler.SimpleResponse(Constants.PONG), socket);
        }

        protected override void HandleWaitCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            int minimumNoReplicas = Int32.Parse(command.arguments[1]);
            int timeoutFromCommand = Int32.Parse(command.arguments[2]);

            if (_replicas.Count == 0)
            {
                SendResponse($":{_replicas.Count}\r\n", socket);
                return;
            }

            if (_leaderReplOffset == 0)
            {
                Thread.Sleep(timeoutFromCommand);
                SendResponse($":{_replicas.Count}\r\n", socket);
                return;
            }

            foreach (var replica in _replicas)
            {
                replica.ReceiveTimeout = timeoutFromCommand;
                SendResponse(ResponseHandler.ArrayResponse(["REPLCONF", "GETACK", "*"]), replica);
            }

            Thread.Sleep(timeoutFromCommand);
            SendResponse($":{_inSyncReplicas.Count}\r\n", socket);
        }

        private void _PropagateSetToReplicas(string key, string value)
        {
            var setCommand = ResponseHandler.ArrayResponse(["SET", key, value]);
            foreach (var replica in _replicas)
            {
                SendResponse(setCommand, replica);
            }
            // If a leader sends a set command to a replica, we track the bytes sent. 
            _leaderReplOffset += setCommand.Length;
        }

        private string _GenerateReplicationId()
        {
            Random res = new Random();

            string alphaNumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";

            string replicationId = "";

            for (int i = 0; i < 40; i++)
            {
                int x = res.Next(alphaNumeric.Length);
                replicationId = replicationId + alphaNumeric[x];
            }

            return replicationId;
        }

    }
}
