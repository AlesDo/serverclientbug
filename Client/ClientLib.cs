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
         int bufferSize = 1024;
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
      private const int maxOpenConnections = 10;
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
            // Console.WriteLine(openConnections);
            using (TcpConnection tcpConnection = new TcpConnection())
            {
               await tcpConnection.Connect(hostname, port);
               await tcpConnection.Send(input);
               byte[] result = await tcpConnection.Recieve();
               // Console.WriteLine(string.Join(", ", result));
               return result;
            }
         }
         finally
         {
            Interlocked.Decrement(ref openConnections);
            // Console.WriteLine(openConnections);
         }
      }
   }
}
  