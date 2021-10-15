using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading;
using System.Net.Sockets;
using System.IO;

namespace Heimdall
{

    public class ConnectionToRouter : Connection
    {
        public ConnectionToRouter(TcpClient client, string name)
            : base(client, name)
        {
            Thread.Sleep(1000);
            SendMessage(Encoding.Unicode.GetBytes(Name), "init", "router");
        }

        public ConnectionToRouter(string hostname, int port, string name)
            : this(new TcpClient(hostname, port), name)
        {

        }

        public void End()
        {
            SendMessage("", "bye", "router");
            Client.Close();
        }

        public void Ping()
        {

        }
    }
}
