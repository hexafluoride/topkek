using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Handlers
{
    public static class UserHandlers
    {
        public static void HandleWhoIsUser(IrcClient client, IrcMessage message)
        {
            var whois = (WhoIs)client.RequestManager.PeekOperation("WHOIS " + message.Parameters[1]).State;
            whois.User.Nick = message.Parameters[1];
            whois.User.User = message.Parameters[2];
            whois.User.Hostname = message.Parameters[3];
            whois.User.RealName = message.Parameters[5];
        }

        public static void HandleWhoIsLoggedInAs(IrcClient client, IrcMessage message)
        {
            var whois = (WhoIs)client.RequestManager.PeekOperation("WHOIS " + message.Parameters[1]).State;
            whois.LoggedInAs = message.Parameters[2];
        }

        public static void HandleWhoIsServer(IrcClient client, IrcMessage message)
        {
            var whois = (WhoIs)client.RequestManager.PeekOperation("WHOIS " + message.Parameters[1]).State;
            whois.Server = message.Parameters[2];
            whois.ServerInfo = message.Parameters[3];
        }

        public static void HandleWhoIsOperator(IrcClient client, IrcMessage message)
        {
            var whois = (WhoIs)client.RequestManager.PeekOperation("WHOIS " + message.Parameters[1]).State;
            whois.IrcOp = true;
        }

        public static void HandleWhoIsIdle(IrcClient client, IrcMessage message)
        {
            var whois = (WhoIs)client.RequestManager.PeekOperation("WHOIS " + message.Parameters[1]).State;
            whois.SecondsIdle = int.Parse(message.Parameters[2]);
        }

        public static void HandleWhoIsChannels(IrcClient client, IrcMessage message)
        {
            var whois = (WhoIs)client.RequestManager.PeekOperation("WHOIS " + message.Parameters[1]).State;
            var channels = message.Parameters[2].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < channels.Length; i++)
                if (!channels[i].StartsWith("#"))
                    channels[i] = channels[i].Substring(1);
            whois.Channels = whois.Channels.Concat(channels).ToArray();
        }

        public static void HandleWhoIsEnd(IrcClient client, IrcMessage message)
        {
            var request = client.RequestManager.DequeueOperation("WHOIS " + message.Parameters[1]);
            if (request.Callback != null)
                request.Callback(request);
            client.OnWhoIsReceived(new Events.WhoIsReceivedEventArgs((WhoIs) request.State));
        }
    }
}
