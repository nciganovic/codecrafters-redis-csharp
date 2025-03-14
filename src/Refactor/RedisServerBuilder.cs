namespace codecrafters_redis.src
{
    internal class RedisServerBuilder
    {
        private CLIArguments _arguments;

        public RedisServerBuilder(CLIArguments arguments)
        {
            _arguments = arguments;
        }

        public IRedisServer Build()
        {

            if (_arguments.role == "leader")
            {
                return new RedisLeader(_arguments.dir, _arguments.dbFileName, _arguments.portNumber);

            }
            else
            {
                // if (_arguments.role == "follower")
                Console.Out.WriteLine("Creating a Replica!");
                int leaderPort = _arguments.leaderPort ?? 6398;

                return new RedisFollower(_arguments.dir, _arguments.dbFileName, _arguments.portNumber, _arguments.leaderHost, leaderPort);
            }

        }
    }
}
