using Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServerClient
{
   class Program
   {
      static void Main(string[] args)
      {
         var tasks = new List<Task<byte[]>>();
         ClientSockets client = new ClientSockets();
         var Hostname = "127.0.0.1";
         var Port = 2055;
         Enumerable.Range(0, 5000).ToList().ForEach(f =>
         {
            tasks.Add(client.CallServer(new List<byte>() { 3, 0, 0, 0, 1, 2, 3 }.ToArray(), Hostname, Port));
         });
         tasks.ForEach(f => f.Wait());
         Console.WriteLine("DONE");
         Console.ReadKey();
      }
   }
}
