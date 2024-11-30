using System.ComponentModel.Design;
using System.Net.Sockets;
using System.Text;

namespace codecrafters_redis.src
{
    public class Protocol
    {
        private const char ASTERISK_CHAR = '*';
        private const char DOLLAR_CHAR = '$';
        private const char PLUS_CHAR = '+';
        private const string SPACE_SING = "\r\n";
        private const string PING_RESPONSE = "PONG";
        private const string OK_RESPONSE = "OK";
        private const string NULL_RESPONSE = "-1";
        private const string PX = "PX";

        private readonly string[] ReplConfSupportedParams = { "capa", "listening-port" };

        private enum Commands
        { 
            PING,
            ECHO,
            GET,
            SET,
            CONFIG,
            KEYS,
            INFO,
            REPLCONF,
            PSYNC
        }

        private readonly string command;
        private readonly Dictionary<string, string> serverSettings;

        public enum HanshakeState
        {
            NONE,
            PING,
            REPLCONF1,
            REPLCONF2,
            PSYNC,
            FULLRESYNC
        }
        public HanshakeState ProtocolHanshakeState { get; set; }

        public Protocol(Dictionary<string, string> parameters)
        {
            command = string.Empty;
            this.serverSettings = parameters;
        }

        public Protocol(string comamnd, Dictionary<string, string> parameters)
        {
            this.command = comamnd;
            this.serverSettings = parameters;   
        }

        public async Task Write(NetworkStream stream, Dictionary<string, ItemValue> values)
        {
            try
            {
                ParsedCommand parsedCommand = ParseCommand(command);
                if (parsedCommand.CommandActions == null || parsedCommand.CommandActions.Count == 0)
                    return;

                if (!Enum.TryParse(parsedCommand.CommandActions[0], true, out Commands action))
                {
                    string errorMessage = ErrorResponse($"Unknown command: {parsedCommand.CommandActions[0]}");
                    await SendResponse(stream, errorMessage);
                    return;
                }

                string response = action switch
                {
                    Commands.PING => HandlePingResponse(),
                    Commands.ECHO => HandleEchoCommand(parsedCommand),
                    Commands.GET => HandleGetCommand(parsedCommand, values),
                    Commands.SET => HandleSetCommand(parsedCommand, values),
                    Commands.CONFIG => HandleConfigCommand(parsedCommand),
                    Commands.KEYS => HandleKeysCommand(parsedCommand, values),
                    Commands.INFO => HandleInfoCommand(parsedCommand),
                    Commands.REPLCONF => HandleReplConfCommand(parsedCommand),
                    Commands.PSYNC => HandlePsyncCommand(parsedCommand),
                    _ => ErrorResponse($"Unknown command: {action}")
                };

                await SendResponse(stream, response);

                if (action == Commands.PSYNC)
                {
                    string emptyRdbBase64 = "UkVESVMwMDEx+glyZWRpcy12ZXIFNy4yLjD6CnJlZGlzLWJpdHPAQPoFY3RpbWXCbQi8ZfoIdXNlZC1tZW3CsMQQAPoIYW9mLWJhc2XAAP/wbjv+wP9aog==";
                    byte[] binaryData = Convert.FromBase64String(emptyRdbBase64);
                    byte[] rdbResynchronizationFileMsg = Encoding.ASCII.GetBytes($"${binaryData.Length}\r\n").Concat(binaryData).ToArray();
                    stream.Write(rdbResynchronizationFileMsg);
                }
            }
            catch (Exception ex)
            {
                string errorMessage = ErrorResponse($"Error: {ex.Message}");
                await SendResponse(stream, errorMessage);
            }
        }

        public async Task HandleMasterSlaveHandshake(NetworkStream stream, string request)
        {
            //Recived PONG from master, send two REPLCONF commands from slave to master
            if (ProtocolHanshakeState == HanshakeState.PING && request == SimpleResponse(PING_RESPONSE))
            {
                string slavePort = serverSettings["port"];
                string repconfCommand = Enum.GetName(typeof(Commands), Commands.REPLCONF) ?? "REPLCONF";
                string response = ArrayResponse([repconfCommand, "listening-port", slavePort.ToString()]);
                await SendResponse(stream, response);
                ProtocolHanshakeState = HanshakeState.REPLCONF1;
            }
            else if (ProtocolHanshakeState == HanshakeState.REPLCONF1 && request == SimpleResponse(OK_RESPONSE))
            {
                string repconfCommand = Enum.GetName(typeof(Commands), Commands.REPLCONF) ?? "REPLCONF";
                string response = ArrayResponse([repconfCommand, "capa", "psync2"]);
                await SendResponse(stream, response);
                ProtocolHanshakeState = HanshakeState.REPLCONF2;
            }
            else if (ProtocolHanshakeState == HanshakeState.REPLCONF2 && request == SimpleResponse(OK_RESPONSE))
            {
                string repconfCommand = Enum.GetName(typeof(Commands), Commands.PSYNC) ?? "PSYNC";
                string response = ArrayResponse([repconfCommand, "?", "-1"]);
                await SendResponse(stream, response);
                ProtocolHanshakeState = HanshakeState.PSYNC;
            }
            else if (ProtocolHanshakeState == HanshakeState.PSYNC)
            {
                string[] parameters = request.Split(' ');
                serverSettings["master_replid"] = parameters[1];
                ProtocolHanshakeState = HanshakeState.FULLRESYNC;
            }
            else if (ProtocolHanshakeState == HanshakeState.FULLRESYNC)
            { 
                
            }
            else
                ErrorResponse("synchronization failed between master and slave");
        }

        private string HandlePingResponse()
        {
            return SimpleResponse(PING_RESPONSE);
        }

        private string HandleEchoCommand(ParsedCommand parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 2)
                ErrorResponse("ECHO command excpects one parameter");

            return BulkResponse(parsedCommand.CommandActions[1]);
        }

        private string HandleGetCommand(ParsedCommand parsedCommand, Dictionary<string, ItemValue> values)
        {
            if (parsedCommand.CommandActions.Count != 2)
                return ErrorResponse("GET expects one parameter");

            string key = parsedCommand.CommandActions[1];

            if (values.TryGetValue(key, out var item) && item.IsValid)
                return BulkResponse(item.Value);

            values.Remove(key); 
            return NullResponse();
        }

        private string HandleSetCommand(ParsedCommand parsedCommand, Dictionary<string, ItemValue> values)
        {
            if (parsedCommand.CommandActions.Count < 2)
                ErrorResponse("SET command requiers at least 2 parameters, key and value");

            string key = parsedCommand.CommandActions[1];
            string value = parsedCommand.CommandActions[2];

            double timeToLive = double.MaxValue;

            if (parsedCommand.CommandActions.Count > 3 && parsedCommand.CommandActions[3].ToUpper() == PX)
                timeToLive = Convert.ToDouble(parsedCommand.CommandActions[4]);

            values[key] = new ItemValue(value, timeToLive);

            return SimpleResponse(OK_RESPONSE);
        }

        private string HandleConfigCommand(ParsedCommand parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ErrorResponse("wrong number of parameters for config command");

            if (parsedCommand.CommandActions[1].ToUpper() != Enum.GetName(typeof(Commands), Commands.GET))
                ErrorResponse("Only get command for CONFIG action is currently supported");

            if (!serverSettings.ContainsKey(parsedCommand.CommandActions[2]))
                ErrorResponse($"Key {parsedCommand.CommandActions[2]} not found");

            string[] elements = { parsedCommand.CommandActions[2], serverSettings[parsedCommand.CommandActions[2]] };
            return ArrayResponse(elements);
        }

        private string HandleKeysCommand(ParsedCommand parsedCommand, Dictionary<string, ItemValue> values)
        {
            if (parsedCommand.CommandActions.Count != 2)
                ErrorResponse("wrong number of arguments for 'keys' command");

            string pattern = parsedCommand.CommandActions[1];

            //select all keys
            if (pattern == $"{ASTERISK_CHAR}")
            {
                List<string> keys = new List<string>();
                foreach (string key in values.Keys)
                {
                    if (values[key].IsValid)
                        keys.Add(key);
                    else
                        values.Remove(key);
                }

                return ArrayResponse(keys.ToArray());
            }

            return NullResponse();
        }

        private string HandleInfoCommand(ParsedCommand parsedCommand)
        {
            Console.WriteLine("enter info command");

            if (parsedCommand.CommandActions.Count != 2)
                ErrorResponse("wrong number of arguments for 'info' command");

            string infoType = parsedCommand.CommandActions[1];
            if (infoType != "replication")
                ErrorResponse("unsupported type of info command");

            string role = serverSettings.ContainsKey("replicaof") ? "slave" : "master";
            string info = $"role:{role}";

            string[] items = new string[3];
            items[0] = info;
            items[1] = $"master_replid:{serverSettings["replid"]}";
            items[2] = "master_repl_offset:0";

            return BulkResponse(string.Join(SPACE_SING, items));
        }

        private string HandleReplConfCommand(ParsedCommand parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ErrorResponse("wrong number of arguments for 'replconf' command");

            serverSettings[parsedCommand.CommandActions[1]] = parsedCommand.CommandActions[2];

            return SimpleResponse(OK_RESPONSE);
        }

        private string HandlePsyncCommand(ParsedCommand parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ErrorResponse("wrong number of arguments for 'psync' command");

            return SimpleResponse($"FULLRESYNC {serverSettings["replid"]} 0");
        }

        private async Task SendResponse(NetworkStream stream, string response)
        {
            byte[] responseData = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseData, 0, responseData.Length);
            Console.WriteLine("Response sent: " + response);
        }

        private string ErrorResponse(string message)
        {
            return $"-ERROR {message}{SPACE_SING}";
        }

        private string SimpleResponse(string value)
        {
            string echo = string.Empty;
            echo += PLUS_CHAR;
            echo += value;
            echo += SPACE_SING;
            return echo;
        }

        private string ArrayResponse(string[] elements)
        {
            string echo = string.Empty;
            echo += ASTERISK_CHAR;
            echo += elements.Length;
            echo += SPACE_SING;
            foreach (var element in elements)
            {
                echo += BulkResponse(element);
            }
            return echo;
        }

        private string BulkResponse(string value)
        {
            string echo = string.Empty;
            echo += DOLLAR_CHAR;
            echo += value.Length;
            echo += SPACE_SING;
            echo += value;
            echo += SPACE_SING;
            return echo;
        }

        private string NullResponse()
        {
            string response = string.Empty;
            response += DOLLAR_CHAR;
            response += NULL_RESPONSE;
            response += SPACE_SING;
            return response;
        }

        private ParsedCommand ParseCommand(string command)
        {
            List<string> commands = command.Split(SPACE_SING).ToList();
            
            if (commands.Count == 0)
                throw new ArgumentException($"Invalid command, no \\r\\n found");

            if (commands[0].IndexOf(ASTERISK_CHAR) == -1)
                throw new ArgumentException($"{ASTERISK_CHAR} required as first parameter");

            int paramCount = Convert.ToInt32(commands[0].Split(ASTERISK_CHAR)[1]);

            if (paramCount == -1)
                return new ParsedCommand();

            if (paramCount == 0)
                return new ParsedCommand();

            commands.RemoveAt(0);
            commands.RemoveAt(commands.Count - 1);
            commands.RemoveAll(x => x.IndexOf('$') != -1);
            commands.RemoveAll(x => x == string.Empty);

            List<int> commandParamsToRemove = new List<int>();

            Dictionary<string, string> commandParams = new Dictionary<string, string>();
            for (int i = 0; i < commands.Count - 1; i++)
            {
                if (commands[i][0] == '-')
                { 
                    commandParams.Add(commands[i], commands[i + 1]);
                    commandParamsToRemove.Insert(0, i);
                    commandParamsToRemove.Insert(0, i + 1);
                }
            }

            foreach(int cmd in commandParamsToRemove)
                commands.RemoveAt(cmd);

            return new ParsedCommand { CommandActions = commands, CommandParams = commandParams };
        }
    }

    class ParsedCommand
    {
        public List<string> CommandActions { get; set; } = new List<string>();
        public Dictionary<string, string> CommandParams { get; set; } = new Dictionary<string, string>();
    }
}
