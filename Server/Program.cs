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
   class ServerSockets
   {
      static int port = 2055;
      public static void Main()
      {
         SocketServer socketServer = new SocketServer(port);
         socketServer.Run().Wait();
      }
   }

   class SocketServer
   {
      Socket listener;
      int numberOfConnections = 0;

      public SocketServer(int port)
      {
         listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
         Console.WriteLine("Listening on " + port);
         listener.Bind(new IPEndPoint(IPAddress.Any, port));
         listener.Listen((int)SocketOptionName.MaxConnections);
      }

      public async Task Run()
      {
         try
         {
            while (true)
            {
               Socket handler = await Task.Factory.FromAsync(listener.BeginAccept(null, null), listener.EndAccept);
               ProcessConnection(handler);
            }
         }
         catch (Exception ex)
         {
            Console.WriteLine("BAD ERROR... " + ex.Message);
         }
      }

      private async void ProcessConnection(Socket handler)
      {
         Interlocked.Increment(ref numberOfConnections);
         Console.WriteLine(numberOfConnections);
         using (handler)
         {
            byte[] receivedData = await Recieve(handler);
            Random random = new Random();
            await Task.Delay(random.Next(100, 1000));
            //Console.WriteLine(string.Join(", ", receivedData));

            List<byte> responseData = new List<byte>();
            responseData.AddRange(BitConverter.GetBytes(receivedData.Length));
            responseData.AddRange(receivedData);

            await Send(handler, responseData.ToArray());
            handler.Close();
         }
         Interlocked.Decrement(ref numberOfConnections);
         Console.WriteLine(numberOfConnections);
      }

      private Task<int> Send(Socket client, byte[] data)
      {
         return Task.Factory.FromAsync(client.BeginSend(data, 0, data.Length, SocketFlags.None, null, null), client.EndSend);
      }

      private async Task<byte[]> Recieve(Socket client)
      {
         int dataLength = await ReadDataLength(client);
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

      private async Task<int> ReadDataLength(Socket client)
      {
         byte[] dataLengthBytes = new byte[4];
         int bytesRead = await Task.Factory.FromAsync(client.BeginReceive(dataLengthBytes, 0, 4, SocketFlags.None, null, null), client.EndReceive);
         if (bytesRead < 4)
         {
            throw new Exception("Could not determine data length received less than 4 bytes of data");
         }
         return BitConverter.ToInt32(dataLengthBytes, 0);
      }
   }
}
