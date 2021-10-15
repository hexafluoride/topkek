using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ChatSharp;
using ChatSharp.Events;
using System.Threading;
using Heimdall;
using HeimdallBase;

namespace OsirisNext
{
    public delegate void NoticeHandler(string message, string source);
    public delegate void JoinHandler(string nick, string source);
    public delegate void ModeHandler(string nick, string change, string source);
    public delegate void MessageHandler(IrcClient client, PrivateMessage message, string source);

    public class IrcManager
    {
        public List<IrcClient> Clients = new List<IrcClient>();
        public Dictionary<string, IrcClient> ClientsByName = new Dictionary<string, IrcClient>();
        public List<Trigger> Triggers = new List<Trigger>();
        public List<KeyValuePair<string, string>> IgnoreModules = new List<KeyValuePair<string, string>>();

        public DateTime LastBots;

        public event MessageHandler OnMessage;
        public event NoticeHandler OnNotice;
        public event JoinHandler OnJoin;
        public event ModeHandler OnModeChange;

        public IrcManager()
        {
            PingLoop();
        }

        public IrcClient GetClient(string name)
        {
            if (!ClientsByName.ContainsKey(name))
                return null;
            return ClientsByName[name];
        }

        public void Connect(ConnectionOptions options)
        {
            if (ClientsByName.ContainsKey(options.Name))
                return;

            IrcClient Client = new IrcClient(options.Server, new IrcUser(options.Nickname, options.Nickname), options.Ssl);

            if(options.ZncLogin)
            {
                Client.User = new IrcUser(options.Nickname, string.Format("{0}@bot/{1}", options.ZncUsername, options.ZncNetwork), options.ZncPassword);
            }

            Client.Options = options;
            Client.Delay = options.Delay;

            Client.IgnoreInvalidSSL = true;

            Client.SetHandler("INVITE", new IrcClient.MessageHandler((c, msg) =>
            {
                if(msg.Prefix.StartsWith(options.Owner))
                    c.JoinChannel(msg.Parameters[1]);
            }));

            Client.SetHandler("KICK", new IrcClient.MessageHandler((c, msg) =>
            {
                if (msg.Parameters[1] == options.Nickname)
                {
                    c.PartChannel(msg.Parameters[0]);
                    System.Threading.Thread.Sleep(10000);
                    c.JoinChannel(msg.Parameters[0]);
                }
            }));
            Client.SetHandler("PONG", new IrcClient.MessageHandler((c, msg) =>
            {
                Console.WriteLine("Received PONG from {0}", c.ServerAddress);
                c.LastPong = DateTime.Now;
            }));
            Client.ConnectionComplete += (s, e) =>
            {
                Console.WriteLine("Connection complete on {0}", Client.ServerAddress);

                if (!Client.authed)
                    Client.authact();
            };
            
            Client.ChannelMessageRecieved += ChannelMessage;
            Client.NoticeRecieved += Client_NoticeReceived;
            Client.RawMessageRecieved += Client_RawMessageReceived;
            Client.UserJoinedChannel += Client_UserJoinedChannel;
            Client.ModeChanged += Client_ModeChanged;
            Client.NickChanged += Client_NickChanged;
            Client.NetworkError += Client_NetworkError;

            Client.authact = delegate
            {
                if (options.NickServ)
                {
                    Client.SendMessage("identify " + options.NickServPassword, "NickServ");
                }
                Client.authed = true;
                Client.Reconnecting = false;

                Task.Factory.StartNew(delegate
                {
                    Thread.Sleep(10000);
                    foreach (string str in options.Autojoin)
                        Client.JoinChannel(str);
                });
            };

            Client.ConnectAsync();

            Client.Owner = options.Owner;
            ClientsByName[options.Name] = Client;
            Clients.Add(Client);
            ConnectionGuard(Client);
        }

        private void PingLoop()
        {
            Task.Factory.StartNew(delegate
            {
                while (true)
                {
                    Thread.Sleep(30000);

                    foreach (var client in Clients)
                        client.SendRawMessage("PING ayy");
                }
            });
        }

        private void ConnectionGuard(IrcClient client)
        {
            Task.Factory.StartNew(delegate
            {
                while(true)
                {
                    Thread.Sleep(1000);

                    if(!client.Reconnecting && (DateTime.Now - client.LastPong).TotalSeconds > 60)
                    {
                        Console.WriteLine("Ping timeout on {0}", client.ServerAddress);
                        Client_NetworkError(client, new SocketErrorEventArgs(System.Net.Sockets.SocketError.TimedOut));
                        return;
                    }
                }
            });
        }

        private void Client_NetworkError(object sender, SocketErrorEventArgs e)
        {
            (sender as IrcClient).NetworkError -= Client_NetworkError;

            if (!Clients.Contains((sender as IrcClient)))
            {
                return;
            }
        
            Task.Factory.StartNew(delegate
            {
                IrcClient client = (sender as IrcClient);
                client.Reconnecting = true;
                Console.WriteLine("NETWORK ERROR!");
                Console.WriteLine(e.SocketError);
                Console.WriteLine("Attempting to reconnect...");

                Console.Write("Waiting for connection...");

                int i = 0;
                /*while (!Utilities.HaveConnection())
                {
                    Thread.Sleep(3000);
                    i++;

                    if (i % 20 == 0)
                        Console.Write(".");
                }*/

                Thread.Sleep(10000);

                Console.WriteLine("\nReconnecting...");

                ConnectionOptions options = (ConnectionOptions)client.Options;
                try {client.Quit(); client.Socket.Close(1000);} catch{}
                Clients.Remove(client);
                ClientsByName.Remove(options.Name);
                Thread.Sleep(10000);
                Connect(options);
            });
        }

        private string GetSourceName(IrcClient client, IrcChannel channel) => GetSourceName(client, channel.Name);
        
        private string GetSourceName(IrcClient client, string sender) =>
            $"{((ConnectionOptions) client.Options).Name}/{sender}";
        
        public (IrcClient, IrcChannel) GetChannelFromSource(string source)
        {
            (var client, var channelName) = GetSenderFromSource(source);
            return (client, client.Channels[channelName]);
        }

        public (IrcClient, string) GetSenderFromSource(string source)
        {
            var parts = source.Split('/');
            var client = GetClient(parts[0]);
            return (client, source.Substring(parts[0].Length + 1));
        }

        private void Client_NickChanged(IrcClient client, string prev, string now)
        {
            foreach (var channel in client.Channels.Where(c => c.Users.Any(u => u.Nick == prev || u.Nick == now)))
                Client_UserJoinedChannel(client, new ChannelUserEventArgs(channel, new IrcUser(now, "")));
        }

        private void Client_ModeChanged(object sender, ModeChangeEventArgs e)
        {
            IrcClient client = (IrcClient)sender;

            if (!e.Change.Contains(" "))
                return;

            if (!client.Channels.Any(c => c.Name == e.Target))
                return;

            var channel = client.Channels[e.Target];

            string nick = e.Change.Split(' ')[1];

            if (!channel.Users.Contains(nick))
                return;

            if (OnModeChange != null)
                OnModeChange(e.User.Nick, e.Change, channel.Name);
        }

        private void Client_UserJoinedChannel(object sender, ChannelUserEventArgs e)
        {
            Task.Factory.StartNew(delegate
            {
                var client = (IrcClient)sender;

                if (OnJoin != null)
                    OnJoin(e.User.Nick, GetSourceName(client, e.Channel));
            });
        }

        private void Client_NoticeReceived(object sender, IrcNoticeEventArgs e)
        {
            IrcClient client = (IrcClient)sender;
            var source = e.Message.Prefix.Split('!')[0];
            if (OnNotice != null)
                OnNotice(e.Notice, GetSourceName(client, source));
        }

        private void Client_RawMessageReceived(object sender, RawMessageEventArgs e)
        {
            IrcClient client = (IrcClient)sender;

            var msg = new IrcMessage(e.Message);
            string command = msg.Command.ToUpper();

            if (command != "PRIVMSG")
            {
                Triggers.ForEach(t => t.ExecuteIfMatches(new IrcMessage(e.Message), client));
            }
        }

        public void SendMessage(string message, string source)
        {
            var client = source.Split('/')[0];
            var target = source.Substring(client.Length + 1);
            var actualClient = ClientsByName[client];

            actualClient.SendMessage(message, target);
        }
        
        private void ChannelMessage(object sender, PrivateMessageEventArgs e)
        {
            IrcClient client = (IrcClient)sender;

            if (!client.authed)
                client.authact();

            var source = GetSourceName(client, e.PrivateMessage.Source);
            if ((Config.Contains("ignored", e.PrivateMessage.User.Nick) ||
                 Config.Contains("ignored", ((ConnectionOptions)client.Options).Name + "/" + e.PrivateMessage.User.Nick) ||
                 Config.Contains("ignored", source + "/" + e.PrivateMessage.User.Nick)) && 
                e.PrivateMessage.User.Nick != client.Owner)
                return;

            string msg = e.PrivateMessage.Message;
            bool authed = e.PrivateMessage.User.Nick == client.Owner;

            if (!(authed && msg.StartsWith("$")))
            {
                foreach (Trigger t in Triggers)
                {
                    string result = t.ExecuteIfMatches(e.IrcMessage, client);
                    if (result != "")
                    {
                        msg = result;
                        if (result.StartsWith("$"))
                            authed = true;

                        if (t.StopExecution)
                            break;
                    }
                }
            }

            e.PrivateMessage.Message = msg;

            if (OnMessage != null)
                OnMessage(client, e.PrivateMessage, GetSourceName(client, e.PrivateMessage.Source));
        }
    }
}
