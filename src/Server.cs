using codecrafters_redis.src;
using System.Net;
using System.Net.Sockets;
using System.Text;

int port = 6379;
TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();
Console.WriteLine($"Server started on port {port}. Waiting for connections...");
Dictionary<string, ItemValue> values = new Dictionary<string, ItemValue>();

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();
    Console.WriteLine("Client connected.");
    _ = HandleClientAsync(client, values);
}

static async Task HandleClientAsync(TcpClient client, Dictionary<string, ItemValue> values)
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
            Protocol p = new Protocol(request);
            await p.Write(stream, values);

            Console.WriteLine("Response sent to client.");
        }
    }

    client.Close();
    Console.WriteLine("Client disconnected.");
}