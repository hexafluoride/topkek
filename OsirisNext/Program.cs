using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using ChatSharp;
using ConversionTherapy;
using Heimdall;
using HeimdallBase;

namespace OsirisNext
{
    class Program
    {
        static void Main(string[] args)
        {
            new Osiris().Start(args);
        }
    }

    public class Osiris : HeimdallBase.HeimdallBase
    {
        public IrcManager IrcManager { get; set; }
        public List<MessageMatcher> Matchers = new List<MessageMatcher>();

        private Dictionary<string, MessageHandler> InternalMatchers = new Dictionary<string, MessageHandler>();

        public Osiris()
        {
            
        }

        public void Start(string[] args)
        {
            Name = "irc";
            this.Init(args, OsirisMain);
        }

        void OsirisMain()
        {
            if (File.Exists("homoglyphs.txt"))
            {
                Trigger.Table = new HomoglyphTable("./glyphs.txt");
            }
            else
            {
                Trigger.Table = new HomoglyphTable();
            }
            
            IrcManager = new IrcManager();
            IrcManager.OnMessage += IrcManager_OnMessage;
            /*IrcManager.OnNotice += IrcManager_OnNotice;
            IrcManager.OnJoin += IrcManager_OnJoin;
            IrcManager.OnModeChange += IrcManager_OnModeChange;*/

            var options = Directory.GetFiles("./servers", "*.json");

            foreach(var file in options)
            {
                try
                {
                    var conn = ConnectionOptions.FromFile(file);
                    IrcManager.Connect(conn);

                    Console.WriteLine("Connected to {0}:{1} from file \"{2}\"", conn.Server, conn.Port, file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Exception while loading connection options from \"{0}\"", file);
                    Console.WriteLine(ex);
                }
            }
            
            Connection.AddHandler("add_matcher", AddMatcher);
            Connection.AddHandler("clear_matchers", ClearMatchers);
            Connection.AddHandler("has_user", HasUser);
            Connection.AddHandler("get_users", GetUsers);
            Connection.AddHandler("irc_send", SendMessage);
            Connection.AddHandler("irc_notice", SendNotice);

            InternalMatchers = new Dictionary<string, MessageHandler>()
            {
                {"$join", JoinToChannel},
                {"$leave", LeaveChannel},
                {"$rehash", SendRehash},
                {"$ignore", Ignore},
                {"$unignore", Unignore},
                {".uptime", ShowUptime},
                {".modules", ListModules},
            };

            foreach ((var matchStr, var handler) in InternalMatchers)
            {
                var matcher = MessageMatcher.FromCommand(matchStr);
                matcher.Internal = true;
                Matchers.Add(matcher);
            }

            foreach (string module in GetModules())
            {
                Console.WriteLine($"Asking {module} to send its matchers to us.");
                Connection.SendMessage(new byte[0], "send_matchers", module);
            }
        }

        private void Ignore(IrcClient client, PrivateMessage message, string source)
        {
            string nick = message.Message.Substring(".ignore".Length).Trim();
            var trimmedSource = source.Substring(0, source.IndexOf('/'));

            Config.Add("ignored", trimmedSource + "/" + nick);
            Config.Save();
        }

        private void Unignore(IrcClient client, PrivateMessage message, string source)
        {
            string nick = message.Message.Substring(".unignore".Length).Trim();
            var trimmedSource = source.Substring(0, source.IndexOf('/'));

            Config.Remove("ignored", trimmedSource + "/" + nick);
            Config.Save();
        }

        private void SendRehash(IrcClient client, PrivateMessage message, string source)
        {
            foreach (var module in GetModules())
            {
                Connection.SendMessage(new byte[0], "rehash", module);
            }
            client.SendMessage("done", source);
        }

        private void JoinToChannel(IrcClient client, PrivateMessage message, string source)
        {
            client.JoinChannel(message.Message.Substring("$join".Length));
        }

        private void LeaveChannel(IrcClient client, PrivateMessage message, string source)
        {
            var channel = message.Message.Substring("$leave".Length);
            client.PartChannel(channel);
        }

        private void ShowUptime(IrcClient client, PrivateMessage message, string source)
        {
            var uptimes = GetModules().Select(m => new KeyValuePair<string, int>(m, GetUptime(m)));
            var latest = uptimes.OrderBy(k => k.Value).First();

            IrcManager.SendMessage(string.Format("Last started module: {0} with uptime {1}", latest.Key, latest.Value.DisplaySeconds()), source);
            IrcManager.SendMessage(string.Join(" // ", uptimes.Select(p => string.Format("{0} uptime: {1}", p.Key, p.Value.DisplaySeconds()))), source);
        }

        private void ListModules(IrcClient client, PrivateMessage message, string source)
        {
            IrcManager.SendMessage(string.Join(", ", GetModules()) + ".", source);
        }
        
        private void IrcManager_OnMessage(IrcClient client, PrivateMessage message, string source)
        {
            var messageSerialized = new byte[0];
            bool anyMatch = false;
            bool anyFallback = false;
            List<MessageMatcher> matching = new List<MessageMatcher>();

            for (int i = 0; i < Matchers.Count; i++)
            {
                var matcher = Matchers[i];
                if (!matcher.Matches(message.Message))
                    continue;
                
                if (matcher.OwnerOnly && message.User.Nick != client.Owner)
                    continue;
                
                if (!string.IsNullOrWhiteSpace(matcher.Nick) && matcher.Nick != message.User.Nick)
                    continue;

                if (matcher.ExecuteIfNoMatch)
                {
                    anyFallback = true;
                    continue;
                }
                
                matching.Add(matcher);
                if (matcher.EndExecution)
                    break;
            }

            if (!matching.Any() && anyFallback)
            {
                for (int i = 0; i < Matchers.Count; i++)
                {
                    var matcher = Matchers[i];
                    if (matcher.ExecuteIfNoMatch)
                        matching.Add(matcher);
                }
            }

            foreach (var matcher in matching)
            {
                if (matcher.Internal)
                {
                    if (InternalMatchers.ContainsKey(matcher.ID))
                        InternalMatchers[matcher.ID](client, message, source);
                    
                    continue;
                }
                
                if (messageSerialized.Length == 0)
                {
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.WriteString(message.Message);
                        ms.WriteString(source);
                        ms.WriteString(message.User.Nick);
                        messageSerialized = ms.ToArray();
                    }
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    ms.WriteString(matcher.ID);
                    ms.Write(messageSerialized);
                    Connection.SendMessage(ms.ToArray(), "message", matcher.Node);
                }
            }
        }
        
        void ClearMatchers(Connection conn, Message msg)
        {
            Matchers.Clear();
        }

        void AddMatcher(Connection conn, Message msg)
        {
            IFormatter bf = new BinaryFormatter();
            MemoryStream ms = new MemoryStream(msg.Data);
            MessageMatcher matcher = (MessageMatcher)bf.Deserialize(ms);
            
            Console.WriteLine($"Adding matcher sent by {msg.Source}/{matcher.Node}: id: {matcher.ID}, match string: {matcher.MatchString}");

            if (matcher == null)
            {
                Console.WriteLine($"Received invalid matcher from {msg.Source}");
                return;
            }

            lock (Matchers)
            {
                Matchers.RemoveAll(m => m.ID == matcher.ID && m.MatchType == matcher.MatchType && m.MatchString == matcher.MatchString && m.Node == matcher.Node);
                Matchers.Add(matcher);
            }

            ms.Close();
        }

        void SendMessage(Connection conn, Message msg)
        {
            MemoryStream ms = new MemoryStream(msg.Data);

            string target = ms.ReadString();
            string message = ms.ReadString();

            ms.Close();

            IrcManager.SendMessage(message, target);
        }
        void SendNotice(Connection conn, Message msg)
        {
            MemoryStream ms = new MemoryStream(msg.Data);

            string nick = ms.ReadString();
            string source = ms.ReadString();
            string message = ms.ReadString();

            message = message.Replace("\n", "");
            message = message.Replace("\r", "");

            (var client, var target) = IrcManager.GetSenderFromSource(source);
            client.SendRawMessage("NOTICE {0} :{1}", nick, message);
        }
        
        void HasUser(Connection conn, Message msg)
        {
            MemoryStream ms = new MemoryStream(msg.Data);

            string source = ms.ReadString();
            string nick = ms.ReadString();

            (var client, var channel) = IrcManager.GetChannelFromSource(source);

            bool has = channel?.Users?.Any(u => u.Nick == nick) == true;
            conn.SendMessage(has ? "+" : "-", "has_user_resp", msg.Source);
        }
        
        private void GetUsers(Connection conn, Message msg)
        {
            using (MemoryStream ms = new MemoryStream(msg.Data))
            using (MemoryStream ret = new MemoryStream())
            {
                string source = ms.ReadString();
                (var client, var channel) = IrcManager.GetChannelFromSource(source);

                foreach (var pair in channel.UsersByMode)
                {
                    foreach (var user in pair.Value)
                        ret.WriteString(pair.Key + user.Nick);
                }

                var allUsers = channel.UsersByMode.SelectMany(p => p.Value);
                foreach (var user in channel.Users.Where(u => !allUsers.Contains(u)))
                    ret.WriteString(" " + user.Nick);

                conn.SendMessage(ret.ToArray(), "users", msg.Source);
            }
        }
    }
}