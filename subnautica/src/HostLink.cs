using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SubnautiCraft
{
    /// <summary>
    /// A small WebSocket (RFC 6455) client for the Fabric mod's server on 127.0.0.1. It reconnects by itself.
    /// The camera pose is a single "latest" slot (only the newest is worth sending); everything else is queued.
    /// Received text messages wait in <see cref="Inbox"/> for the main thread.
    /// </summary>
    internal sealed class HostLink : IDisposable
    {
        private readonly int port;
        private readonly Thread thread;
        private readonly ConcurrentQueue<KeyValuePair<int, byte[]>> outbox = new ConcurrentQueue<KeyValuePair<int, byte[]>>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly object writeLock = new object();
        private volatile bool running = true;
        private volatile string latestCam;
        private TcpClient client;
        private Stream stream;

        public readonly ConcurrentQueue<string> Inbox = new ConcurrentQueue<string>();

        private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Seconds on the clock <see cref="LatestPovTime"/> uses.</summary>
        public static double Now => Clock.Elapsed.TotalSeconds;

        /// <summary>
        /// Minecraft's newest "pov" (Steve's camera, sent every Minecraft frame), kept apart from the queue with its
        /// arrival time: only the newest matters, and the camera is carried forward from when it arrived.
        /// </summary>
        public volatile string LatestPov;
        public double LatestPovTime;

        /// <summary>Set by the connection thread; the main thread watches it to resend its setup after a reconnect.</summary>
        public volatile bool Connected;

        public int Connections;

        public HostLink(int port)
        {
            this.port = port;
            thread = new Thread(Run) { IsBackground = true, Name = "SubnautiCraft link" };
            thread.Start();
        }

        public void SendCamera(string json)
        {
            latestCam = json;
            wake.Set();
        }

        public void Send(string json)
        {
            if (Connected)
            {
                outbox.Enqueue(new KeyValuePair<int, byte[]>(0x1, Encoding.UTF8.GetBytes(json)));
                wake.Set();
            }
        }

        /// <summary>A binary message (terrain triangles).</summary>
        public void SendBinary(byte[] data)
        {
            if (Connected)
            {
                outbox.Enqueue(new KeyValuePair<int, byte[]>(0x2, data));
                wake.Set();
            }
        }

        /// <summary>Messages waiting to go out (the terrain exporter waits while Minecraft catches up).</summary>
        public int Pending => outbox.Count;

        public void Dispose()
        {
            running = false;
            wake.Set();
            Close();
        }

        private void Run()
        {
            while (running)
            {
                try
                {
                    Connect();
                    Connected = true;
                    Interlocked.Increment(ref Connections);
                    var reader = new Thread(ReadLoop) { IsBackground = true, Name = "SubnautiCraft link reader" };
                    reader.Start();
                    while (running && Connected)
                    {
                        wake.WaitOne(100);
                        string cam = latestCam;
                        if (cam != null)
                        {
                            latestCam = null;
                            WriteFrame(0x1, Encoding.UTF8.GetBytes(cam));
                        }

                        while (outbox.TryDequeue(out KeyValuePair<int, byte[]> message))
                        {
                            WriteFrame(message.Key, message.Value);
                        }
                    }
                }
                catch (Exception e) when (e is IOException || e is SocketException || e is ObjectDisposedException || e is InvalidDataException)
                {
                    // Minecraft isn't running (yet), or went away: try again shortly
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"link: {e}");
                }

                if (Connected)
                {
                    Plugin.Log.LogInfo("Minecraft disconnected");
                }

                Connected = false;
                Close();
                while (outbox.TryDequeue(out _))
                {
                }

                for (int i = 0; i < 20 && running; i++)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private void Connect()
        {
            client = new TcpClient { NoDelay = true };
            client.Connect("127.0.0.1", port);
            stream = client.GetStream();
            byte[] nonce = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }

            string key = Convert.ToBase64String(nonce);
            string request = "GET / HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{port}\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: {key}\r\n" +
                "Sec-WebSocket-Version: 13\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);

            // the response headers end with an empty line
            var header = new StringBuilder();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int b = stream.ReadByte();
                if (b < 0 || header.Length > 8192)
                {
                    throw new IOException("handshake: connection closed");
                }

                header.Append((char)b);
            }

            string response = header.ToString();
            if (!response.StartsWith("HTTP/1.1 101", StringComparison.Ordinal))
            {
                throw new InvalidDataException("handshake refused: " + response.Split('\r')[0]);
            }

            Plugin.Log.LogInfo($"connected to Minecraft on 127.0.0.1:{port}");
        }

        private void ReadLoop()
        {
            var message = new MemoryStream();
            try
            {
                while (running && Connected)
                {
                    int b0 = ReadByte();
                    int b1 = ReadByte();
                    bool fin = (b0 & 0x80) != 0;
                    int opcode = b0 & 0x0F;
                    bool masked = (b1 & 0x80) != 0;
                    long length = b1 & 0x7F;
                    if (length == 126)
                    {
                        length = (ReadByte() << 8) | ReadByte();
                    }
                    else if (length == 127)
                    {
                        length = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            length = (length << 8) | (uint)ReadByte();
                        }
                    }

                    if (length > 64 * 1024 * 1024)
                    {
                        throw new InvalidDataException("frame too large");
                    }

                    byte[] mask = masked ? ReadExactly(4) : null;
                    byte[] payload = ReadExactly((int)length);
                    if (mask != null)
                    {
                        for (int i = 0; i < payload.Length; i++)
                        {
                            payload[i] ^= mask[i & 3];
                        }
                    }

                    switch (opcode)
                    {
                        case 0x0: // continuation
                        case 0x1: // text
                        case 0x2: // binary
                            message.Write(payload, 0, payload.Length);
                            if (fin)
                            {
                                string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                                message.SetLength(0);
                                if (text.StartsWith("{\"t\":\"pov\"", StringComparison.Ordinal))
                                {
                                    LatestPovTime = Now;
                                    LatestPov = text;
                                }
                                else
                                {
                                    Inbox.Enqueue(text);
                                }
                            }

                            break;
                        case 0x8: // close
                            Connected = false;
                            break;
                        case 0x9: // ping
                            WriteFrame(0xA, payload);
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // closed under us: the connection thread notices and reconnects
            }

            Connected = false;
            wake.Set();
        }

        private int ReadByte()
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                throw new IOException("connection closed");
            }

            return b;
        }

        private byte[] ReadExactly(int n)
        {
            byte[] buffer = new byte[n];
            int at = 0;
            while (at < n)
            {
                int got = stream.Read(buffer, at, n - at);
                if (got <= 0)
                {
                    throw new IOException("connection closed");
                }

                at += got;
            }

            return buffer;
        }

        /// <summary>One unfragmented frame; client frames are always masked.</summary>
        private void WriteFrame(int opcode, byte[] payload)
        {
            var frame = new MemoryStream(payload.Length + 14);
            frame.WriteByte((byte)(0x80 | opcode));
            if (payload.Length < 126)
            {
                frame.WriteByte((byte)(0x80 | payload.Length));
            }
            else if (payload.Length <= 0xFFFF)
            {
                frame.WriteByte(0x80 | 126);
                frame.WriteByte((byte)(payload.Length >> 8));
                frame.WriteByte((byte)payload.Length);
            }
            else
            {
                frame.WriteByte(0x80 | 127);
                long n = payload.Length;
                for (int i = 7; i >= 0; i--)
                {
                    frame.WriteByte((byte)(n >> (8 * i)));
                }
            }

            byte[] mask = new byte[4];
            mask[0] = (byte)Environment.TickCount;
            mask[1] = (byte)(Environment.TickCount >> 8);
            mask[2] = (byte)payload.Length;
            mask[3] = 0x5A;
            frame.Write(mask, 0, 4);
            for (int i = 0; i < payload.Length; i++)
            {
                frame.WriteByte((byte)(payload[i] ^ mask[i & 3]));
            }

            lock (writeLock)
            {
                Stream s = stream ?? throw new IOException("not connected");
                s.Write(frame.GetBuffer(), 0, (int)frame.Length);
            }
        }

        private void Close()
        {
            try
            {
                stream?.Dispose();
                client?.Close();
            }
            catch (Exception)
            {
                // already closed
            }

            stream = null;
            client = null;
        }
    }
}
