using codecrafters_redis.src;
using System.Net;
using System.Net.Sockets;
using System.Text;

// echo -ne "*2\r\n$4\r\nKEYS\r\n$1\r\n*\r\n" | nc localhost 6380

int port = 6379;
TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();
Console.WriteLine($"Server started on port {port}. Waiting for connections...");

Dictionary<string, ItemValue> values = new Dictionary<string, ItemValue>();
Dictionary<string, string> parameters = CollectParameters(args);

if (parameters.ContainsKey("dir") && parameters.ContainsKey("dbfilename"))
{
    string dir = $"{parameters["dir"]}/{parameters["dbfilename"]}";

    if (File.Exists(dir))
    { 
        var reader = new RDSFileReader(dir, true);
        values = reader.rdsDatabse;
    }
}

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();
    Console.WriteLine("Client connected.");
    _ = HandleClientAsync(client, values, parameters);
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

            Console.WriteLine("Response sent to client.");
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