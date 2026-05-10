using System.Net;
using System.Net.Sockets;
using System.Text;

class Server
{
    private static readonly List<TcpClient> clients = new();
    private static readonly object lockObj = new();
    private static int clickCount = 0; // ← счётчик

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

        BroadcastOnlineCount();
        SendClickCount(client); // ← отправить текущий счётчик новому клиенту

        var stream = client.GetStream();
        try
        {
            while (true)
            {
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

                if (message == "CLICK") // ← обработка клика
                {
                    lock (lockObj)
                        clickCount++;
                    Console.WriteLine($"Клик! Всего: {clickCount}");
                    BroadcastClickCount();
                }
                else
                {
                    Console.WriteLine($"Сообщение: {message}");
                    await BroadcastAsync(msgBytes);
                }
            }
        }
        catch { }
        finally
        {
            lock (lockObj)
                clients.Remove(client);
            client.Close();
            Console.WriteLine("Клиент отключился");
            BroadcastOnlineCount();
        }
    }

    static void SendClickCount(TcpClient client)
    {
        try
        {
            string message = $"CLICKCOUNT|{clickCount}";
            byte[] msgBytes = Encoding.UTF8.GetBytes(message);
            byte[] lenBytes = BitConverter.GetBytes(msgBytes.Length);
            var stream = client.GetStream();
            stream.Write(lenBytes, 0, lenBytes.Length);
            stream.Write(msgBytes, 0, msgBytes.Length);
        }
        catch { }
    }

    static void BroadcastClickCount()
    {
        string message = $"CLICKCOUNT|{clickCount}";
        byte[] msgBytes = Encoding.UTF8.GetBytes(message);
        byte[] lenBytes = BitConverter.GetBytes(msgBytes.Length);

        List<TcpClient> snapshot;
        lock (lockObj)
            snapshot = new List<TcpClient>(clients);

        foreach (var c in snapshot)
        {
            try
            {
                var stream = c.GetStream();
                stream.Write(lenBytes, 0, lenBytes.Length);
                stream.Write(msgBytes, 0, msgBytes.Length);
            }
            catch { }
        }
    }

    static void BroadcastOnlineCount()
    {
        int count;
        lock (lockObj)
            count = clients.Count;

        string message = $"ONLINE|{count}";
        byte[] msgBytes = Encoding.UTF8.GetBytes(message);
        byte[] lenBytes = BitConverter.GetBytes(msgBytes.Length);

        List<TcpClient> snapshot;
        lock (lockObj)
            snapshot = new List<TcpClient>(clients);

        foreach (var c in snapshot)
        {
            try
            {
                var stream = c.GetStream();
                stream.Write(lenBytes, 0, lenBytes.Length);
                stream.Write(msgBytes, 0, msgBytes.Length);
            }
            catch { }
        }
    }

    static async Task BroadcastAsync(byte[] msgBytes)
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