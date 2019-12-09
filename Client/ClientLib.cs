using System;
using System.Collections.Concurrent;
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
   public class TcpConnection: IDisposable
   {
      public Socket client { get; set; }

      public TcpConnection()
      {
         client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
      }

      public async Task Connect(string hostname, int port)
      {
         await Task.Factory.FromAsync(client.BeginConnect(new IPEndPoint(IPAddress.Parse(hostname), port), null, null), client.EndConnect);
      }

      public Task<int> Send(byte[] data)
      {
         return Task.Factory.FromAsync(client.BeginSend(data, 0, data.Length, SocketFlags.None, null, null), client.EndSend);
      }

      public async Task<byte[]> Recieve()
      {
         int dataLength = await ReadDataLength();
         List<byte> data = new List<byte>();
         int bufferSize = 10;
         byte[] buffer = new byte[bufferSize];
         int bytesRead = await Task.Factory.FromAsync(client.BeginReceive(buffer, 0, bufferSize, SocketFlags.None, null, null), client.EndReceive);
         while (bytesRead == bufferSize)
         {
            data.AddRange(buffer);
            if (data.Count < dataLength)
            {
               bytesRead = await Task.Factory.FromAsync(client.BeginReceive(buffer, 0, bufferSize, SocketFlags.None, null, null), client.EndReceive);
            }
            else
            {
               bytesRead = 0;
               break;
            }
         }
         if (bytesRead > 0)
         {
            Array.Resize(ref buffer, bytesRead);
            data.AddRange(buffer);
         }

         return data.ToArray();
      }

      public async Task<int> ReadDataLength()
      {
         byte[] dataLengthBytes = new byte[4];
         int bytesRead = await Task.Factory.FromAsync(client.BeginReceive(dataLengthBytes, 0, 4, SocketFlags.None, null, null), client.EndReceive);
         if (bytesRead < 4)
         {
            throw new Exception("Could not determine data length received less than 4 bytes of data");
         }
         return BitConverter.ToInt32(dataLengthBytes, 0);
      }

      #region IDisposable Support
      private bool disposedValue = false; // To detect redundant calls

      protected virtual void Dispose(bool disposing)
      {
         if (!disposedValue)
         {
            if (disposing)
            {
               client.Close();
               client.Dispose();
            }
            disposedValue = true;
         }
      }


      // This code added to correctly implement the disposable pattern.
      public void Dispose()
      {
         Dispose(true);
      }
      #endregion
   }

   public class ClientSockets
   {
      private const int maxOpenConnections = 100;
      private int openConnections = 0;

      public ClientSockets()
      {

      }

      public async Task<byte[]> CallServer(byte[] input, string hostname, int port)
      {
         while (openConnections > maxOpenConnections)
         {
            await Task.Delay(100);
         }
         Task<byte[]> serverTask = ExecuteServerCall(input, hostname, port);
         return await serverTask;
      }

      private async Task<byte[]> ExecuteServerCall(byte[] input, string hostname, int port)
      {
         try
         {
            Interlocked.Increment(ref openConnections);
            Console.WriteLine(openConnections);
            using (TcpConnection tcpConnection = new TcpConnection())
            {
               await tcpConnection.Connect(hostname, port);
               await tcpConnection.Send(input);
               byte[] result = await tcpConnection.Recieve();
               //Console.WriteLine(string.Join(", ", result));
               Interlocked.Decrement(ref openConnections);
               Console.WriteLine(openConnections);
               return result;
            }
         }
         catch
         {
            Interlocked.Decrement(ref openConnections);
            Console.WriteLine(openConnections);
            throw;
         }
      }
   }
}

   /*
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
*/
  