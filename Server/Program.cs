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
         byte[] receivedData = await Recieve(handler);
         Random random = new Random();
         await Task.Delay(random.Next(100, 1000));
         Console.WriteLine(string.Join(", ", receivedData));
         await Send(handler, new List<byte>() { 3, 0, 0, 0, 1, 2, 3 }.ToArray());
      }

      private Task<int> Send(Socket client, byte[] data)
      {
         return Task.Factory.FromAsync(client.BeginSend(data, 0, data.Length, SocketFlags.None, null, null), client.EndSend);
      }

      private async Task<byte[]> Recieve(Socket client)
      {
         List<byte> data = new List<byte>();
         int bufferSize = 1024;
         byte[] buffer = new byte[bufferSize];
         int bytesRead = await Task.Factory.FromAsync(client.BeginReceive(buffer, 0, bufferSize, SocketFlags.None, null, null), client.EndReceive);
         while (bytesRead == bufferSize)
         {
            data.AddRange(buffer);
            bytesRead = await Task.Factory.FromAsync(client.BeginReceive(buffer, 0, bufferSize, SocketFlags.None, null, null), client.EndReceive);
         }
         Array.Resize(ref buffer, bytesRead);
         data.AddRange(buffer);

         return data.ToArray();
      }
   }
}
