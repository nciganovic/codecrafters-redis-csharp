using System.Globalization;
using System.Net.Sockets;
using System.Net;
using System.Text;
using static codecrafters_redis.src.RedisProtocolParser;

namespace codecrafters_redis.src
{
    interface IRedisServer
    {
        public void StartServer();
        protected void _HandleClient(Socket Socket) { }
        protected void _SendResponse(string response, Socket socket) { }
        protected string? _DateAsString(double? milliseconds) { return ""; }
        protected void _SyncStorageToRDSFile() { }
        protected IPEndPoint? _GetIPEndPoint() { return null; }

    }

    public abstract class RedisServer : IRedisServer
    {
        protected int _listeningPort;
        protected TcpListener _server;
        protected RedisDatabaseStored _storage;
        protected RedisStreamStorage _streamStorage;
        protected string? _directory;
        protected string? _dbfilename;
        protected RDSFileReader _reader;
        protected string _role;
        protected RedisTransactions _redisTransactions;
        protected Dictionary<string, List<string>> _redisList = new Dictionary<string, List<string>>();

        public RedisServer(string? dir, string? dbName, int port, string role)
        {
            _listeningPort = port;
            _server = new TcpListener(IPAddress.Any, port);
            _storage = new RedisDatabaseStored();
            _streamStorage = new RedisStreamStorage();
            _directory = dir;
            _dbfilename = dbName;
            _reader = new RDSFileReader(_directory + "/" + _dbfilename);
            _role = role;
            _redisTransactions = new RedisTransactions(this);
        }

        public virtual void StartServer()
        {
            _server.Start();

            if (_directory + _dbfilename != string.Empty)
            {
                _reader.ReadRDSFile();
                _SyncStorageToRDSFile();
            }

            while (true)
            {
                var socket = _server.AcceptSocket();
                var thread = new Thread(() => _HandleClient(socket));
                thread.Start();
            }
        }

        public void HandleCommand(RESPMessage command, Socket socket)
        {
            switch (command.command)
            {
                case "ECHO":
                    HandleEchoCommand(command, socket);
                    break;

                case "SET":
                    HandleSetCommand(command, socket);
                    break;

                case "GET":
                    HandleGetCommand(command, socket);
                    break;

                case "INCR":
                    HandleIncrementCommand(command, socket);
                    break;

                case "MULTI":
                    HandleMultiCommand(command, socket);
                    break;

                case "EXEC":
                    HandleExecCommand(command, socket);
                    break;

                case "DISCARD":
                    HandleDiscardCommand(command, socket);
                    break;

                case "CONFIG":
                    HandleConfigComamnd(command, socket);
                    break;

                case "KEYS":
                    HandleKeysCommand(command, socket);
                    break;

                case "INFO":
                    HandleInfoComamnd(command, socket);
                    break;

                case "REPLCONF":
                    HandleReplconfCommand(command, socket);
                    break;

                case "PSYNC":
                    HandlePsyncCommand(command, socket);
                    break;

                case "PING":
                    HandlePingCommand(command, socket);
                    break;

                case "WAIT":
                    HandleWaitCommand(command, socket);
                    break;

                case "TYPE":
                    HandleTypeCommand(command, socket);
                    break;

                case "XADD":
                    HandleStreamAddCommand(command, socket);
                    break;

                case "XRANGE":
                    HandleStreamRangeCommand(command, socket);
                    break;

                case "XREAD":
                    HandleStreamReadCommand(command, socket);
                    break;

                case "RPUSH":
                    HandlePushCommand(command, socket);
                    break;

                default:
                    HandleUnrecognizedComamnd(socket);
                    break;
            }
        }

        protected virtual void HandleEchoCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var commandToEcho = command.arguments[1];
            SendResponse(ResponseHandler.BulkResponse(commandToEcho), socket);
        }

        protected virtual void HandleSetCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandleGetCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var keyToRetrieve = command.GetKey();

            StoredValue? retrievedValue = _storage.Get(keyToRetrieve);
            if (retrievedValue != null)
            {
                int valueLength = retrievedValue.Value.Length;

                // Check for Expiry Value
                if (retrievedValue.Expiry != null)
                {
                    var expiryTime = DateTime.Parse(retrievedValue.Expiry);

                    // If Expired
                    if (expiryTime <= DateTime.Now && expiryTime.Millisecond <= DateTime.Now.Millisecond)
                    {

                        _storage.Remove(keyToRetrieve);
                        SendResponse(ResponseHandler.NullResponse(), socket);
                        return;

                    }
                    // If not expired
                    else
                    {
                        SendResponse(ResponseHandler.BulkResponse(retrievedValue.Value), socket);
                        return;
                    }
                }
                //value not set with expiry
                else
                {
                    SendResponse(ResponseHandler.BulkResponse(retrievedValue.Value), socket);
                    return;
                }

            }
            // Key not set
            else
            {
                SendResponse(ResponseHandler.NullResponse(), socket);
                return;
            }
        }

        protected void HandleIncrementCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            string keyToIncrement = command.GetKey();
            StoredValue? retrievedValue = _storage.Get(keyToIncrement);

            if (retrievedValue != null)
            {
                if (long.TryParse(retrievedValue.Value, out long currentValue))
                {
                    currentValue++;
                    retrievedValue.Value = currentValue.ToString();
                    _storage.Set(keyToIncrement, retrievedValue);
                    SendResponse(ResponseHandler.IntegerResponse(currentValue), socket);
                }
                else
                {
                    SendResponse(ResponseHandler.ErrorResponse("value is not an integer or out of range"), socket);
                }
            }
            else
            {
                // If key does not exist, set it to 1
                _storage.Set(keyToIncrement, new StoredValue("1", null));
                SendResponse(ResponseHandler.IntegerResponse(1), socket);
            }
        }

        protected void HandleMultiCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            _redisTransactions.InitalizeTransaction(socket);
        }

        protected void HandleExecCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            Transaction transaction = _redisTransactions.GetTransaction(socket)!;

            if (transaction == null)
            {
                SendResponse(ResponseHandler.ErrorResponse("EXEC without MULTI"), socket);
                return;
            }

            foreach (var execCommand in transaction.Commands)
            {
                HandleCommand(execCommand, socket);
            }

            transaction.Stop();

            SendResponse(ResponseHandler.SimpleArrayResponse(transaction.Responses.ToArray()), socket);

            _redisTransactions.RemoveTransaction(socket);
        }

        protected void HandleDiscardCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            if(!_redisTransactions.IsTransactionRunning(socket))
            {
                SendResponse(ResponseHandler.ErrorResponse("DISCARD without MULTI"), socket);
                return;
            }

            _redisTransactions.RemoveTransaction(socket);
            SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
        }

        protected virtual void HandleConfigComamnd(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var configToGet = command.GetConfigParameter();

            if (configToGet == "dir")
            {
                SendResponse(ResponseHandler.ArrayResponse([configToGet, _directory]), socket);
            }
            else if (configToGet == "dbfilename")
            {
                SendResponse(ResponseHandler.ArrayResponse([configToGet, _dbfilename]), socket);
            }
        }

        protected virtual void HandleKeysCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected void HandleInfoComamnd(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var param = command.GetInfoParameter();
            if (param == "replication")
            {
                var bulkString = GenerateBulkStringForInfoComamnd();
                var bulkStringLength = bulkString.Length;

                SendResponse(ResponseHandler.BulkResponse(bulkString), socket);
            }
            else
            {
                Console.Out.WriteLine("INFO command param not supported");
                SendResponse(ResponseHandler.NullResponse(), socket);
            }
        }

        protected abstract string GenerateBulkStringForInfoComamnd();

        protected virtual void HandleReplconfCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandlePsyncCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandlePingCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual void HandleWaitCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected void HandleTypeCommand(RedisProtocolParser.RESPMessage command, Socket socket) 
        {
            var keyToRetrieve = command.GetKey();
            StoredValue? retrievedValue = _storage.Get(keyToRetrieve);

            string type = (retrievedValue != null) ? "string" : "none";
            if (type == "none" && _streamStorage.GetStream(keyToRetrieve) != null)
                type = "stream";

            SendResponse(ResponseHandler.SimpleResponse(type), socket); 
        }

        protected void HandleStreamAddCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            //Correct command is XADD stream_key stream_id property_name property_value
            var streamName = command.GetKey();
            string streamId = command.arguments[2];

            RedisStream redisStream = _streamStorage.GetOrCreateStream(streamName);

            if (!IsStreamEntryValid(socket, redisStream, streamId))
                return;

            string previousEntryId = redisStream.Entries.LastOrDefault()?.Id ?? "0-0"; 

            Dictionary<string, string> entries = new();
            for (int i = 3; i < command.arguments.Count; i += 2)
            {
                entries.Add(command.arguments[i], command.arguments[i + 1]);
            }

            RedisStreamEntry redisStreamEntry = _streamStorage.GenerateValidEntry(redisStream, streamId, entries);

            _streamStorage.AddEntryToStream(streamName, redisStreamEntry);

            SendResponse(ResponseHandler.BulkResponse(redisStreamEntry.Id), socket);
        }

        private bool IsStreamEntryValid(Socket socket, RedisStream stream, string entryId)
        {
            // Matches the format of stream ID like "123-456" or "123-*"
            if (entryId != "*" && !RedisStream.IsStreamFormatValid(entryId))
            {
                SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD is invalid format"), socket);
                return false;
            }

            if (entryId == "*")
                return true;
            else if (entryId.Split("-")[1] == "*")
            {
                long timestamp = Convert.ToInt64(entryId.Split("-")[0]);

                if (stream.Entries.Any())
                {
                    RedisStreamEntry lastEntry = stream.Entries.Last();
                    if (lastEntry.CreatedAt > timestamp)
                    {
                        SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD is equal or smaller than the target stream top item"), socket);
                        return false;
                    }
                }
            }
            else
            {
                long timestamp = Convert.ToInt64(entryId.Split("-")[0]);
                int sequence = Convert.ToInt32(entryId.Split("-")[1]);

                if (timestamp == 0 && sequence == 0)
                {
                    SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD must be greater than 0-0"), socket);
                    return false;
                }

                if (stream.Entries.Any())
                {
                    RedisStreamEntry lastEntry = stream.Entries.Last();
                    if (lastEntry.CreatedAt > timestamp || (lastEntry.CreatedAt == timestamp && lastEntry.Sequence >= sequence))
                    {
                        SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD is equal or smaller than the target stream top item"), socket);
                        return false;
                    }
                }
            }

            return true;
        }

        protected void HandleStreamRangeCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var streamName = command.GetKey();
            string startStreamId = command.arguments[2];
            string endStreamId = command.arguments[3];

            RedisStream? stream = _streamStorage.GetStream(streamName);

            if (stream == null)
            {
                SendResponse(ResponseHandler.ErrorResponse($"Stream {streamName} does not exist."), socket);
                return;
            }

            List<RedisStreamEntry> entries = stream.GetEntriesInRange(startStreamId, endStreamId, true);
            List<string> responses = new List<string>();

            foreach (RedisStreamEntry entry in entries)
            {
                List<string> innerResponses = new List<string>();
                string bulkResponse = ResponseHandler.BulkResponse(entry.Id);
                innerResponses.Add(bulkResponse);
                List<string> entryValueResponese = new List<string>();  
                foreach (var kvp in entry.Values)
                {
                    entryValueResponese.Add(kvp.Key);
                    entryValueResponese.Add(kvp.Value);
                }

                string entryValueResponse = ResponseHandler.ArrayResponse(entryValueResponese.ToArray());
                innerResponses.Add(entryValueResponse);

                string totalEntryResponse = ResponseHandler.SimpleArrayResponse(innerResponses.ToArray());
                responses.Add(totalEntryResponse);
            }

            SendResponse(ResponseHandler.SimpleArrayResponse(responses.ToArray()), socket);
        }

        protected void HandleStreamReadCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            
            List<string> streamNames = new List<string>();
            List<string> streamIds = new List<string>();
            int blockTime = -1; // Default to -1 (no blocking)

            for (int i = 1; i < command.arguments.Count; i++)
            {
                string arg = command.arguments[i];
                if (arg.ToUpper() == "BLOCK")
                {
                    blockTime = Convert.ToInt32(command.arguments[i + 1]);
                    i++; // Skip the next argument since it's the block time
                    continue;
                }

                if (arg.ToUpper() == "STREAMS")
                    continue;

                //Convert "$" to lastest stram ID
                if (arg == "$")
                {
                    string latestID = _streamStorage.GetStream(streamNames[streamIds.Count])?.Entries.LastOrDefault()?.Id ?? "0-0";
                    streamIds.Add(latestID);
                    continue;
                }

                if (RedisStream.IsStreamFormatValid(arg))
                    streamIds.Add(arg); // This is a stream ID
                else
                    streamNames.Add(arg); // This is a stream name
            }

            //if (blockTime != -1)
            //   Thread.Sleep(blockTime);

            (string simpleArrayResponse, bool hasEntries) = GenerateSimpleArrayResponseForStreams(streamNames, streamIds);

            if (blockTime == -1)
            {
                SendResponse(simpleArrayResponse, socket);
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            while (blockTime == 0 || stopwatch.ElapsedMilliseconds <= blockTime)
            {
                // Check if any of the streams have entries
                (simpleArrayResponse, hasEntries) = GenerateSimpleArrayResponseForStreams(streamNames, streamIds);

                if (hasEntries)
                    break;

                //Thread.Sleep(100); // Sleep for a short time before checking again
            }
            
            if (blockTime != -1 && !hasEntries)
            {
                SendResponse(ResponseHandler.NullResponse(), socket);
                return;
            }

            SendResponse(simpleArrayResponse, socket);
        }

        protected void HandlePushCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var listName = command.GetKey();
            List<string> valueToPush = command.arguments[2..];

            if(_redisList.ContainsKey(listName))
            {
                _redisList[listName].AddRange(valueToPush);
            }
            else
            {
                _redisList.Add(listName, valueToPush);
            }

            SendResponse(ResponseHandler.IntegerResponse(_redisList[listName].Count()), socket);
        }

        private (string, bool) GenerateSimpleArrayResponseForStreams(List<string> streamNames, List<string> streamIds)
        {
            List<string> streamResponses = new List<string>();
            bool hasEntries = false;

            for (int i = 0; i < streamNames.Count; i++)
            {
                string streamName = streamNames[i];
                string startStreamId = streamIds.Count > i ? streamIds[i] : "0-0"; // Default to "0-0" if no ID is provided

                RedisStream? stream = _streamStorage.GetStream(streamName);

                //if (startStreamId == "$")
                //{   
                //    startStreamId = stream?.Entries.LastOrDefault()?.Id ?? "0-0"; // If "$" is provided, use the last entry ID or default to "0-0"
                //    if(!isSlept)
                //        Thread.Sleep(1000);
                //}

                List<RedisStreamEntry> entries = stream?.GetEntriesInRange(startStreamId, "+", false) ?? new List<RedisStreamEntry>();
                List<string> responses = new List<string>();

                if(entries.Count > 0)
                    hasEntries = true;

                foreach (RedisStreamEntry entry in entries)
                {
                    List<string> innerResponses = new List<string>();
                    string bulkResponse = ResponseHandler.BulkResponse(entry.Id);
                    innerResponses.Add(bulkResponse);
                    List<string> entryValueResponese = new List<string>();
                    foreach (var kvp in entry.Values)
                    {
                        entryValueResponese.Add(kvp.Key);
                        entryValueResponese.Add(kvp.Value);
                    }

                    string entryValueResponse = ResponseHandler.ArrayResponse(entryValueResponese.ToArray());
                    innerResponses.Add(entryValueResponse);

                    string totalEntryResponse = ResponseHandler.SimpleArrayResponse(innerResponses.ToArray());
                    responses.Add(totalEntryResponse);
                }


                string streamNameBulk = ResponseHandler.BulkResponse(streamName);
                string streamResponse = ResponseHandler.SimpleArrayResponse(responses.ToArray());
                string finalResponse = ResponseHandler.SimpleArrayResponse(new string[] { streamNameBulk, streamResponse });
                streamResponses.Add(finalResponse);
            }

            return (ResponseHandler.SimpleArrayResponse(streamResponses.ToArray()), hasEntries);
        }

        protected void HandleUnrecognizedComamnd(Socket socket)
        {
            SendResponse(ResponseHandler.NullResponse(), socket);
        }

        protected virtual void _HandleClient(Socket socket)
        {
            while (socket.Connected)
            {
                byte[] buffer = new byte[1024];
                int bytesRead = 0;

                try
                {
                    bytesRead = socket.Receive(buffer);
                }
                catch (SocketException e)
                {

                    Console.Out.WriteLine(e.Message);
                }
                finally
                {
                    if (socket.ReceiveTimeout > 0)
                    {
                        socket.ReceiveTimeout = 0;
                    }
                }

                RedisProtocolParser parser = new RedisProtocolParser(buffer, bytesRead);
                parser.Parse();

                var commandsRecieved = parser.commandArray;

                foreach (var command in commandsRecieved)
                {
                    if (_redisTransactions.IsTransactionRunning(socket) && command.command != "EXEC" && command.command != "DISCARD")
                    {
                        _redisTransactions.AddCommand(socket, command);
                        SendResponse(ResponseHandler.SimpleResponse(Constants.QUEUED_RESPONSE), socket);
                        continue;
                    }

                    HandleCommand(command, socket);
                }
            }
        }

        protected void SendResponse(string response, Socket socket)
        {
            if(_redisTransactions.IsTransactionRunning(socket) && response != ResponseHandler.SimpleResponse(Constants.QUEUED_RESPONSE))
            {
                //Only allow QUEUED response to be sent if we are in a transaction
                _redisTransactions.AddResponse(socket, response);
                return;
            }

            byte[] encodedResponse = Encoding.UTF8.GetBytes(response);
            socket.Send(encodedResponse);
        }

        protected string? _DateAsString(double? milliSeconds)
        {
            if (milliSeconds == null)
                return null;
            
            // have to do this to get around the nullable type checking thingy 
            double milliSecondsToParse = milliSeconds.Value;
            return DateTime.Now.AddMilliseconds(milliSecondsToParse).ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        protected void _SyncStorageToRDSFile()
        {
            _storage = _reader.GetCurrentDBState();
        }

        protected IPEndPoint _GetIPEndPoint(string host, int port)
        {
            IPHostEntry ipHostInfo = Dns.GetHostEntry(host);
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            IPEndPoint ipEndPoint = new(ipAddress, port);
            return ipEndPoint;
        }
    }

    public class StoredValue
    {
        public string Value { get; set; }
        public string? Expiry { get; set; }

        public StoredValue(string value, string? expiry)
        {
            Value = value;
            Expiry = expiry;
        }
    }
}
