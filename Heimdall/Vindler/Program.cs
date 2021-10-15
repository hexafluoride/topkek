using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net.Sockets;
using System.IO;

using Heimdall;
using System.Net;

namespace Vindler
{
    class Program
    {
        public static List<ConnectionToClient> Clients = new List<ConnectionToClient>();

        public static Random rnd = new Random();

        public static DateTime Start = DateTime.Now;

        static void Main(string[] args)
        {
            IPAddress addr = IPAddress.Loopback;
            int port = 9933;

            if(args.Any())
            {
                int temp_port = -1;
                IPAddress temp_addr = IPAddress.Loopback;

                if(int.TryParse(args[0], out temp_port))
                {
                    port = temp_port;
                }
                else if(IPAddress.TryParse(args[0], out temp_addr))
                {
                    addr = temp_addr;
                }
                else
                {
                    var parts = args[0].Split(':');
                    addr = IPAddress.Parse(parts[0]);
                    port = int.Parse(parts[1]);
                }
            }

            Listener listener = new Listener(addr, port);

            listener.NewConnection += listener_NewConnection;
            listener.Start();
        }

        static void conn_MessageReceived(Connection connection, Message message)
        {
            Console.WriteLine(Encoding.Unicode.GetString(message.Data));
        }

        static void listener_NewConnection(ConnectionToClient conn)
        {
            Console.WriteLine("New connection {0}", conn.ClientName);
            conn.RouteMessage += conn_RouteMessage;
            conn.AddHandler("bye", HandleDisconnect);
            conn.AddHandler("ping", Pong);
            conn.AddHandler("get_uptime", GetUptime);
            conn.AddHandler("get_modules", GetModules);

            Clients.Add(conn);
        }

        static void Pong(Connection conn, Message msg)
        {
            conn.SendMessage(msg.Data, "pong", msg.Source);
        }

        static void GetModules(Connection conn, Message msg)
        {
            MemoryStream ms = new MemoryStream();

            foreach (ConnectionToClient c in Clients)
            {
                ms.WriteString(c.ClientName);
            }

            Message reply = msg.Clone(true);

            reply.Data = ms.ToArray();
            reply.MessageType = "modules";

            conn.SendMessage(reply);
        }

        static void GetUptime(Connection conn, Message msg)
        {
            Message reply = msg.Clone(true);

            reply.MessageType = "uptime";
            reply.Data = BitConverter.GetBytes((int)(DateTime.Now - Start).TotalSeconds);

            conn.SendMessage(reply);
        }

        static void HandleDisconnect(Connection conn, Message msg)
        {
            Console.WriteLine("{0} disconnecting", (conn as ConnectionToClient).ClientName);
            Clients.Remove(conn as ConnectionToClient);
            return;
        }

        static void conn_RouteMessage(ConnectionToClient conn, Message msg)
        {
            //Console.WriteLine("Routing message \"{0}\" from \"{1}\" to \"{2}\"", Encoding.Unicode.GetString(msg.Data), msg.Source, msg.Destination);

            string dest = msg.Destination;

            if(Clients.Any(c => c.ClientName == dest))
            {
                Clients.First(c => c.ClientName == dest).SendMessage(msg);
            }
            else
            {
                Console.WriteLine($"Failed to route message from {msg.Source} to {msg.Destination}");
            }
        }
    }
}
