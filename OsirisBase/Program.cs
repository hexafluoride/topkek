using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Heimdall;
using HeimdallBase;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Globalization;
using System.Threading;
using OsirisNext;
using MatchType = System.IO.MatchType;

namespace OsirisBase
{
    public delegate void MessageHandler(string args, string source, string nick);
    public abstract class OsirisModule : HeimdallBase.HeimdallBase
    {
        public Action MatcherSetup = null;

        public Dictionary<string, MessageHandler> Commands = new Dictionary<string, MessageHandler>()
        {
        };
        
        public OsirisModule() { MatcherSetup = MatcherSetup ?? new Action(SetUpMatchers); }

        public override void ConnectionEstablished()
        {
            base.ConnectionEstablished();
            
            Connection.AddHandler("message", CallMatcher);
            Connection.AddHandler("send_matchers", (c, m) => { MatcherSetup(); });

            MatcherSetup();
        }

        public void SendNotice(string message, string token, string nick)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteString(nick);
                ms.WriteString(token);
                ms.WriteString(message);

                Connection.SendMessage(ms.ToArray(), "irc_notice", "irc");
            }
        }

        public void SetUpMatchers()
        {
            char[] prefixes = new char[] { '.', '!', '?', '>' };
            Commands = Commands.SelectMany(c => c.Key.StartsWith("*") ? prefixes.Select(prefix => new KeyValuePair<string, MessageHandler>(prefix + c.Key.Substring(1), c.Value)) : new List<KeyValuePair<string, MessageHandler>>() { c }).ToDictionary(k => k.Key, k => k.Value);

            foreach (var pair in Commands)
            {
                Console.WriteLine(pair.Key);
                AddMatcher(MessageMatcher.FromCommand(pair.Key));
            }   
        }

        private IFormatter formatter = new BinaryFormatter();
        
        public void AddMatcher(MessageMatcher matcher)
        {
            matcher.Node = Name;

            using (MemoryStream ms = new MemoryStream())
            {
                formatter.Serialize(ms, matcher);
                Connection.SendMessage(ms.ToArray(), "add_matcher", "irc");
            }
        }

        public void CallMatcher(Connection conn, Message msg)
        {
            var data = msg.DataSliced;
            data = data.ReadString(out string id);

            if (Commands.ContainsKey(id))
            {
                data = data.ReadString(out string message);
                data = data.ReadString(out string source);
                data = data.ReadString(out string nick);
                message = message.Trim();
                Commands[id](message, source, nick);
            }
        }

        public List<(char, string)> GetUsers(string source)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteString(source);
                var resp = Connection.WaitFor(ms.ToArray(), "get_users", "irc", "users");

                ReadOnlySpan<byte> respSliced = resp.AsSpan();
                var list = new List<(char, string)>();

                while (respSliced.Length > 0)
                {
                    respSliced = respSliced.ReadString(out string nextNick);
                    list.Add((nextNick[0], nextNick.Substring(1)));
                }

                return list;
            }
        }

        public bool HasUser(string source, string nick)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteString(source);
                ms.WriteString(nick);

                return Connection.WaitFor(ms.ToArray(), "has_user", "irc", "has_user_resp")[0] == '+';
            }
        }

        public void SendMessage(string message, string target)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                ms.WriteString(target);
                ms.WriteString(message);

                Connection.SendMessage(ms.ToArray(), "irc_send", "irc");

                ms.Close();
            }
        }
    }
}
