using static codecrafters_redis.src.Enums;

namespace codecrafters_redis.src
{
    public class CommandHandler
    {
        private readonly bool isMasterInstance;
        private readonly RedisDatabase inMemoryDatabase;
        private readonly Dictionary<string, string> configuration;

        public CommandHandler(RedisDatabase inMemoryDatabase, bool isMasterInstance, Dictionary<string, string> configuration)
        {
            this.inMemoryDatabase = inMemoryDatabase;
            this.isMasterInstance = isMasterInstance;   
            this.configuration = configuration;
        }

        public string Handle(Commands action, Command parsedCommand)
        {
            return action switch
            {
                Commands.PING => HandlePingCommand(),
                Commands.ECHO => HandleEchoCommand(parsedCommand),
                Commands.GET => HandleGetCommand(parsedCommand),
                Commands.SET => HandleSetCommand(parsedCommand),
                Commands.CONFIG => HandleConfigCommand(parsedCommand),
                Commands.KEYS => HandleKeysCommand(parsedCommand),
                Commands.INFO => HandleInfoCommand(parsedCommand),
                Commands.REPLCONF => HandleReplConfCommand(parsedCommand),
                Commands.PSYNC => HandlePsyncCommand(parsedCommand),
                _ => ResponseHandler.ErrorResponse($"Unknown command: {action}")
            };
        }

        private string HandlePingCommand()
        {
            return ResponseHandler.SimpleResponse(Constants.PING_RESPONSE);
        }

        private string HandleEchoCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 2)
                ResponseHandler.ErrorResponse("ECHO command excpects one parameter");

            return ResponseHandler.BulkResponse(parsedCommand.CommandActions[1]);
        }

        private string HandleGetCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 2)
                return ResponseHandler.ErrorResponse("GET expects one parameter");

            string key = parsedCommand.CommandActions[1];
            ItemValue item = inMemoryDatabase.Get(key);

            if (item.IsValid)
                return ResponseHandler.BulkResponse(item.Value);

            //inMemoryDatabase.Remove(key);
            return ResponseHandler.NullResponse();
        }

        private string HandleSetCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count < 2)
                ResponseHandler.ErrorResponse("SET command requiers at least 2 parameters, key and value");

            string key = parsedCommand.CommandActions[1];
            string value = parsedCommand.CommandActions[2];

            double timeToLive = double.MaxValue;

            if (parsedCommand.CommandActions.Count > 3 && parsedCommand.CommandActions[3].ToUpper() == Constants.PX)
                timeToLive = Convert.ToDouble(parsedCommand.CommandActions[4]);

            inMemoryDatabase.Set(key, new ItemValue(value, timeToLive));

            return isMasterInstance ? ResponseHandler.SimpleResponse(Constants.OK_RESPONSE) : string.Empty;
        }

        private string HandleConfigCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ResponseHandler.ErrorResponse("wrong number of parameters for config command");

            if (parsedCommand.CommandActions[1].ToUpper() != Enum.GetName(typeof(Commands), Commands.GET))
                ResponseHandler.ErrorResponse("Only get command for CONFIG action is currently supported");

            if (!configuration.ContainsKey(parsedCommand.CommandActions[2]))
                ResponseHandler.ErrorResponse($"Key {parsedCommand.CommandActions[2]} not found");

            string[] elements = { parsedCommand.CommandActions[2], configuration[parsedCommand.CommandActions[2]] };
            return ResponseHandler.ArrayResponse(elements);
        }

        private string HandleKeysCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 2)
                ResponseHandler.ErrorResponse("wrong number of arguments for 'keys' command");

            string pattern = parsedCommand.CommandActions[1];

            //select all keys
            if (pattern == $"{Constants.ASTERISK_CHAR}")
            {
                List<string> keys = new List<string>();
                foreach (string key in inMemoryDatabase.Keys)
                {
                    ItemValue item = inMemoryDatabase.Get(key);

                    if (item.IsValid)
                        keys.Add(key);
                    else
                        inMemoryDatabase.Remove(key);
                }

                return ResponseHandler.ArrayResponse(keys.ToArray());
            }

            return ResponseHandler.NullResponse();
        }

        private string HandleInfoCommand(Command parsedCommand)
        {
            Console.WriteLine("enter info command");

            if (parsedCommand.CommandActions.Count != 2)
                ResponseHandler.ErrorResponse("wrong number of arguments for 'info' command");

            string infoType = parsedCommand.CommandActions[1];
            if (infoType != "replication")
                ResponseHandler.ErrorResponse("unsupported type of info command");

            string role = isMasterInstance ? "master" : "slave";
            string info = $"role:{role}";

            string[] items = new string[3];
            items[0] = info;
            items[1] = $"master_replid:{configuration["server_id"]}";
            items[2] = "master_repl_offset:0";

            return ResponseHandler.BulkResponse(string.Join(Constants.SPACE_SING, items));
        }

        private string HandleReplConfCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ResponseHandler.ErrorResponse("wrong number of arguments for 'replconf' command");

            if (parsedCommand.CommandActions[1] == "GETACK" || parsedCommand.CommandActions[1] == "ACK")
                return string.Empty;

            configuration[parsedCommand.CommandActions[1]] = parsedCommand.CommandActions[2];

            return ResponseHandler.SimpleResponse(Constants.OK_RESPONSE);
        }

        private string HandlePsyncCommand(Command parsedCommand)
        {
            if (parsedCommand.CommandActions.Count != 3)
                ResponseHandler.ErrorResponse("wrong number of arguments for 'psync' command");

            return ResponseHandler.SimpleResponse($"{Enum.GetName(typeof(HandshakeState), HandshakeState.FULLRESYNC)} {configuration["server_id"]} 0");
        }
    }
}
