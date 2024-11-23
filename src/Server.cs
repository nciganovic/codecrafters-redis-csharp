using codecrafters_redis.src;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

// echo -ne "*2\r\n$4\r\nKEYS\r\n$1\r\n*\r\n" | nc localhost 6380

Dictionary<string, ItemValue> values = new Dictionary<string, ItemValue>();
Dictionary<string, string> parameters = CollectParameters(args);

int port = parameters.ContainsKey("port") && int.TryParse(parameters["port"], out _) ? Convert.ToInt32(parameters["port"]) : 6379;

TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();
Console.WriteLine($"Server started on port {port}. Waiting for connections...");

if (parameters.ContainsKey("dir") && parameters.ContainsKey("dbfilename"))
{
    string dir = $"{parameters["dir"]}/{parameters["dbfilename"]}";

    if (File.Exists(dir))
    { 
        var reader = new RDSFileReader(dir, true);
        values = reader.rdsDatabse;
    }
}

if (parameters.ContainsKey("replicaof"))
{
    string[] replicaofValues = parameters["replicaof"].Split(' ');
    string masterHost = replicaofValues[0];
    string masterPort = replicaofValues[1];

    int masterPortValue;
    bool isConverted = int.TryParse(masterPort, out masterPortValue);
    if (!isConverted)
        throw new Exception("Port value is not in valid format");

    TcpClient masterClient = new TcpClient();
    masterClient.Connect(masterHost, masterPortValue);
    Console.WriteLine("Connected to master:" + masterHost + " at " + masterPortValue);
    _ = HandleMasterServerAsync(masterClient);
}

while (true)
{   
    TcpClient client = await server.AcceptTcpClientAsync();
    Console.WriteLine("Client connected.");
    _ = HandleClientAsync(client, values, parameters);
}

static async Task HandleMasterServerAsync(TcpClient client)
{
    using (NetworkStream stream = client.GetStream())
    {
        string message = "*1\r\n$4\r\nPING\r\n";
        byte[] data = Encoding.UTF8.GetBytes(message);
        stream.Write(data, 0, data.Length);
        Console.WriteLine($"Sent: {message}");

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
            Console.WriteLine($"Received: {request}");
        }
    }
}

static async Task HandleClientAsync(TcpClient client, Dictionary<string, ItemValue> values, Dictionary<string, string> parameters)
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
            Console.WriteLine($"Received: {request}");

            // Process the data here if necessary, and prepare a response
            Protocol p = new Protocol(request, parameters);
            await p.Write(stream, values);
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
}