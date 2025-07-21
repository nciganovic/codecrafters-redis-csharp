using System.Net.Sockets;
using static codecrafters_redis.src.RedisProtocolParser;

namespace codecrafters_redis.src
{


    public class RedisTransactions
    {
        Dictionary<Socket, List<RESPMessage>> transactionsDict = new Dictionary<Socket, List<RESPMessage>>();

        public void Begin(Socket socket)
        {
            transactionsDict.Add(socket, []);
        }

        public bool IsStarted(Socket socket)
        {
            return transactionsDict.ContainsKey(socket);
        }

        public List<RESPMessage> GetCommands(Socket socket)
        {
            if (transactionsDict.ContainsKey(socket))
                return transactionsDict[socket];
            else
                throw new Exception("Transaction not started for this socket.");
        }

        public void AddCommand(Socket socket, RESPMessage command)
        {
            if (transactionsDict.ContainsKey(socket))
                transactionsDict[socket].Add(command);
            else
                transactionsDict.Add(socket, [command]);
        }

        public void AddCommands(Socket socket, List<RESPMessage> command)
        {
            if (transactionsDict.ContainsKey(socket))
                transactionsDict[socket].AddRange(command);
            else
                transactionsDict.Add(socket, command);
        }

        public void Finish(Socket socket)
        {
            // Code for executing the transaction

            transactionsDict.Remove(socket);
        }
    }

    public record Transaction(Socket socket, List<RESPMessage> commands)
    { 
        public bool IsInitialized { get; private set; } = false;
        public Socket Socket { get; private set; } = socket;
        public List<RESPMessage> Commands { get; private set; } = new List<RESPMessage>(commands);
    }
}
