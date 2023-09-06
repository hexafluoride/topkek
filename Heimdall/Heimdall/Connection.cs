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

            // Loop = new Thread(ReceiveLoop);
            // Loop.Start();

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

        void Fail()
        {
            Stop();

            try
            {
                Client.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            if (Handlers.Any(t => t.Key == "bye"))
            {
                Handlers.First(t => t.Key == "bye").Value(this, null);
            }
        }

        // public List<Message> PumpBox = new();
        
        public bool PumpNextMessage(ref Message msg)
        {
            if(!Client.Connected)
            {
                Console.WriteLine("Client not connected");
                Fail();
                return false;
            }

            if (!Message.TryConsume(BinaryReader, msg))
            {
                Console.WriteLine("Message could not be consumed");
                Fail();
                return false;
            }

            //Console.WriteLine("Received {0} from {1}, data: {2}", msg.MessageType, msg.Source, Encoding.Unicode.GetString(msg.Data));

            if(!msg.Valid)
            {
                Violations++;

                if(Violations > 100)
                {
                    Console.WriteLine("Too many violations");
                    Fail();
                    return false;
                }

                Console.WriteLine("Message not valid");
                return false;
            }

            if(Violations > 0)
                Violations--;
            
            DispatchMessage(msg);
            return true;
        }

        public void DispatchMessage(Message msg)
        {
            if (MessageReceived != null)
            {
                try
                {
                    MessageReceived(this, msg);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        // private void ReceiveLoop()
        // {
        //     Message msg = new Message();
        //     
        //     while(true)
        //     {
        //         while (LoopRunning.WaitOne(1000))
        //         {
        //             PumpNextMessage(ref msg);
        //         }
        //     }
        // }

        public byte[] WaitFor(string message, string type, string destination, string response_type, CancellationToken cancellationToken = default)
        {
            SendMessage(message, type, destination);

            var tempMessage = new Message();
            var backedUp = new List<Message>();
            while (!cancellationToken.IsCancellationRequested)
            {
                PumpNextMessage(ref tempMessage);
                if (tempMessage.MessageType == response_type)
                {
                    return tempMessage.DataSliced.ToArray();
                }
            }
            
            throw new OperationCanceledException();
        }

        public byte[] WaitFor(byte[] message, string type, string destination, string response_type, CancellationToken cancellationToken = default)
        {
            SendMessage(message, type, destination);

            var tempMessage = new Message();
            while (!cancellationToken.IsCancellationRequested)
            {
                PumpNextMessage(ref tempMessage);
                if (tempMessage.MessageType == response_type)
                {
                    return tempMessage.DataSliced.ToArray();
                }
            }

            throw new OperationCanceledException();
        }

        private void HandleMessage(Connection conn, Message msg)
        {
            if (msg.Destination != Name)
                return;

            for (int i = 0; i < Handlers.Count; i++)
            {
                lock (Handlers)
                {
                    (var targetType, var handler) = Handlers[i];
                    if (msg.MessageType == targetType)
                        handler(conn, msg);
                }
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
                message.SerializeTo(NetworkStream);
                NetworkStream.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            //Console.WriteLine("Sent message {0}", message.MessageType);
        }

        public void SendMessage(byte[] data, string type, string dest)
        {
            try
            {
                lock (Client)
                {
                    Message.SerializeTo(NetworkStream, Name, dest, type, data);
                    NetworkStream.Flush();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void SendMessage(string data, string type, string dest)
        {
            try
            {
                Message.SerializeTo(NetworkStream, Name, dest, type, data);
                NetworkStream.Flush();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }
}
