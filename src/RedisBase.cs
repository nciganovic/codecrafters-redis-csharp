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
                var thread = new Thread(async () => await _HandleClient(socket));
                thread.Start();
            }
        }

        public async Task HandleCommand(RESPMessage command, Socket socket)
        {
            switch (command.command)
            {
                case "ECHO":
                    await HandleEchoCommand(command, socket);
                    break;

                case "SET":
                    await HandleSetCommand(command, socket);
                    break;

                case "GET":
                    await HandleGetCommand(command, socket);
                    break;

                case "INCR":
                    await HandleIncrementCommand(command, socket);
                    break;

                case "MULTI":
                    await HandleMultiCommand(command, socket);
                    break;

                case "EXEC":
                    await HandleExecCommand(command, socket);
                    break;

                case "DISCARD":
                    await HandleDiscardCommand(command, socket);
                    break;

                case "CONFIG":
                    await HandleConfigComamnd(command, socket);
                    break;

                case "KEYS":
                    await HandleKeysCommand(command, socket);
                    break;

                case "INFO":
                    await HandleInfoComamnd(command, socket);
                    break;

                case "REPLCONF":
                    await HandleReplconfCommand(command, socket);
                    break;

                case "PSYNC":
                    await HandlePsyncCommand(command, socket);
                    break;

                case "PING":
                    await HandlePingCommand(command, socket);
                    break;

                case "WAIT":
                    await HandleWaitCommand(command, socket);
                    break;

                case "TYPE":
                    await HandleTypeCommand(command, socket);
                    break;

                case "XADD":
                    await HandleStreamAddCommand(command, socket);
                    break;

                case "XRANGE":
                    await HandleStreamRangeCommand(command, socket);
                    break;

                case "XREAD":
                    await HandleStreamReadCommand(command, socket);
                    break;

                case "RPUSH":
                    await HandlePushCommand(command, socket);
                    break;

                case "LPUSH":
                    await HandlePushCommand(command, socket, true);
                    break;

                case "LLEN":
                    await HandleLLenCommand(command, socket);
                    break;

                case "LPOP":
                    await HandleLPopCommand(command, socket, false);
                    break;

                case "BLPOP":
                    await HandleLPopCommand(command, socket, true);
                    break;

                case "LRANGE":
                    await HandleRangeCommand(command, socket);
                    break;

                default:
                    await HandleUnrecognizedComamnd(socket);
                    break;
            }
        }

        protected virtual async Task HandleEchoCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var commandToEcho = command.arguments[1];
            await SendResponse(ResponseHandler.BulkResponse(commandToEcho), socket);
        }

        protected virtual async Task HandleSetCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual async Task HandleGetCommand(RedisProtocolParser.RESPMessage command, Socket socket)
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
                        await SendResponse(ResponseHandler.NullResponse(), socket);
                        return;

                    }
                    // If not expired
                    else
                    {
                        await SendResponse(ResponseHandler.BulkResponse(retrievedValue.Value), socket);
                        return;
                    }
                }
                //value not set with expiry
                else
                {
                    await SendResponse(ResponseHandler.BulkResponse(retrievedValue.Value), socket);
                    return;
                }

            }
            // Key not set
            else
            {
                await SendResponse(ResponseHandler.NullResponse(), socket);
                return;
            }
        }

        protected async Task HandleIncrementCommand(RedisProtocolParser.RESPMessage command, Socket socket)
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
                    await SendResponse(ResponseHandler.IntegerResponse(currentValue), socket);
                }
                else
                {
                   await SendResponse(ResponseHandler.ErrorResponse("value is not an integer or out of range"), socket);
                }
            }
            else
            {
                // If key does not exist, set it to 1
                _storage.Set(keyToIncrement, new StoredValue("1", null));
                await SendResponse(ResponseHandler.IntegerResponse(1), socket);
            }
        }

        protected async Task HandleMultiCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            await SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
            _redisTransactions.InitalizeTransaction(socket);
        }

        protected async Task HandleExecCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            Transaction transaction = _redisTransactions.GetTransaction(socket)!;

            if (transaction == null)
            {
                await SendResponse(ResponseHandler.ErrorResponse("EXEC without MULTI"), socket);
                return;
            }

            foreach (var execCommand in transaction.Commands)
            {
                await HandleCommand(execCommand, socket);
            }

            transaction.Stop();

            await SendResponse(ResponseHandler.SimpleArrayResponse(transaction.Responses.ToArray()), socket);

            _redisTransactions.RemoveTransaction(socket);
        }

        protected async Task HandleDiscardCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            if(!_redisTransactions.IsTransactionRunning(socket))
            {
                await SendResponse(ResponseHandler.ErrorResponse("DISCARD without MULTI"), socket);
                return;
            }

            _redisTransactions.RemoveTransaction(socket);
            await SendResponse(ResponseHandler.SimpleResponse(Constants.OK_RESPONSE), socket);
        }

        protected virtual async Task HandleConfigComamnd(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var configToGet = command.GetConfigParameter();

            if (configToGet == "dir")
            {
                await SendResponse(ResponseHandler.ArrayResponse([configToGet, _directory]), socket);
            }
            else if (configToGet == "dbfilename")
            {
                await SendResponse(ResponseHandler.ArrayResponse([configToGet, _dbfilename]), socket);
            }
        }

        protected virtual async Task HandleKeysCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected async Task HandleInfoComamnd(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var param = command.GetInfoParameter();
            if (param == "replication")
            {
                var bulkString = GenerateBulkStringForInfoComamnd();
                var bulkStringLength = bulkString.Length;

                await SendResponse(ResponseHandler.BulkResponse(bulkString), socket);
            }
            else
            {
                Console.Out.WriteLine("INFO command param not supported");
                await SendResponse(ResponseHandler.NullResponse(), socket);
            }
        }

        protected abstract string GenerateBulkStringForInfoComamnd();

        protected virtual async Task HandleReplconfCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual async Task HandlePsyncCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual async Task HandlePingCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected virtual async Task HandleWaitCommand(RedisProtocolParser.RESPMessage command, Socket socket) { }

        protected async Task HandleTypeCommand(RedisProtocolParser.RESPMessage command, Socket socket) 
        {
            var keyToRetrieve = command.GetKey();
            StoredValue? retrievedValue = _storage.Get(keyToRetrieve);

            string type = (retrievedValue != null) ? "string" : "none";
            if (type == "none" && _streamStorage.GetStream(keyToRetrieve) != null)
                type = "stream";

            await SendResponse(ResponseHandler.SimpleResponse(type), socket); 
        }

        protected async Task HandleStreamAddCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            //Correct command is XADD stream_key stream_id property_name property_value
            var streamName = command.GetKey();
            string streamId = command.arguments[2];

            RedisStream redisStream = _streamStorage.GetOrCreateStream(streamName);

            if (!await IsStreamEntryValid(socket, redisStream, streamId))
                return;

            string previousEntryId = redisStream.Entries.LastOrDefault()?.Id ?? "0-0"; 

            Dictionary<string, string> entries = new();
            for (int i = 3; i < command.arguments.Count; i += 2)
            {
                entries.Add(command.arguments[i], command.arguments[i + 1]);
            }

            RedisStreamEntry redisStreamEntry = _streamStorage.GenerateValidEntry(redisStream, streamId, entries);

            _streamStorage.AddEntryToStream(streamName, redisStreamEntry);

            await SendResponse(ResponseHandler.BulkResponse(redisStreamEntry.Id), socket);
        }

        private async Task<bool> IsStreamEntryValid(Socket socket, RedisStream stream, string entryId)
        {
            // Matches the format of stream ID like "123-456" or "123-*"
            if (entryId != "*" && !RedisStream.IsStreamFormatValid(entryId))
            {
                await SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD is invalid format"), socket);
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
                        await SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD is equal or smaller than the target stream top item"), socket);
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
                    await SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD must be greater than 0-0"), socket);
                    return false;
                }

                if (stream.Entries.Any())
                {
                    RedisStreamEntry lastEntry = stream.Entries.Last();
                    if (lastEntry.CreatedAt > timestamp || (lastEntry.CreatedAt == timestamp && lastEntry.Sequence >= sequence))
                    {
                        await SendResponse(ResponseHandler.ErrorResponse("The ID specified in XADD is equal or smaller than the target stream top item"), socket);
                        return false;
                    }
                }
            }

            return true;
        }

        protected async Task HandleStreamRangeCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var streamName = command.GetKey();
            string startStreamId = command.arguments[2];
            string endStreamId = command.arguments[3];

            RedisStream? stream = _streamStorage.GetStream(streamName);

            if (stream == null)
            {
                await SendResponse(ResponseHandler.ErrorResponse($"Stream {streamName} does not exist."), socket);
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

            await SendResponse(ResponseHandler.SimpleArrayResponse(responses.ToArray()), socket);
        }

        protected async Task HandleStreamReadCommand(RedisProtocolParser.RESPMessage command, Socket socket)
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
                await SendResponse(simpleArrayResponse, socket);
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
                await SendResponse(ResponseHandler.NullResponse(), socket);
                return;
            }

            await SendResponse(simpleArrayResponse, socket);
        }

        protected async Task HandlePushCommand(RedisProtocolParser.RESPMessage command, Socket socket, bool prepend = false)
        {
            var listName = command.GetKey();
            List<string> valueToPush = command.arguments[2..];

            if(_redisList.ContainsKey(listName))
            {
                if (prepend)
                {
                    valueToPush.Reverse();
                    _redisList[listName].InsertRange(0, valueToPush);
                }
                else
                    _redisList[listName].AddRange(valueToPush);
            }
            else
            {
                _redisList.Add(listName, valueToPush);
            }

            await SendResponse(ResponseHandler.IntegerResponse(_redisList[listName].Count()), socket);
        }

        protected async Task HandleLLenCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var listName = command.GetKey();
            
            int len = _redisList.ContainsKey(listName) ? _redisList[listName].Count : 0;    

            await SendResponse(ResponseHandler.IntegerResponse(len), socket);
        }

        protected async Task HandleLPopCommand(RedisProtocolParser.RESPMessage command, Socket socket, bool hasBlock)
        {
            var listName = command.GetKey();
            int popCount = 1; // Default pop count is 1

            if ((command.arguments.Count == 3 && !hasBlock) && command.arguments.Count > 2 && int.TryParse(command.arguments[2], out int count))
            {
                popCount = count;
            }

            if (!hasBlock && (!_redisList.ContainsKey(listName) || _redisList[listName].Count == 0))
            {
                await SendResponse(ResponseHandler.NullResponse(), socket);
                return;
            }

            Console.WriteLine($"Popping {popCount} items from list '{listName}'");

            //TODO handle if block time is not valid number
            long blockTime = -1; // Default to -1 (no blocking)
            if (hasBlock)
            {
                blockTime = Convert.ToInt64(command.arguments[command.arguments.Count() - 1]);

                Console.WriteLine($"Blocking pop for {listName} with block time {blockTime} ms");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                bool foundItem = false;
                List<string> popedItems = new List<string>();

                while (blockTime == 0 || stopwatch.ElapsedMilliseconds <= blockTime)
                {
                    if(!_redisList.ContainsKey(listName))
                    {
                        Console.WriteLine($"List '{listName}' does not exist, waiting for items to be added...");
                        //await Task.Delay(100); // Wait before checking again
                        continue;
                    }

                    Console.WriteLine($"Checking for items in list '{listName}' to pop {popCount}...");
                    string[] itemsToPop = _redisList[listName].Take(popCount).ToArray();
                    if (itemsToPop.Length > 0)
                    {
                        Console.WriteLine($"Found {itemsToPop.Length} items to pop from list '{listName}'");
                        popedItems.AddRange(itemsToPop);
                        _redisList[listName].RemoveRange(0, popCount);
                        foundItem = true;
                        break;
                    }
                }

                popedItems.Insert(0, listName);

                if (foundItem)
                    await SendResponse(ResponseHandler.ArrayResponse(popedItems.ToArray()), socket);
                else
                    await SendResponse(ResponseHandler.NullResponse(), socket);

                return;
            }

            string[] items = _redisList[listName].Take(popCount).ToArray();

            _redisList[listName].RemoveRange(0, popCount);

            if(items.Count() == 1)
                await SendResponse(ResponseHandler.SimpleResponse(items.First()), socket);
            else
                await SendResponse(ResponseHandler.ArrayResponse(items.ToArray()), socket);
        }

        protected async Task HandleRangeCommand(RedisProtocolParser.RESPMessage command, Socket socket)
        {
            var listName = command.GetKey();

            if (!int.TryParse(command.arguments[2], out int startIndex) || !int.TryParse(command.arguments[3], out int endIndex))
            {
                await SendResponse(ResponseHandler.ErrorResponse("Invalid range specified"), socket);
                return;
            }

            if (startIndex < 0)
                startIndex = _redisList[listName].Count + startIndex; // Handle negative indexing
            if (endIndex < 0)
                endIndex = _redisList[listName].Count + endIndex; // Handle negative indexing

            if (!_redisList.ContainsKey(listName) || startIndex > endIndex)
            {
                await SendResponse(ResponseHandler.ArrayResponse([]), socket);
                return;
            }

            List<string> listValues = _redisList[listName].Skip(startIndex).Take(endIndex - startIndex + 1).ToList();

            await SendResponse(ResponseHandler.ArrayResponse(listValues.ToArray()), socket);
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

        protected async Task HandleUnrecognizedComamnd(Socket socket)
        {
            await SendResponse(ResponseHandler.NullResponse(), socket);
        }

        protected virtual async Task _HandleClient(Socket socket)
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
                        await SendResponse(ResponseHandler.SimpleResponse(Constants.QUEUED_RESPONSE), socket);
                        continue;
                    }

                    await HandleCommand(command, socket);
                }
            }
        }

        protected async Task SendResponse(string response, Socket socket)
        {
            if(_redisTransactions.IsTransactionRunning(socket) && response != ResponseHandler.SimpleResponse(Constants.QUEUED_RESPONSE))
            {
                //Only allow QUEUED response to be sent if we are in a transaction
                _redisTransactions.AddResponse(socket, response);
                return;
            }

            byte[] encodedResponse = Encoding.UTF8.GetBytes(response);
            await socket.SendAsync(encodedResponse);
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
