namespace codecrafters_redis.src
{
    public record CLIArguments(string? dir, string? dbFileName, int portNumber, string role, string? leaderHost, int? leaderPort);

    public class CLIArgumentsBuilder
    {
        private List<string> _cliArgs;
        private string? _dir;
        private string? _dbFileName;
        private string? _port;
        private int _portNumber;
        private string? _replicaOf;
        private string? _role;
        private string? _leaderHost;
        private int? _leaderPort;

        public CLIArgumentsBuilder(List<string> cliArgs)
        {
            _cliArgs = cliArgs;
            _portNumber = 6379;
        }

        public CLIArguments Build()
        {
            _SetAllCLIArguments();
            return new CLIArguments(_dir, _dbFileName, _portNumber, _role, _leaderHost, _leaderPort);
        }

        private void _SetAllCLIArguments()
        {
            _dir = _GetCLIArgument(_cliArgs, "--dir");
            _dbFileName = _GetCLIArgument(_cliArgs, "--dbfilename");
            _port = _GetCLIArgument(_cliArgs, "--port");
            _replicaOf = _GetCLIArgument(_cliArgs, "--replicaof");

            _leaderHost = _ParseLeaderHostFromArg(_replicaOf);
            _leaderPort = _ParseLeaderPortFromArg(_replicaOf);


            if (_port is not null)
            {
                _portNumber = Int32.Parse(_port);
            }


            if (_replicaOf is null)
            {
                //change to leader down the line
                _role = "leader";
            }
            else
            {
                //change to follower down the line
                _role = "follower";
            }
        }


        private string? _GetCLIArgument(List<string> cliArgs, string arg)
        {
            var dirIndex = cliArgs.IndexOf(arg);
            if (dirIndex != -1)
            {
                try
                {
                    return cliArgs[dirIndex + 1];
                }
                catch (ArgumentOutOfRangeException _)
                {
                    return null;
                }
            }
            else
            {
                return null;
            }
        }

        private string? _ParseLeaderHostFromArg(string? argument)
        {
            if (argument == null)
            {
                return null;
            }
            else
            {
                var argumentWithPortRemoved = argument.Remove((argument.Length - 5), 5);
                return argumentWithPortRemoved;
            }

        }

        static int? _ParseLeaderPortFromArg(string? argument)
        {
            if (argument == null)
            {
                return null;
            }
            else
            {
                return Int32.Parse(argument.Substring(argument.Length - 4));
            }
        }
    }
}
