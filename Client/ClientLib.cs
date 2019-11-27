using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client
{
    public class TcpConnection
    {
        object _lock = new object();
        bool _is_busy = false;
        public bool TakeLock()
        {
            lock (_lock)
            {
                if (_is_busy)
                {
                    return false;
                }
                else
                {
                    _is_busy = true;
                    return true;
                }
            }
        }
        public void ReleaseLock()
        {
            _is_busy = false;
        }
        public bool Connected { get; set; }
        public string ConnError { get; set; }
        public Socket client { get; set; }
        public Stream stream { get; set; }
        public BinaryWriter bw { get; set; }
        public DateTime LastUsed { get; set; }
        public int Index { get; set; }
        public TcpConnection(string hostname, int port)
        {
            client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            SocketAsyncEventArgs connectEventArg = new SocketAsyncEventArgs();
            connectEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(ConnectedEvent);
            connectEventArg.UserToken = this;
            connectEventArg.RemoteEndPoint = new IPEndPoint(IPAddress.Parse(hostname), port);
            var connected = client.ConnectAsync(connectEventArg);
            if (!connected)
            {
                if (connectEventArg.SocketError != SocketError.Success)
                {
#if (VERBOSE)
                    Console.WriteLine("Connection error (immediate)");
#endif
                    throw new Exception("Linqdb: Connection error (immediate)");
                }
#if (VERBOSE)
                Console.WriteLine("Connected immediately");
#endif
                //client.NoDelay = true;
                client.ReceiveTimeout = 60000;
                client.SendTimeout = 60000;
                this.stream = new NetworkStream(client);
                this.bw = new BinaryWriter(stream);
            }
            else
            {
                int total_wait_ms = 0;
                while (!this.Connected)
                {
                    Thread.Sleep(100);

                    total_wait_ms += 100;
#if (VERBOSE)
                    if (total_wait_ms % 2000 == 0)
                    {
                        Console.WriteLine("Can't connect in {0} ms", total_wait_ms);
                    }
#endif
                }
                if (!string.IsNullOrEmpty(this.ConnError))
                {
                    throw new Exception(this.ConnError + "  after " + total_wait_ms + " ms wait time");
                }
                else
                {
#if (VERBOSE)
                    Console.WriteLine("Connected {0} ms", total_wait_ms);
#endif
                }
            }
            _is_busy = true;
            LastUsed = DateTime.Now;
        }
        private void ConnectedEvent(object sender, SocketAsyncEventArgs e)
        {
            TcpConnection conn = e.UserToken as TcpConnection;
            if (e.SocketError != SocketError.Success)
            {
#if (VERBOSE)
                Console.WriteLine("Connection error");
#endif
                conn.ConnError = "Connection error";
                conn.Connected = true;
                return;
            }
            //e.ConnectSocket.NoDelay = true;
            e.ConnectSocket.ReceiveTimeout = 60000;
            e.ConnectSocket.SendTimeout = 60000;

            conn.stream = new NetworkStream(conn.client);
            conn.bw = new BinaryWriter(conn.stream);
            conn.ConnError = null;
            conn.Connected = true;
        }
    }

    public class ClientSockets
    {

        const int _limit = 100;
        TcpConnection[] cons = new TcpConnection[_limit];
        object _lock = new object();
        object[] _locks = null;

        public byte[] CallServer(byte[] input, string hostname, int port, out string error_msg)
        {
            error_msg = null;
            if (_locks == null)
            {
                lock (_lock)
                {
                    if (_locks == null)
                    {
                        _locks = new object[_limit];
                        for (int i = 0; i < _limit; i++)
                        {
                            _locks[i] = new object();
                        }
                    }
                }
            }
            TcpConnection conn = null;
            while (true)
            {
                int last_index = 0;
                for (int i = _limit - 1; i >= 0; i--)
                {
                    if (cons[i] != null)
                    {
                        last_index = i;
                        break;
                    }
                }
                for (int i = 0; i < _limit; i++)
                {
                    var tmp = cons[i];
                    if (tmp != null)
                    {
                        var available = tmp.TakeLock();
                        if (!available)
                        {
                            continue;
                        }
                        else
                        {
                            if ((DateTime.Now - tmp.LastUsed).TotalSeconds > 30)
                            {
                                cons[i] = null;
                                try
                                {
                                    tmp.client.Dispose();
                                    tmp.stream.Dispose();
                                    tmp.bw.Dispose();
                                }
                                catch (Exception ex)
                                {
#if (VERBOSE)
                                    Console.WriteLine("Disposing error:" + ex.Message);
#endif
                                }
                                continue;
                            }
                            else
                            {
                                //ping
                                tmp.bw.Write(BitConverter.GetBytes(-3));
                                tmp.bw.Flush();

                                int numBytesRead = 0;
                                var data = new byte[1024];
                                var bad = false;
                                while (numBytesRead < 4)
                                {
                                    int read = 0;
                                    try
                                    {
                                        read = tmp.stream.Read(data, numBytesRead, data.Length - numBytesRead);
                                    }
                                    catch (Exception ex)
                                    {
                                        //server closed connection
                                        bad = true;
                                        break;
                                    }
                                    numBytesRead += read;
                                    if (read <= 0)
                                    {
                                        //server closed connection
                                        bad = true;
                                        break;
                                    }
                                }
                                if (bad)
                                {
                                    cons[i] = null;
                                    try
                                    {
                                        tmp.client.Dispose();
                                        tmp.stream.Dispose();
                                        tmp.bw.Dispose();
                                    }
                                    catch (Exception ex)
                                    {
#if (VERBOSE)
                                    Console.WriteLine("Disposing error:" + ex.Message);
#endif
                                    }
                                    continue;
                                }
                                var pong = BitConverter.ToInt32(new byte[4] { data[0], data[1], data[2], data[3] }, 0);
                                if (pong != -3)
                                {
                                    cons[i] = null;
                                    try
                                    {
                                        tmp.client.Dispose();
                                        tmp.stream.Dispose();
                                        tmp.bw.Dispose();
                                    }
                                    catch (Exception ex)
                                    {
#if (VERBOSE)
                                    Console.WriteLine("Disposing error:" + ex.Message);
#endif
                                    }
                                    continue;
                                }

                                //socket is ok
                                conn = tmp;
                                break;
                            }

                        }
                    }
                    else
                    {
                        if (i < last_index)
                        {
                            continue;
                        }
                        if (Monitor.TryEnter(_locks[i]))
                        {
                            try
                            {
                                if (cons[i] != null)
                                {
                                    continue;
                                }
                                conn = new TcpConnection(hostname, port);
                                cons[i] = conn;
                                conn.Index = i;
                                break;
                            }
                            catch (Exception ex)
                            {
                                conn = null;
                                cons[i] = null;
#if (VERBOSE)
                                Console.WriteLine("Client socket creation error: " + ex.Message);
#endif
                                error_msg = ex.Message;
                                return BitConverter.GetBytes(-1);
                            }
                            finally
                            {
                                Monitor.Exit(_locks[i]);
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }
                }
                if (conn == null)
                {
                    Thread.Sleep(150);
                    continue;
                }
                else
                {
                    break;
                }
            }

            bool error = false;
            try
            {
                var length = BitConverter.GetBytes(input.Length);
                var data = new byte[1024];
                conn.bw.Write(input);
                conn.bw.Flush();

                using (MemoryStream ms = new MemoryStream())
                {
                    int numBytesRead;
                    int total;
                    while (true)
                    {
                        numBytesRead = 0;
                        while (numBytesRead < 4)
                        {
                            int read = conn.stream.Read(data, numBytesRead, data.Length - numBytesRead);
                            numBytesRead += read;
                            if (read <= 0)
                            {
                                throw new Exception("Read <= 0: " + read);
                            }
                        }
                        numBytesRead -= 4;
                        total = BitConverter.ToInt32(new byte[4] { data[0], data[1], data[2], data[3] }, 0);
                        if (total == -2)
                        {
#if (VERBOSE)
                            Console.WriteLine("PINGER!!!");
#endif
                            continue;
                        }
                        break;
                    }
                    if (numBytesRead > 0)
                    {
                        var finput = new byte[numBytesRead];
                        for (int i = 0; i < numBytesRead; i++)
                        {
                            finput[i] = data[4 + i];
                        }
                        ms.Write(finput, 0, numBytesRead);
                    }
                    total -= numBytesRead;
                    while (total > 0)
                    {
                        numBytesRead = conn.stream.Read(data, 0, data.Length);
                        if (numBytesRead <= 0)
                        {
                            throw new Exception("numBytesRead <= 0: " + numBytesRead);
                        }
                        ms.Write(data, 0, numBytesRead);
                        total -= numBytesRead;
                    }
                    conn.LastUsed = DateTime.Now;
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
#if (VERBOSE)
                Console.WriteLine("Client socket error: " + ex.Message);
#endif
                error = true;
                error_msg = ex.Message;
                return BitConverter.GetBytes(-1);
            }
            finally
            {
                if (!error)
                {
                    conn.ReleaseLock();
                }
                else
                {
                    cons[conn.Index] = null;
                    try
                    {
                        conn.client.Dispose();
                        conn.stream.Dispose();
                        conn.bw.Dispose();
                    }
                    catch (Exception ex)
                    {
#if (VERBOSE)
                        Console.WriteLine("Disposing error:" + ex.Message);
#endif
                    }
                }
            }
        }
    }
}
