namespace codecrafters_redis.src
{
    public static class Enums
    {
        public enum Commands
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

        public enum HandshakeState
        {
            NONE,
            PING,
            REPLCONF1,
            REPLCONF2,
            PSYNC,
            FULLRESYNC,
            COMPLETED
        }
    }
}
