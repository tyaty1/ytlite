// Minimal HTTP/HTTPS (CONNECT) proxy for testing on the Android emulator when the emulator's own
// DNS forwarding is blocked: the host resolves names, the emulator only needs TCP to 10.0.2.2.
//
//   dotnet run android/tools/DevProxy.cs [port]
//   adb shell settings put global http_proxy 10.0.2.2:8888      (undo: settings put global http_proxy :0)
//
// Listens on 127.0.0.1 only (the emulator reaches it as 10.0.2.2). Test use only.
using System.Net;
using System.Net.Sockets;
using System.Text;

int port = args.Length > 0 ? int.Parse(args[0]) : 8888;
var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"dev proxy on 127.0.0.1:{port}");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => HandleAsync(client));
}

static async Task HandleAsync(TcpClient client)
{
    using var _c = client;
    var cs = client.GetStream();
    try
    {
        // Read the request head byte by byte up to the blank line (small, once per connection).
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 65536)
        {
            if (await cs.ReadAsync(one) == 0) return;
            head.Add(one[0]);
            int n = head.Count;
            if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
        }
        var lines = Encoding.Latin1.GetString(head.ToArray()).Split("\r\n");
        var parts = lines[0].Split(' ', 3);
        if (parts.Length < 3) return;
        string method = parts[0], target = parts[1], version = parts[2];

        using var upstream = new TcpClient();
        if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
        {
            int colon = target.LastIndexOf(':');
            await upstream.ConnectAsync(target[..colon], int.Parse(target[(colon + 1)..]));
            await cs.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection established\r\n\r\n"));
        }
        else
        {
            var uri = new Uri(target);
            await upstream.ConnectAsync(uri.Host, uri.Port);
            var sb = new StringBuilder($"{method} {uri.PathAndQuery} {version}\r\n");
            foreach (var h in lines.Skip(1))
                if (h.Length > 0 && !h.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)) sb.Append(h).Append("\r\n");
            sb.Append("\r\n");
            await upstream.GetStream().WriteAsync(Encoding.Latin1.GetBytes(sb.ToString()));
        }

        var us = upstream.GetStream();
        var a = cs.CopyToAsync(us);
        var b = us.CopyToAsync(cs);
        await Task.WhenAny(a, b);
    }
    catch { /* client or upstream went away */ }
}
