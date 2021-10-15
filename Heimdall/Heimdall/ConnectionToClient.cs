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
    public delegate void OnMessageToRoute(ConnectionToClient conn, Message msg);

    public class ConnectionToClient : Connection
    {
        public event OnMessageToRoute RouteMessage;

        public string ClientName { get; internal set; }

        public ConnectionToClient(TcpClient client)
            : base(client, "")
        {
            Name = "router";
            ClientName = "";

            AddHandler("init", HandleHandshake);

            MessageReceived += TryRoute;
        }

        public ConnectionToClient(string hostname, int port)
            : this(new TcpClient(hostname, port))
        {

        }

        void TryRoute(Connection connection, Message message)
        {
            if (!message.Valid)
                return;

            if(message.Destination != Name)
            {
                if (RouteMessage != null)
                    RouteMessage(this, message);
            }
        }

        public void HandleHandshake(Connection conn, Message msg)
        {
            ClientName = Encoding.Unicode.GetString(msg.Data);
        }
    }
}
