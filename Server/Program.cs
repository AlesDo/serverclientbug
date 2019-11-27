using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    class Pinger
    {
        public bool Done { get; set; }
        public object _lock = new object();
        public BinaryWriter bw { get; set; }
        public void Do()
        {
            try
            {
                int total_wait = 0;
                int sleep_ms = 2000;
                while (!Done)
                {
                    Thread.Sleep(sleep_ms);
                    total_wait += sleep_ms;
                    if (total_wait % 10000 == 0)
                    {
                        lock (_lock)
                        {
                            if (!Done)
                            {
                                bw.Write(BitConverter.GetBytes(-2));
                                bw.Flush();
                            }
                        }
                    }
                }
            }
            catch { return; }
        }
    }
    class ServerSockets
    {
        static Socket listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        static int port = 2055;
        public static void Main()
        {

            Console.WriteLine("Listening on " + port);
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
            listener.Listen((int)SocketOptionName.MaxConnections);


            SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(Service);
            bool willRaiseEvent = listener.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                Service(null, acceptEventArg);
            }

            while (true)
            {
                try
                {
                    Thread.Sleep(60000);
#if (VERBOSE)
                    Console.WriteLine("Still kicking...");
#endif
                }
                catch (Exception ex)
                {
                    Console.WriteLine("BAD ERROR... " + ex.Message);
                }
            }
        }
        static void OnProcessExit(object sender, EventArgs e)
        {
        }
        private static void LoopToStartAccept()
        {
            SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(Service);
            bool willRaiseEvent = listener.AcceptAsync(acceptEventArg);
            if (!willRaiseEvent)
            {
                Service(null, acceptEventArg);
            }
        }
        private static void HandleBadAccept(SocketAsyncEventArgs acceptEventArgs)
        {
#if (VERBOSE)
            Console.WriteLine("bad accept");
#endif
            acceptEventArgs.AcceptSocket.Dispose();
        }
        private static void Service(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
            {
                LoopToStartAccept();
                HandleBadAccept(e);
                return;
            }

            LoopToStartAccept();


            try
            {
                using (Socket soc = e.AcceptSocket)
                {
                    var rg = new Random();
#if (VERBOSE)
                    Console.WriteLine("New socket: " + rg.Next(0, 1000000));
#endif
                    //soc.NoDelay = true;
                    soc.ReceiveTimeout = 60000;
                    soc.SendTimeout = 60000;
                    using (Stream stream = new NetworkStream(soc))
                    using (BinaryWriter bw = new BinaryWriter(stream))
                    {
                        while (true) //reuse same connection for many commands
                        {
                            byte[] data = new byte[1024];
                            using (MemoryStream ms = new MemoryStream())
                            {
                                int numBytesRead = 0;
                                while (numBytesRead < 4)
                                {
                                    int read = 0;
                                    try
                                    {
                                        read = stream.Read(data, numBytesRead, data.Length - numBytesRead);
                                    }
                                    catch (Exception ex)
                                    {
                                        //client closed connection
                                        return;
                                    }
                                    numBytesRead += read;
                                    if (read <= 0)
                                    {
                                        //throw new Exception("Read <= 0: " + read);
                                        //client closed connection
                                        return;
                                    }
                                }
                                numBytesRead -= 4;
                                var total = BitConverter.ToInt32(new byte[4] { data[0], data[1], data[2], data[3] }, 0);
                                if (total == -3) //ping
                                {
                                    //pong
                                    bw.Write(BitConverter.GetBytes(-3));
                                    bw.Flush();
                                    continue;
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
                                    numBytesRead = stream.Read(data, 0, data.Length);
                                    if (numBytesRead <= 0)
                                    {
                                        throw new Exception("numBytesRead <= 0: " + numBytesRead);
                                    }
                                    ms.Write(data, 0, numBytesRead);
                                    total -= numBytesRead;
                                }
                                var input = ms.ToArray();
                                var pinger = new Pinger()
                                {
                                    bw = bw
                                };
                                ThreadPool.QueueUserWorkItem(f => { pinger.Do(); });


                                var random = new Random();
                                Thread.Sleep(random.Next(100, 1000));

                                pinger.Done = true;
                                lock (pinger._lock)
                                {
                                    bw.Write(new List<byte>() { 3, 0, 0, 0, 1, 2, 3 }.ToArray());
                                    bw.Flush();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
#if (VERBOSE)
                Console.WriteLine("Socket error: " + ex.Message);
#endif
                //try
                //{
                //    var rg = new Random();
                //    File.WriteAllText("sock_error_" + rg.Next() + ".txt", ex.Message + " " + ex.StackTrace + (ex.InnerException != null ? (" " + ex.InnerException.Message + " " + ex.InnerException.StackTrace) : ""));
                //}
                //catch (Exception) { }
                return;
            }
            finally
            {
#if (VERBOSE)
                Console.WriteLine("Listener finally ");
#endif
            }
        }
    }
}
