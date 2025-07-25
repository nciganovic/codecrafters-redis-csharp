using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using static codecrafters_redis.src.RedisProtocolParser;

namespace codecrafters_redis.src
{
    public record Transaction(Socket socket)
    {
        public bool IsStarted = false;
        public Socket Socket = socket;
        public List<RESPMessage> Commands = new List<RESPMessage>();
        public List<string> Responses = new List<string>();

        public void Start()
        {
            IsStarted = true;
        }

        public void Stop()
        {
            IsStarted = false;
        }
    }

    public class RedisTransactions(RedisServer redisServer)
    {
        private RedisServer redisServer = redisServer;
        List<Transaction> transactions = new List<Transaction>();

        public void InitalizeTransaction(Socket socket)
        {
            Transaction t = new Transaction(socket);
            t.Start();
            if (!transactions.Contains(t))
                transactions.Add(t);
        }

        public Transaction? GetTransaction(Socket socket)
        {
            return transactions.FirstOrDefault(x => x.Socket == socket);
        }


        public bool IsTransactionRunning(Socket socket)
        {
            return GetTransaction(socket)?.IsStarted ?? false;
        }

        public void AddCommand(Socket socket, RESPMessage command)
        {
            Transaction? transaction = GetTransaction(socket);
            if (transaction != null)
                transaction.Commands.Add(command);
        }

        public void RemoveTransaction(Socket socket)
        {
            transactions.RemoveAll(t => t.Socket == socket);
        }

        public void AddResponse(Socket socket, string response)
        {
            Transaction? transaction = GetTransaction(socket);
            if (transaction != null)
                transaction.Responses.Add(response);
            else
                throw new Exception("Transaction not started for this socket.");
        }
    }
}
