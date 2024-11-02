using Common;
using System.Net;
using System.Net.Sockets;
using System.Text;

int port = 6379;
TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();
//Console.WriteLine($"Server started on port {port}. Waiting for connections...");

while (true)
{
    TcpClient client = await server.AcceptTcpClientAsync();
    //Console.WriteLine("Client connected.");
    _ = HandleClientAsync(client);
}

static async Task HandleClientAsync(TcpClient client)
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
            //Console.WriteLine($"Received: {request}");

            // Process the data here if necessary, and prepare a response
            string response = $"Server received: {request}";

            if (request == Constants.PING_REQUEST_COMMAND)
                Console.WriteLine(Constants.PING_RESPOSNSE);

            byte[] responseData = Encoding.UTF8.GetBytes(response);

            // Send response back to the client
            await stream.WriteAsync(responseData, 0, responseData.Length);
            //Console.WriteLine("Response sent to client.");
        }
    }

    client.Close();
    //Console.WriteLine("Client disconnected.");
}