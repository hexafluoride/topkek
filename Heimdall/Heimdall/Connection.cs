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
    public delegate void OnMessageReceived(Connection connection, Message message);

    public class Connection
    {
        public TcpClient Client { get; set; }
        public string Name { get; internal set; }

        public List<KeyValuePair<string, OnMessageReceived>> Handlers { get; set; }

        public event OnMessageReceived MessageReceived;

        private NetworkStream NetworkStream { get; set; }
        private BinaryReader BinaryReader { get; set; }
        private BinaryWriter BinaryWriter { get; set; }

        private Thread Loop { get; set; }
        private ManualResetEvent LoopRunning { get; set; }

        private int Violations = 0;

        public Connection(string hostname, int port, string name)
            : this(new TcpClient(hostname, port), name)
        {

        }

        public Connection(TcpClient client, string name)
        {
            Name = name;

            Handlers = new List<KeyValuePair<string, OnMessageReceived>>();

            MessageReceived += HandleMessage;

            InitializeNetwork(client);

            LoopRunning = new ManualResetEvent(false);

            Loop = new Thread(ReceiveLoop);
            Loop.Start();

            Start();
        }

        private void InitializeNetwork(TcpClient client)
        {
            Client = client;
            NetworkStream = Client.GetStream();
            BinaryReader = new BinaryReader(NetworkStream);
            BinaryWriter = new BinaryWriter(NetworkStream);
        }

        internal void Start()
        {
            LoopRunning.Set();
        }

        internal void Stop()
        {
            LoopRunning.Reset();
        }

        private void ReceiveLoop()
        {
            while(true)
            {
                while (LoopRunning.WaitOne(1000))
                {
                    if(!Client.Connected)
                    {
                        Stop();

                        if (Handlers.Any(t => t.Key == "bye"))
                        {
                            Handlers.First(t => t.Key == "bye").Value(this, null);
                        }

                        return;
                    }

                    Message msg = Message.Consume(NetworkStream);

                    //Console.WriteLine("Received {0} from {1}, data: {2}", msg.MessageType, msg.Source, Encoding.Unicode.GetString(msg.Data));

                    if(!msg.Valid)
                    {
                        Violations++;

                        if(Violations > 100)
                        {
                            Stop();

                            if (Handlers.Any(t => t.Key == "bye"))
                            {
                                Handlers.First(t => t.Key == "bye").Value(this, null);
                            }

                            return;
                        }

                        continue;
                    }

                    if(Violations > 0)
                        Violations--;

                    //Console.WriteLine("Received message {0}", msg.MessageType);

                    if (MessageReceived != null)
                        MessageReceived(this, msg);
                }
            }
        }

        public byte[] WaitFor(string message, string type, string destination, string response_type)
        {
            byte[] ret = new byte[0];

            ManualResetEvent finished = new ManualResetEvent(false);

            OnMessageReceived rec = (c, msg) =>
            {
                if (msg.Source != destination)
                    return;

                ret = msg.Data;
                finished.Set();
            };

            AddHandler(response_type, rec);

            SendMessage(message, type, destination);

            finished.WaitOne();

            RemoveHandler(response_type);

            return ret;
        }

        public byte[] WaitFor(byte[] message, string type, string destination, string response_type)
        {
            byte[] ret = new byte[0];

            ManualResetEvent finished = new ManualResetEvent(false);

            OnMessageReceived rec = (c, msg) =>
            {
                if (msg.Source != destination)
                    return;

                ret = msg.Data;
                finished.Set();
            };

            AddHandler(response_type, rec);

            SendMessage(message, type, destination);

            finished.WaitOne();

            RemoveHandler(response_type);

            return ret;
        }

        private void HandleMessage(Connection conn, Message msg)
        {
            if (msg.Destination != Name)
                return; 

            lock (Handlers)
            {
                if (Handlers.Any(p => p.Key == msg.MessageType))
                    Handlers.Where(p => p.Key == msg.MessageType).ToList().ForEach(p => Task.Factory.StartNew(delegate { p.Value(this, msg); }));
            }
        }

        public void AddHandler(string type, OnMessageReceived handler)
        {
            lock (Handlers)
            {
                Handlers.Add(new KeyValuePair<string, OnMessageReceived>(type, handler));
            }
        }

        public void RemoveHandler(string type)
        {
            lock (Handlers)
            {
                Handlers.RemoveAll(p => p.Key == type);
            }
        }

        public void SendMessage(Message message)
        {
            try
            {
//                var data = message.Serialize();
  //              BinaryWriter.Write(data);
  message.SerializeTo(NetworkStream);
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex);
            }
            //Console.WriteLine("Sent message {0}", message.MessageType);
        }

        public void SendMessage(byte[] data, string type, string dest)
        {
            Message msg = new Message(data);
            msg.MessageType = type;
            msg.Destination = dest;
            msg.Source = Name;

            SendMessage(msg);
        }

        public void SendMessage(string data, string type, string dest)
        {
            SendMessage(Encoding.Unicode.GetBytes(data), type, dest);
        }
    }
}
