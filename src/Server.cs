using codecrafters_redis.src;

//RespGenerator respGenerator = new RespGenerator();

List<string> cliArgs = args.ToList();

CLIArgumentsBuilder argBuilder = new CLIArgumentsBuilder(cliArgs);
CLIArguments arguments = argBuilder.Build();

RedisServerBuilder serverBuilder = new RedisServerBuilder(arguments);
var server = serverBuilder.Build();

server.StartServer();

/*
// echo -ne "*2\r\n$4\r\nKEYS\r\n$1\r\n*\r\n" | nc localhost 6380

RedisDatabase inMemoryDatabase = new RedisDatabase();
Dictionary<string, string> configuration = CollectParameters(args);

const int defaultPort = 6379;
int port = configuration.ContainsKey("port") && int.TryParse(configuration["port"], out _) ? Convert.ToInt32(configuration["port"]) : defaultPort;
configuration.Add("server_id", Generate.AlphanumericString());

TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();

Console.WriteLine($"Server {configuration["server_id"]} started on port {port}. Waiting for connections...");

if (configuration.ContainsKey("dir") && configuration.ContainsKey("dbfilename"))
{
    string dir = $"{configuration["dir"]}/{configuration["dbfilename"]}";

    if (File.Exists(dir))
    {
        var reader = new codecrafters_redis.src.RDSFileReader(dir, true);
        inMemoryDatabase = reader.Database;
    }
}

Protocol protocol = new Protocol(configuration, inMemoryDatabase);

if (configuration.ContainsKey("replicaof"))
{
    string[] replicaofValues = configuration["replicaof"].Split(' ');
    string masterHost = replicaofValues[0];
    string masterPort = replicaofValues[1];

    int masterPortValue;
    bool isConverted = int.TryParse(masterPort, out masterPortValue);
    if (!isConverted)
        throw new Exception("Port value is not in valid format");

    TcpClient masterClient = new TcpClient();
    masterClient.Connect(masterHost, masterPortValue);
    Console.WriteLine("Connected to master:" + masterHost + " at " + masterPortValue);
    _ = HandleMasterServerAsync(masterClient, protocol);
}

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();
    Console.WriteLine("Client connected.");
    _ = HandleClientAsync(client, protocol);
}

static async Task HandleMasterServerAsync(TcpClient client, Protocol protocol)
{
    using (NetworkStream stream = client.GetStream())
    {
        Console.WriteLine("Sending Ping request...");
        await protocol.SendPingRequest(stream);

        byte[] buffer = new byte[1024];
        bool connected = true;

        protocol.ProtocolHandshakeState = HandshakeState.PING;

        while (connected)
        {
            // Receive data from the client
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            if (bytesRead == 0)
            {
                connected = false;
                break;
            }

            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Slave Received: {Regex.Escape(request)}");

            if (request.IndexOf("ERROR") != -1)
                break;

            // Process the data here if necessary, and prepare a response
            await protocol.HandleMasterSlaveHandshake(stream, request);

            if (protocol.ProtocolHandshakeState == HandshakeState.FULLRESYNC)
                await protocol.HandleSlaveCommand(stream, request);

        }
    }
}

static async Task HandleClientAsync(TcpClient client, Protocol protocol)
{
    using (NetworkStream stream = client.GetStream())
    {
        byte[] buffer = new byte[1024];
        bool connected = true;

        while (connected)
        {
            // Receive data from the client
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            if (bytesRead == 0) // Client disconnected
            {
                connected = false;
                break;
            }

            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine($"Master Received: {Regex.Escape(request)}");

            if (request.IndexOf("ERROR") != -1)
                break;

            // Process the data here if necessary, and prepare a response
            await protocol.Write(stream, request);
        }
    }

    client.Close();
    Console.WriteLine("Client disconnected.");
}

static Dictionary<string, string> CollectParameters(string[] args)
{
    Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();
    if (args.Length > 0)
    {
        for (int i = 0; i < args.Length; i += 2)
        {
            if (args[i].IndexOf("--") != -1)
                args[i] = args[i].Replace("--", "");

            keyValuePairs[args[i]] = args[i + 1];
        }
    }
    return keyValuePairs;
}*/