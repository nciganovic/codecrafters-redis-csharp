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
        private readonly Dictionary<string, string> parameters;

        public Protocol(string comamnd, Dictionary<string, string> parameters)
        {
            this.command = comamnd;
            this.parameters = parameters;   
        }

        public async Task Write(NetworkStream stream, Dictionary<string, ItemValue> values)
        {
            List<string>? commandParams = ParseCommand(command);
            
            if (commandParams == null)
                return;

            string action = commandParams[0];

            string response = string.Empty;

            if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.PING))
                response = SimpleResponse(PING_RESPONSE);
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.ECHO))
            {
                if (commandParams.Count != 2)
                    throw new Exception("ECHO comamnd excpects one parameter");

                response = BulkResponse(commandParams[1]);
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.GET))
            {
                string key = commandParams[1];

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
                if (commandParams.Count < 2)
                    throw new Exception("SET command requiers at least 2 parameters, key and value");

                string key = commandParams[1];
                string value = commandParams[2];

                double timeToLive = double.MaxValue;

                if (commandParams.Count > 3 && commandParams[3].ToUpper() == Enum.GetName(typeof(Commands), Commands.PX))
                    timeToLive = Convert.ToDouble(commandParams[4]);

                values[key] = new ItemValue(value, timeToLive);

                response = SimpleResponse(OK_RESPONSE);
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.CONFIG))
            {
                if (commandParams.Count != 3)
                    throw new Exception("wrong number of parameters for config command");

                if (commandParams[1].ToUpper() == Enum.GetName(typeof(Commands), Commands.GET))
                {
                    if (parameters.ContainsKey(commandParams[2]))
                    {
                        string[] elements = { commandParams[2], parameters[commandParams[2]] };
                        response = ArrayResponse(elements);
                    }
                }
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.KEYS))
            {
                if (commandParams.Count != 2)
                    throw new Exception("wrong number of arguments for 'keys' command");

                string pattern = commandParams[1];

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
                if(commandParams.Count != 2)
                    throw new Exception("wrong number of arguments for 'info' command");

                string infoType = commandParams[1];
                if (infoType != "replication")
                    throw new Exception("unsupported type of info command");

                string role = "master";
                string info = $"role:{role}";

                response = BulkResponse(info);
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

        private List<string>? ParseCommand(string command)
        {
            List<string> commandParams = command.Split(SPACE_SING).ToList();
            
            if (commandParams.Count == 0)
                throw new ArgumentException($"Invalid command, no \\r\\n found");

            if (commandParams[0].IndexOf(ASTERISK_CHAR) == -1)
                throw new ArgumentException($"{ASTERISK_CHAR} required as first parameter");

            int paramCount = Convert.ToInt32(commandParams[0].Split(ASTERISK_CHAR)[1]);

            if (paramCount == -1)
                return null;

            if(paramCount == 0)
                return new List<string>();

            commandParams.RemoveAt(0);
            commandParams.RemoveAt(commandParams.Count - 1);
            commandParams.RemoveAll(x => x.IndexOf('$') != -1);
            commandParams.RemoveAll(x => x == string.Empty);
            return commandParams;
        }
    }
}
