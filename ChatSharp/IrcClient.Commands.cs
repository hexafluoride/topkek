using ChatSharp.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public partial class IrcClient
    {
        public bool authed = false;

        public Action authact;

        public void Nick(string newNick)
        {
            SendRawMessage("NICK {0}", newNick);
            User.Nick = newNick;
        }

        public void SendMessage(string message, params string[] destinations)
        {
            try
            {
                const string illegalCharacters = "\r\n\0";

                foreach (char c in illegalCharacters)
                    message = message.Replace(c.ToString(), "");

                if (message.Length > 450)
                    message = message.Substring(0, 450);

                if (!destinations.Any()) throw new InvalidOperationException("Message must have at least one target.");
                if (illegalCharacters.Any(message.Contains)) throw new ArgumentException("Illegal characters are present in message.", "message");
                string to = string.Join(",", destinations);
                SendRawMessage("PRIVMSG {0} :{1}", to, message);
            }
            catch
            {

            }
        }

        public void SendAction(string message, params string[] destinations)
        {
            const string illegalCharacters = "\r\n\0";
            if (!destinations.Any()) throw new InvalidOperationException("Message must have at least one target.");
            if (illegalCharacters.Any(message.Contains)) throw new ArgumentException("Illegal characters are present in message.", "message");
            string to = string.Join(",", destinations);
            SendRawMessage("PRIVMSG {0} :\x0001ACTION {1}\x0001", to, message);
        }

        public void PartChannel(string channel)
        {
            if (!Channels.Contains(channel))
                throw new InvalidOperationException("Client is not present in channel.");
            SendRawMessage("PART {0}", channel);
            Channels.Remove(Channels[channel]);
        }

        public void PartChannel(string channel, string reason)
        {
            if (!Channels.Contains(channel))
                throw new InvalidOperationException("Client is not present in channel.");
            SendRawMessage("PART {0} :{1}", channel, reason);
            Channels.Remove(Channels[channel]);
        }

        public void JoinChannel(string channel)
        {
            //if (Channels.Contains(channel))
            //    throw new InvalidOperationException("Client is not already present in channel.");
            SendRawMessage("JOIN {0}", channel);
        }

        public void SetTopic(string channel, string topic)
        {
            if (!Channels.Contains(channel))
                throw new InvalidOperationException("Client is not present in channel.");
            SendRawMessage("TOPIC {0} :{1}", channel, topic);
        }

        public void KickUser(string channel, string user)
        {
            SendRawMessage("KICK {0} {1} :{1}", channel, user);
        }

        public void KickUser(string channel, string user, string reason)
        {
            SendRawMessage("KICK {0} {1} :{2}", channel, user, reason);
        }

        public void InviteUser(string channel, string user)
        {
            SendRawMessage("INVITE {1} {0}", channel, user);
        }

        public void WhoIs(string nick)
        {
            WhoIs(nick, null);
        }

        public void WhoIs(string nick, Action<WhoIs> callback)
        {
            var whois = new WhoIs();
            RequestManager.QueueOperation("WHOIS " + nick, new RequestOperation(whois, ro =>
                {
                    if (callback != null)
                        callback((WhoIs)ro.State);
                }));
            SendRawMessage("WHOIS {0}", nick);
        }

        public void GetMode(string channel)
        {
            GetMode(channel, null);
        }

        public void GetMode(string channel, Action<IrcChannel> callback)
        {
            RequestManager.QueueOperation("MODE " + channel, new RequestOperation(channel, ro =>
                {
                    var c = Channels[(string)ro.State];
                    if (callback != null)
                        callback(c);
                }));
            SendRawMessage("MODE {0}", channel);
        }

        public void ChangeMode(string channel, string change)
        {
            SendRawMessage("MODE {0} {1}", channel, change);
        }

        public void GetModeList(string channel, char mode, Action<MaskCollection> callback)
        {
            RequestManager.QueueOperation("GETMODE " + mode + " " + channel, new RequestOperation(new MaskCollection(), ro =>
                {
                    var c = (MaskCollection)ro.State;
                    if (callback != null)
                        callback(c);
                }));
            SendRawMessage("MODE {0} {1}", channel, mode);
        }
    }
}
