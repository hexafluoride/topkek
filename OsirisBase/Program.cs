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
            MemoryStream ms = new MemoryStream();

            ms.WriteString(nick);
            ms.WriteString(token);
            ms.WriteString(message);

            Connection.SendMessage(ms.ToArray(), "irc_notice", "irc");

            ms.Close();
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

        public void AddMatcher(MessageMatcher matcher)
        {
            matcher.Node = Name;
            
            IFormatter bf = new BinaryFormatter();
            MemoryStream ms = new MemoryStream();
            bf.Serialize(ms, matcher);

            Connection.SendMessage(ms.ToArray(), "add_matcher", "irc");
            ms.Close();
        }

        public void CallMatcher(Connection conn, Message msg)
        {
            MemoryStream ms = new MemoryStream(msg.Data);

            string id = ms.ReadString();
            string message = ms.ReadString().Trim();
            string source = ms.ReadString();
            string nick = ms.ReadString();

            if (Commands.Any(t => t.Key == id))
            {
                Commands.First(t => t.Key == id).Value(message, source, nick);
            }

            ms.Close();
        }

        public bool HasUser(string source, string nick)
        {
            MemoryStream ms = new MemoryStream();

            ms.WriteString(source);
            ms.WriteString(nick);

            return Connection.WaitFor(ms.ToArray(), "has_user", "irc", "has_user_resp")[0] == '+';
        }

        public void SendMessage(string message, string target)
        {
            MemoryStream ms = new MemoryStream();

            ms.WriteString(target);
            ms.WriteString(message);

            Connection.SendMessage(ms.ToArray(), "irc_send", "irc");

            ms.Close();
        }
    }
}
