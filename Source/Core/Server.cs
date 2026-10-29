using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AgentBridge
{
    /// <summary>Minimal HTTP/1.1 server on a loopback TcpListener. Unlike HttpListener it needs no URL ACL or
    /// admin rights on Windows and behaves the same on Mono and CoreCLR. Connections are kept alive, so a
    /// persistent client (the MCP server) pays the TCP handshake once.</summary>
    internal sealed class Server
    {
        public delegate (int status, string body) Handler(string method, string path, Dictionary<string, string> headers, string body);
        private readonly TcpListener listener;
        private readonly Handler handler;
        private volatile bool running = true;
        private const int MaxConnections = 32;
        private int active;
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        public int Port { get; }

        private Server(TcpListener l, int port, Handler h) { listener = l; Port = port; handler = h; }

        public static Server? Start(int wanted, Handler handler)
        {
            for (int port = wanted; port < wanted + 10; port++)
            {
                var l = new TcpListener(IPAddress.Loopback, port);
                try { l.Start(); }
                catch (SocketException) { continue; }
                var s = new Server(l, port, handler);
                new Thread(s.Accept) { IsBackground = true, Name = "AgentBridge.Accept" }.Start();
                return s;
            }
            return null;
        }

        /// <summary>Stops listening and drops every open connection, so a kept-alive client cannot send more.</summary>
        public void Stop()
        {
            running = false;
            try { listener.Stop(); } catch { }
            lock (clients) { foreach (var c in clients) try { c.Close(); } catch { } clients.Clear(); }
        }

        private void Accept()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch { return; }
                // One thread per connection; a runaway client cannot exhaust threads.
                if (System.Threading.Interlocked.Increment(ref active) > MaxConnections) { System.Threading.Interlocked.Decrement(ref active); client.Close(); continue; }
                new Thread(() => { try { Serve(client); } finally { System.Threading.Interlocked.Decrement(ref active); } }) { IsBackground = true, Name = "AgentBridge.Conn" }.Start();
            }
        }

        private void Serve(TcpClient client)
        {
            lock (clients) { if (!running) { client.Close(); return; } clients.Add(client); }
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 15 * 60 * 1000;  // idle keep-alive connections are dropped after 15 minutes
                var stream = client.GetStream();
                try
                {
                    while (running)
                    {
                        string? requestLine = ReadLine(stream);
                        if (string.IsNullOrEmpty(requestLine)) return;
                        var parts = requestLine!.Split(' ');
                        if (parts.Length < 2) return;
                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string? line;
                        while (!string.IsNullOrEmpty(line = ReadLine(stream)))
                        {
                            int colon = line!.IndexOf(':');
                            if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                        }
                        // Malformed, negative or huge lengths close the connection instead of throwing: an unhandled
                        // exception on this background thread would terminate the whole game process.
                        int length = 0;
                        if (headers.TryGetValue("Content-Length", out var cl) && (!int.TryParse(cl, NumberStyles.None, CultureInfo.InvariantCulture, out length) || length > 8 * 1024 * 1024)) return;
                        var buffer = new byte[length];
                        for (int read = 0; read < length;)
                        {
                            int n = stream.Read(buffer, read, length - read);
                            if (n <= 0) return;
                            read += n;
                        }
                        if (!running) return;
                        (int status, string body) response;
                        try { response = handler(parts[0], parts[1], headers, Encoding.UTF8.GetString(buffer)); }
                        catch (Exception e) { response = (500, Bridge.ErrorJson(e.Message)); }
                        bool keepAlive = !(headers.TryGetValue("Connection", out var c) && c.Equals("close", StringComparison.OrdinalIgnoreCase));
                        var bytes = Encoding.UTF8.GetBytes(response.body);
                        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + response.status + " " + Reason(response.status) +
                            "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + bytes.Length +
                            "\r\nConnection: " + (keepAlive ? "keep-alive" : "close") + "\r\n\r\n");
                        stream.Write(head, 0, head.Length);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush();
                        if (!keepAlive) return;
                    }
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (Exception e) { Verse.Log.Warning("[AgentBridge] connection dropped: " + e.GetType().Name); }
                finally { lock (clients) clients.Remove(client); }
            }
        }

        private static string Reason(int s) => s switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 503 => "Service Unavailable", 504 => "Gateway Timeout", _ => "Error" };

        private static string? ReadLine(Stream s)
        {
            var sb = new StringBuilder();
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                if (b == '\n') return sb.ToString().TrimEnd('\r');
                if (sb.Length > 16384) throw new IOException("header too long");
                sb.Append((char)b);
            }
        }
    }
}
