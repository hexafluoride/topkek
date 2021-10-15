using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace Heimdall
{
    public delegate void OnConnection(ConnectionToClient conn);

    public class Listener
    {
        private TcpListener listener { get; set; }
        private Thread Loop { get; set; }
        private ManualResetEvent Running = new ManualResetEvent(false);

        public event OnConnection NewConnection;

        public Listener()
        {
            Loop = new Thread(ListenerLoop);
            Loop.Start();
        }

        public Listener(IPEndPoint ep)
            : this()
        {
            listener = new TcpListener(ep);
        }

        public Listener(IPAddress addr, int port)
            : this()
        {
            listener = new TcpListener(addr, port);
        }

        public Listener(int port)
            : this()
        {
            listener = new TcpListener(port);
        }

        public void Start()
        {
            Console.WriteLine("Started listening on {0}", (IPEndPoint)listener.LocalEndpoint);
            listener.Start();
            Running.Set();
        }

        public void Stop()
        {
            listener.Stop();
            Running.Reset();

            Console.WriteLine("Stop listening on {0}", (IPEndPoint)listener.LocalEndpoint);
        }

        private void ListenerLoop()
        {
            while(true)
            {
                while(Running.WaitOne(1000))
                {
                    TcpClient client = listener.AcceptTcpClient();

                    Task.Factory.StartNew(delegate {
                        if (NewConnection != null)
                            NewConnection(new ConnectionToClient(client));
                    });
                }
            }
        }
    }
}
