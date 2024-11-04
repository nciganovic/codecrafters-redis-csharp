using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;


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

        private enum Commands
        { 
            PING,
            ECHO,
            GET,
            SET,
        }

        private readonly string command;

        public Protocol(string comamnd)
        {
            this.command = comamnd;
            List<string> parameters = new List<string>();

        }

        public async Task Write(NetworkStream stream)
        {
            List<string>? parameters = ParseCommand(command);
            
            if (parameters == null)
                return;

            string action = parameters[0];

            StringBuilder response = new StringBuilder();
            
            if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.PING))
            {
                response.Append(PLUS_CHAR);
                response.Append(PING_RESPONSE);
                response.Append(SPACE_SING);
            }
            else if (action.ToUpper() == Enum.GetName(typeof(Commands), Commands.ECHO))
            {
                if (parameters.Count != 2)
                    throw new Exception("ECHO comamnd excpects one parameter");

                response.Append(DOLLAR_CHAR);
                response.Append(parameters[1].Length);
                response.Append(SPACE_SING);
                response.Append(parameters[1]);
                response.Append(SPACE_SING);
            }

            byte[] responseData = Encoding.UTF8.GetBytes(response.ToString());

            await stream.WriteAsync(responseData, 0, responseData.Length);
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
            return commandParams;
        }
    }
}
