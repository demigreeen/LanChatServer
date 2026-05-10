using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private static readonly List<TcpClient> clients = new();
    private static readonly Dictionary<TcpClient, string> fileServers = new();
    private static readonly object lockObj = new();

    static async Task Main()
    {
        int port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8080");
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();

        Console.WriteLine($"Сервер запущен на порту {port}");

        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            Console.WriteLine($"Подключился: {client.Client.RemoteEndPoint}");
            _ = HandleClient(client);
        }
    }

    static async Task HandleClient(TcpClient client)
    {
        lock (lockObj)
            clients.Add(client);

        var stream = client.GetStream();
        var buffer = new byte[65536];

        try
        {
            while (true)
            {
                // Читаем длину сообщения (4 байта)
                byte[] lenBytes = new byte[4];
                int read = await stream.ReadAsync(lenBytes, 0, 4);
                if (read == 0) break;

                int msgLen = BitConverter.ToInt32(lenBytes, 0);
                byte[] msgBytes = new byte[msgLen];
                int received = 0;

                while (received < msgLen)
                {
                    int r = await stream.ReadAsync(msgBytes, received, msgLen - received);
                    if (r == 0) break;
                    received += r;
                }

                string message = Encoding.UTF8.GetString(msgBytes);
                Console.WriteLine($"Сообщение: {message}");

                // Рассылаем всем включая отправителя
                await BroadcastAsync(message, msgBytes);
            }
        }
        catch { }
        finally
        {
            lock (lockObj)
            {
                clients.Remove(client);
                fileServers.Remove(client);
            }
            client.Close();
            Console.WriteLine("Клиент отключился");
        }
    }

    static async Task BroadcastAsync(string message, byte[] msgBytes)
    {
        byte[] lenBytes = BitConverter.GetBytes(msgBytes.Length);

        List<TcpClient> snapshot;
        lock (lockObj)
            snapshot = new List<TcpClient>(clients);

        foreach (var c in snapshot)
        {
            try
            {
                var stream = c.GetStream();
                await stream.WriteAsync(lenBytes, 0, lenBytes.Length);
                await stream.WriteAsync(msgBytes, 0, msgBytes.Length);
            }
            catch { }
        }
    }
}