using static codecrafters_redis.src.RedisProtocolParser;

namespace codecrafters_redis.src
{


    public class RedisTransactions
    {
        public bool IsInitialized { get; private set; }
        List<RESPMessage> comamnds = new List<RESPMessage>();

        public int QueueLength => comamnds.Count;

        public void Begin()
        {
            IsInitialized = true;
        }

        public void AddCommand(RESPMessage command)
        {
            comamnds.Add(command);
        }

        public void Finish()
        {
            // Code for execuing the transaction would go here.

            IsInitialized = false;
        }
    }
}
