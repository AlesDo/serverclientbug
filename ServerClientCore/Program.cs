using Bogus;
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
         Faker faker = new Faker();
         Enumerable.Range(0, 5000).ToList().ForEach(f =>
         {
            int dataLength = faker.Random.Int(1, 10);
            byte[] data = faker.Random.Bytes(dataLength);
            List<byte> dataWithLength = new List<byte>();
            dataWithLength.AddRange(BitConverter.GetBytes(dataLength));
            dataWithLength.AddRange(data);
            tasks.Add(client.CallServer(dataWithLength.ToArray(), Hostname, Port));
         });
         tasks.ForEach(f => f.Wait());
         Console.WriteLine("DONE");
         Console.ReadKey();
      }
   }
}
