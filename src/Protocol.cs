using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;


// *n - number of parameters 
// $n - size of command

// *2\r\n$4\r\nECHO\r\n$3\r\nhey\r\n
// 2 commands, ECHO size 4, hey size 3
// split by \r\n
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

        private enum Commands
        { 
            PING,
            ECHO,
            GET,
            SET,
            PX, 
            CONFIG,
            KEYS,
            INFO
        }

        private readonly string command;
        private readonly Dictionary<string, string> serverParameters;

        public Protocol(string comamnd, Dictionary<string, string> parameters)
        {
            this.command = comamnd;
            this.serverParameters = parameters;   
        }

        public async Task Write(NetworkStream stream, Dictionary<string, ItemValue> values)
        {
            ParsedCommand parsedCommand = ParseCommand(command);
            
            if (parsedCommand.CommandActions == null)
                return;

            string action = parsedCommand.CommandActions[0];
            string response = string.Empty;

            if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.PING))
                response = SimpleResponse(PING_RESPONSE);
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.ECHO))
            {
                if (parsedCommand.CommandActions.Count != 2)
                    throw new Exception("ECHO comamnd excpects one parameter");

                response = BulkResponse(parsedCommand.CommandActions[1]);
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.GET))
            {
                string key = parsedCommand.CommandActions[1];

                if (values.ContainsKey(key))
                {
                    if (values[key].IsValid)
                        response = BulkResponse(values[key].Value);
                    else
                    {
                        values.Remove(key);
                        response = NullResponse();
                    }
                }
                else
                    response = NullResponse();
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.SET))
            {
                if (parsedCommand.CommandActions.Count < 2)
                    throw new Exception("SET command requiers at least 2 parameters, key and value");

                string key = parsedCommand.CommandActions[1];
                string value = parsedCommand.CommandActions[2];

                double timeToLive = double.MaxValue;

                if (parsedCommand.CommandActions.Count > 3 && parsedCommand.CommandActions[3].ToUpper() == Enum.GetName(typeof(Commands), Commands.PX))
                    timeToLive = Convert.ToDouble(parsedCommand.CommandActions[4]);

                values[key] = new ItemValue(value, timeToLive);

                response = SimpleResponse(OK_RESPONSE);
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.CONFIG))
            {
                if (parsedCommand.CommandActions.Count != 3)
                    throw new Exception("wrong number of parameters for config command");

                if (parsedCommand.CommandActions[1].ToUpper() == Enum.GetName(typeof(Commands), Commands.GET))
                {
                    if (serverParameters.ContainsKey(parsedCommand.CommandActions[2]))
                    {
                        string[] elements = { parsedCommand.CommandActions[2], serverParameters[parsedCommand.CommandActions[2]] };
                        response = ArrayResponse(elements);
                    }
                }
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.KEYS))
            {
                if (parsedCommand.CommandActions.Count != 2)
                    throw new Exception("wrong number of arguments for 'keys' command");

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

                    response = ArrayResponse(keys.ToArray());
                }
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.INFO))
            {
                Console.WriteLine("enter info command");

                if(parsedCommand.CommandActions.Count != 2)
                    throw new Exception("wrong number of arguments for 'info' command");

                string infoType = parsedCommand.CommandActions[1];
                if (infoType != "replication")
                    throw new Exception("unsupported type of info command");

                string role = serverParameters.ContainsKey("replicaof") ? "slave" : "master";
                string info = $"role:{role}";

                string[] items = new string[3];
                items[0] = info;
                items[1] = $"master_replid:{GenerateAlphanumericString()}";
                items[2] = "master_repl_offset:0";

                response = BulkResponse(string.Join(SPACE_SING, items));
            }

            byte[] responseData = Encoding.UTF8.GetBytes(response.ToString());
            await stream.WriteAsync(responseData, 0, responseData.Length);
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

        public static string GenerateAlphanumericString(int length = 40)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var result = new StringBuilder(length);

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }

            return result.ToString();
        }
    }

    class ParsedCommand
    {
        public List<string>? CommandActions { get; set; }
        public Dictionary<string, string> CommandParams { get; set; } = new Dictionary<string, string>();
    }
}
