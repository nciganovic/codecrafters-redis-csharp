using System;
using System.ComponentModel.Design;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

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
          
        List<NetworkStream> slaveStreams = new List<NetworkStream>();

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

        private readonly Dictionary<string, string> serverSettings;

        public enum HanshakeState
        {
            NONE,
            PING,
            REPLCONF1,
            REPLCONF2,
            PSYNC,
            FULLRESYNC,
            COMPLETED
        }
        public HanshakeState ProtocolHandshakeState { get; set; }

        public bool IsMasterInstance => !serverSettings.ContainsKey("replicaof");

        public Protocol(Dictionary<string, string> serverSettings)
        {
            this.serverSettings = serverSettings;
        }

        public async Task Write(NetworkStream stream, string request, Dictionary<string, ItemValue> values)
        {
            try
            {
                if (request.Length > 0 && (request[0] == PLUS_CHAR || request[0] == DOLLAR_CHAR))
                    return;

                List<Command> parsedCommands = ParseRequest(request);
                foreach (var parsedCommand in parsedCommands)
                {
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

                    if (response != string.Empty)
                        await SendResponse(stream, response);

                    if (action == Commands.SET || action == Commands.GET)
                    {
                        foreach (NetworkStream slaveStream in slaveStreams)
                        {
                            await SendResponse(slaveStream, ArrayResponse(parsedCommand.CommandActions.ToArray()));
                        }
                    }

                    if (action == Commands.PSYNC)
                    {
                        //Send empty RDB to slave
                        string emptyRdbBase64 = "UkVESVMwMDEx+glyZWRpcy12ZXIFNy4yLjD6CnJlZGlzLWJpdHPAQPoFY3RpbWXCbQi8ZfoIdXNlZC1tZW3CsMQQAPoIYW9mLWJhc2XAAP/wbjv+wP9aog==";
                        byte[] binaryData = Convert.FromBase64String(emptyRdbBase64);
                        byte[] rdbResynchronizationFileMsg = Encoding.ASCII.GetBytes($"${binaryData.Length}\r\n").Concat(binaryData).ToArray();
                        await stream.WriteAsync(rdbResynchronizationFileMsg);
                        Console.WriteLine("Master sent empty rdb file.");
                        slaveStreams.Add(stream);
                    }
                }
                
            }
            catch (Exception ex)
            {
                string errorMessage = ErrorResponse($"Error: {ex.Message}");
                await SendResponse(stream, errorMessage);
            }
        }

        public async Task HandleMasterSlaveHandshake(NetworkStream stream, string request, Dictionary<string, ItemValue> values)
        {
            string response = ProtocolHandshakeState switch
            {
                HanshakeState.PING => CreateReplconf1Response(request),
                HanshakeState.REPLCONF1 => CreateReplconf2Response(request),
                HanshakeState.REPLCONF2 => CreatePsyncResponse(request),
                HanshakeState.PSYNC => HandleFullresyncRequest(request),
                _ => string.Empty
            };

            //When FULLRESYNC first request will be FULLRESYNC sd23l754emvgmz53hk5dwuo7m5oe6cx4tzvc0cv2 0
            //Second will be \$88\r\nREDIS0011?\tredis-ver♣7\.2\.0?\nredis-bits?@?♣ctime??eused-mem°?►aof-base???n;???Z? rdb file
            //TODO on FULLRESYNC state handle saving rdb file that is sent from master

            int index = request.IndexOf(ASTERISK_CHAR);
            if(index > 0)
                request = request.Substring(index);

            if (request[0] == ASTERISK_CHAR)
                await HandleWriteAsSlave(stream, request, values);

            if (response != string.Empty)
                await SendResponse(stream, response);
        }

        public async Task SendPingRequest(NetworkStream stream)
        {
            string request = ArrayResponse([Enum.GetName(typeof(Commands), Commands.PING) ?? "PING"]);
            await SendResponse(stream, request);
        }

        private string CreateReplconf1Response(string request)
        {
            if (request != SimpleResponse(PING_RESPONSE))
                ErrorResponse("Recived: " + request + ", expected: " + SimpleResponse(PING_RESPONSE));

            string slavePort = serverSettings["port"];
            string repconfCommand = Enum.GetName(typeof(Commands), Commands.REPLCONF) ?? "REPLCONF";
            ProtocolHandshakeState = HanshakeState.REPLCONF1;
            return ArrayResponse([repconfCommand, "listening-port", slavePort.ToString()]);
        }

        private string CreateReplconf2Response(string request)
        {
            if (request != SimpleResponse(OK_RESPONSE))
                ErrorResponse("Recived: " + request + ", expected: " + SimpleResponse(OK_RESPONSE));

            string repconfCommand = Enum.GetName(typeof(Commands), Commands.REPLCONF) ?? "REPLCONF";
            ProtocolHandshakeState = HanshakeState.REPLCONF2;
            return ArrayResponse([repconfCommand, "capa", "psync2"]);
        }

        private string CreatePsyncResponse(string request)
        {
            if (request != SimpleResponse(OK_RESPONSE))
                ErrorResponse("Recived: " + request + ", expected: " + SimpleResponse(OK_RESPONSE));

            string repconfCommand = Enum.GetName(typeof(Commands), Commands.PSYNC) ?? "PSYNC";
            ProtocolHandshakeState = HanshakeState.PSYNC;
            return ArrayResponse([repconfCommand, "?", "-1"]);
        }

        private string HandleFullresyncRequest(string request)
        {
            if (!isFullresyncResponseValid(request))
                ErrorResponse($"Response {request} is not valid {Enum.GetName(typeof(HanshakeState), HanshakeState.FULLRESYNC)} pattern");

            string[] parameters = request.Split(' ');
            serverSettings["master_server_id"] = parameters[1];
            ProtocolHandshakeState = HanshakeState.FULLRESYNC;
            Console.WriteLine("Handshake completed with " + serverSettings["master_server_id"]);
            return string.Empty;
        }

        private async Task<string> HandleWriteAsSlave(NetworkStream stream, string request, Dictionary<string, ItemValue> values)
        {
            await Write(stream, request, values);
            return string.Empty;
        }

        private string HandlePingResponse()
        {
            return SimpleResponse(PING_RESPONSE);
        }

        private string HandleEchoCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 2)
                ErrorResponse("ECHO command excpects one parameter");

            return BulkResponse(parsedCommand.CommandActions[1]);
        }

        private string HandleGetCommand(Command parsedCommand, Dictionary<string, ItemValue> values)
        {
            if (parsedCommand.CommandActions.Count != 2)
                return ErrorResponse("GET expects one parameter");

            string key = parsedCommand.CommandActions[1];

            if (values.TryGetValue(key, out var item) && item.IsValid)
                return BulkResponse(item.Value);

            values.Remove(key); 
            return NullResponse();
        }

        private string HandleSetCommand(Command parsedCommand, Dictionary<string, ItemValue> values)
        {
            if (parsedCommand.CommandActions.Count < 2)
                ErrorResponse("SET command requiers at least 2 parameters, key and value");

            string key = parsedCommand.CommandActions[1];
            string value = parsedCommand.CommandActions[2];

            double timeToLive = double.MaxValue;

            if (parsedCommand.CommandActions.Count > 3 && parsedCommand.CommandActions[3].ToUpper() == PX)
                timeToLive = Convert.ToDouble(parsedCommand.CommandActions[4]);

            values[key] = new ItemValue(value, timeToLive);


            return IsMasterInstance ? SimpleResponse(OK_RESPONSE) : string.Empty;
        }

        private string HandleConfigCommand(Command parsedCommand)
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

        private string HandleKeysCommand(Command parsedCommand, Dictionary<string, ItemValue> values)
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

        private string HandleInfoCommand(Command parsedCommand)
        {
            Console.WriteLine("enter info command");

            if (parsedCommand.CommandActions.Count != 2)
                ErrorResponse("wrong number of arguments for 'info' command");

            string infoType = parsedCommand.CommandActions[1];
            if (infoType != "replication")
                ErrorResponse("unsupported type of info command");

            string role = IsMasterInstance ? "master" : "slave";
            string info = $"role:{role}";

            string[] items = new string[3];
            items[0] = info;
            items[1] = $"master_replid:{serverSettings["server_id"]}";
            items[2] = "master_repl_offset:0";

            return BulkResponse(string.Join(SPACE_SING, items));
        }

        private string HandleReplConfCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ErrorResponse("wrong number of arguments for 'replconf' command");

            serverSettings[parsedCommand.CommandActions[1]] = parsedCommand.CommandActions[2];

            return SimpleResponse(OK_RESPONSE);
        }

        private string HandlePsyncCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ErrorResponse("wrong number of arguments for 'psync' command");

            return SimpleResponse($"{Enum.GetName(typeof(HanshakeState), HanshakeState.FULLRESYNC)} {serverSettings["server_id"]} 0");
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

        private List<Command> ParseRequest(string request)
        {
            string[] requestList = Regex.Split(request, @"\*\d+");

            if(requestList.Length == 0)
                throw new ArgumentException($"{ASTERISK_CHAR} required as first parameter");

            List<Command> commands = new List<Command>();

            foreach (string command in requestList)
            {
                if (command.Length == 0)
                    continue;

                List<string> commandParams = command.Split(SPACE_SING).ToList();
            
                if (commandParams.Count == 0)
                    throw new ArgumentException($"Invalid command, no {SPACE_SING} found");

                commandParams.RemoveAt(0);
                commandParams.RemoveAt(commandParams.Count - 1);
                commandParams.RemoveAll(x => x.IndexOf('$') != -1);
                commandParams.RemoveAll(x => x == string.Empty);

                List<int> commandParamsToRemove = new List<int>();

                Dictionary<string, string> commandDict = new Dictionary<string, string>();
                for (int i = 0; i < commandParams.Count - 1; i++)
                {
                    if (commandParams[i][0] == '-')
                    {
                        commandDict.Add(commandParams[i], commandParams[i + 1]);
                        commandParamsToRemove.Insert(0, i);
                        commandParamsToRemove.Insert(0, i + 1);
                    }
                }

                foreach(int cmd in commandParamsToRemove)
                    commandParams.RemoveAt(cmd);

                commands.Add(new Command { CommandActions = commandParams, CommandParams = commandDict });
            }

            return commands;
        }

        private bool isFullresyncResponseValid(string value)
        {
            string pattern = @"^FULLRESYNC [a-z0-9]{40} 0$";
            Regex regex = new Regex(pattern);
            return regex.IsMatch(value);
        }
    }

    class Command
    {
        public List<string> CommandActions { get; set; } = new List<string>();
        public Dictionary<string, string> CommandParams { get; set; } = new Dictionary<string, string>();
    }
}
