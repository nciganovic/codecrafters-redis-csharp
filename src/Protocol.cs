using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using static codecrafters_redis.src.Enums;
using static codecrafters_redis.src.Constants;

namespace codecrafters_redis.src
{
    public class Protocol
    {
        private int offset = 0;

        private List<NetworkStream> slaveStreams = new List<NetworkStream>();

        private readonly Dictionary<string, string> configuration;

        private bool IsMasterInstance => !configuration.ContainsKey("replicaof");

        public HandshakeState ProtocolHandshakeState { get; set; }

        public RedisDatabase inMemoryDatabase { get; private set; }

        public Protocol(Dictionary<string, string> configuration, RedisDatabase inMemoryDatabase)
        {
            this.configuration = configuration;
            this.inMemoryDatabase = inMemoryDatabase;
        }

        public async Task Write(NetworkStream stream, string request)
        {
            try
            {
                if (request.Length > 0 && (request[0] == PLUS_CHAR || request[0] == DOLLAR_CHAR))
                    return;

                List<Command> parsedCommands = ParseRequest(request);
                CommandHandler commandHandler = new CommandHandler(inMemoryDatabase, IsMasterInstance, configuration, slaveStreams);
                
                foreach (var parsedCommand in parsedCommands)
                {
                    if (parsedCommand.CommandActions == null || parsedCommand.CommandActions.Count == 0)
                        return;

                    if (!Enum.TryParse(parsedCommand.CommandActions[0], true, out Commands action))
                    {
                        string errorMessage = ResponseHandler.ErrorResponse($"Unknown command: {parsedCommand.CommandActions[0]}");
                        await SendResponse(stream, errorMessage);
                        return;
                    }

                    string response = commandHandler.Handle(action, parsedCommand);

                    if (response != string.Empty && IsMasterInstance || action == Commands.GET || action == Commands.INFO)
                        await SendResponse(stream, response);

                    if (action == Commands.SET || action == Commands.GET || IsReplconfGetack(parsedCommand.CommandActions))
                    {
                        foreach (NetworkStream slaveStream in slaveStreams)
                        {
                            await SendResponse(slaveStream, ResponseHandler.ArrayResponse(parsedCommand.CommandActions.ToArray()));
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
                string errorMessage = ResponseHandler.ErrorResponse($"Error: {ex.Message} {ex.StackTrace}");
                await SendResponse(stream, errorMessage);
            }
        }

        private bool IsReplconfGetack(List<string> commandActions)
        {
            return commandActions.Count == 3 && commandActions[0] == "REPLCONF" && commandActions[1] == "GETACK" && commandActions[2] == "*";
        }

        public async Task HandleMasterSlaveHandshake(NetworkStream stream, string request)
        {
            string response = ProtocolHandshakeState switch
            {
                HandshakeState.PING => CreateReplconf1Response(request),
                HandshakeState.REPLCONF1 => CreateReplconf2Response(request),
                HandshakeState.REPLCONF2 => CreatePsyncResponse(request),
                HandshakeState.PSYNC => HandleFullresyncRequest(request),
                _ => string.Empty
            };

            if (response != string.Empty)
                await SendResponse(stream, response);
        }
        public async Task HandleSlaveCommand(NetworkStream stream, string fullRequest)
        {
            //When FULLRESYNC first request will be FULLRESYNC sd23l754emvgmz53hk5dwuo7m5oe6cx4tzvc0cv2 0
            //Second will be \$88\r\nREDIS0011?\tredis-ver♣7\.2\.0?\nredis-bits?@?♣ctime??eused-mem°?►aof-base???n;???Z? rdb file
            //TODO on FULLRESYNC state handle saving rdb file that is sent from master

            //Refactoring: handle each action as separate request, very important for slave
            //Make logical destincions between salve and master maybe to seperate classes
            //Move simple actions to separate class 

            //fullRequest = "\\+FULLRESYNC\\ 75cd7bc10c49047e0d163660f3b90625b1af31dc\\ 0\\r\\n\\$88\\r\\nREDIS0011�\\tredis-ver\u00057\\.2\\.0�\\nredis-bits�@�\u0005ctime��eused-mem°�\u0010aof-base���n;���Z�\\*3\\r\\n\\$8\\r\\nREPLCONF\\r\\n\\$6\\r\\nGETACK\\r\\n\\$1\\r\\n\\*\\r\\n";
            //fullRequest = "\\+FULLRESYNC\\ 75cd7bc10c49047e0d163660f3b90625b1af31dc\\ 0\\r\\n\\$88\\r\\nREDIS0011�\\tredis-ver\u00057\\.2\\.0�\\nredis-bits�@�\u0005ctime��eused-mem°�\u0010aof-base���n;���Z�\\*3\\r\\n\\$8\\r\\nREPLCONF\\r\\n\\$6\\r\\nGETACK\\r\\n\\$1\\r\\n\\*\\r\\n";
            //fullRequest = "\\$88\\r\\nREDIS0011�\\tredis-ver\u00057\\.2\\.0�\\nredis-bits�@�\u0005ctime��eused-mem°�\u0010aof-base���n;���Z�\\*3\\r\\n\\$8\\r\\nREPLCONF\\r\\n\\$6\\r\\nGETACK\\r\\n\\$1\\r\\n\\*\\r\\n";
            //fullRequest = "\\*1\\r\\n\\$4\\r\\nPING\\r\\n\\*3\\r\\n\\$8\\r\\nREPLCONF\\r\\n\\$6\\r\\nGETACK\\r\\n\\$1\\r\\n\\*\\r\\n";
            //fullRequest = "\\*3\\r\\n\\$3\\r\\nSET\\r\\n\\$3\\r\\nfoo\\r\\n\\$3\\r\\n123\\r\\n\\*3\\r\\n\\$3\\r\\nSET\\r\\n\\$3\\r\\nbar\\r\\n\\$3\\r\\n456\\r\\n\\*3\\r\\n\\$3\\r\\nSET\\r\\n\\$3\\r\\nbaz\\r\\n\\$3\\r\\n789\\r\\n";

            int emptyRdbIndex = fullRequest.IndexOf("$88");
            if (emptyRdbIndex != -1)
                fullRequest = fullRequest.Substring(emptyRdbIndex);

            List<string> requests = Regex.Split(fullRequest, @"\*\d+").ToList();

            if (requests.Count() == 1 && requests[0].IndexOf("$88") != -1)
                return;

            requests.RemoveAll(x => x.Length <= 1);
            requests.RemoveAll(x => x.IndexOf("$88") != -1);

            List<string> cplRequest = new List<string>();
            requests.ForEach(x =>
            {
                int paramsCount = Regex.Matches(x, @"\$\d+").Count();
                cplRequest.Add($"*{paramsCount}" + x);
            });

            foreach (string req in cplRequest)
            {
                if (req.Contains(Enum.GetName(typeof(HandshakeState), HandshakeState.FULLRESYNC) ?? "FULLRESYNC"))
                    continue;

                var commands = ParseRequest(req);

                await HandleWriteAsSlave(stream, req);

                if (IsReplconfGetack(commands[0].CommandActions))
                    await SendResponse(stream, ResponseHandler.ArrayResponse(["REPLCONF", "ACK", offset.ToString()]));
                offset += Encoding.ASCII.GetBytes(req).Length;
                Console.WriteLine($"Request => {Regex.Escape(req)} added {Encoding.ASCII.GetBytes(req).Length}");
            }
        }

        public string[] TrimCommandsFromRequest(string request)
        {
            // Split the string into lines
            string[] lines = request.Split(new[] { @"\r\n" }, StringSplitOptions.None);

            List<string> commands = new List<string>();
            List<string> currentCommand = new List<string>();

            foreach (string line in lines)
            {
                if (line.StartsWith("*") && currentCommand.Count > 0)
                {
                    // Save the current command as a single string
                    commands.Add(string.Join(@"\r\n", currentCommand));
                    currentCommand.Clear();
                }
                currentCommand.Add(line);
            }

            // Add the last command if it exists
            if (currentCommand.Count > 0)
            {
                commands.Add(string.Join(@"\r\n", currentCommand));
            }

            return lines;
        }

        public async Task SendPingRequest(NetworkStream stream)
        {
            string request = ResponseHandler.ArrayResponse([Enum.GetName(typeof(Commands), Commands.PING) ?? "PING"]);
            await SendResponse(stream, request);
        }

        private string CreateReplconf1Response(string request)
        {
            if (request != ResponseHandler.SimpleResponse(PING_RESPONSE))
                ResponseHandler.ErrorResponse("Recived: " + request + ", expected: " + ResponseHandler.SimpleResponse(PING_RESPONSE));

            string slavePort = configuration["port"];
            string repconfCommand = Enum.GetName(typeof(Commands), Commands.REPLCONF) ?? "REPLCONF";
            ProtocolHandshakeState = HandshakeState.REPLCONF1;
            return ResponseHandler.ArrayResponse([repconfCommand, "listening-port", slavePort.ToString()]);
        }

        private string CreateReplconf2Response(string request)
        {
            if (request != ResponseHandler.SimpleResponse(OK_RESPONSE))
                ResponseHandler.ErrorResponse("Recived: " + request + ", expected: " + ResponseHandler.SimpleResponse(OK_RESPONSE));

            string repconfCommand = Enum.GetName(typeof(Commands), Commands.REPLCONF) ?? "REPLCONF";
            ProtocolHandshakeState = HandshakeState.REPLCONF2;
            return ResponseHandler.ArrayResponse([repconfCommand, "capa", "psync2"]);
        }

        private string CreatePsyncResponse(string request)
        {
            if (request != ResponseHandler.SimpleResponse(OK_RESPONSE))
                ResponseHandler.ErrorResponse("Recived: " + request + ", expected: " + ResponseHandler.SimpleResponse(OK_RESPONSE));

            string repconfCommand = Enum.GetName(typeof(Commands), Commands.PSYNC) ?? "PSYNC";
            ProtocolHandshakeState = HandshakeState.PSYNC;
            return ResponseHandler.ArrayResponse([repconfCommand, "?", "-1"]);
        }

        private string HandleFullresyncRequest(string request)
        {
            if (!isFullresyncResponseValid(request))
                ResponseHandler.ErrorResponse($"Response {request} is not valid {Enum.GetName(typeof(HandshakeState), HandshakeState.FULLRESYNC)} pattern");

            string[] parameters = request.Split(' ');
            configuration["master_server_id"] = parameters[1];
            ProtocolHandshakeState = HandshakeState.FULLRESYNC;
            Console.WriteLine("Handshake completed with " + configuration["master_server_id"]);
            return string.Empty;
        }

        private async Task<string> HandleWriteAsSlave(NetworkStream stream, string request)
        {
            await Write(stream, request);
            return string.Empty;
        }

        private async Task SendResponse(NetworkStream stream, string response)
        {
            byte[] responseData = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseData, 0, responseData.Length);
            Console.WriteLine("Response sent: " + Regex.Escape(response));
        }

        private List<Command> ParseRequest(string request)
        {
            //Remove everything before first asterix
            int firstAst = request.IndexOf(ASTERISK_CHAR);
            request = request.Substring(firstAst);

            //Replace \\ with \
            request = request.Replace("\\*", "*")
                             .Replace("\\$", "$")
                             .Replace("\\?", "?")
                             .Replace("\\r", "\r")
                             .Replace("\\n", "\n");

            string[] requestList = Regex.Split(request, @"\*\d+");

            if (requestList.Length == 0)
                throw new ArgumentException($"{ASTERISK_CHAR} required as first parameter");

            List<Command> commands = new List<Command>();

            foreach (string command in requestList)
            {
                if (command.Length == 0 || command == "\\")
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

                foreach (int cmd in commandParamsToRemove)
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

    public class Command
    {
        public List<string> CommandActions { get; set; } = new List<string>();
        public Dictionary<string, string> CommandParams { get; set; } = new Dictionary<string, string>();
    }
}
