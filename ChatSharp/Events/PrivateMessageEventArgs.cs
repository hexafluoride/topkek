using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class PrivateMessageEventArgs : EventArgs
    {
        public IrcMessage IrcMessage { get; set; }
        public PrivateMessage PrivateMessage { get; set; }

        public PrivateMessageEventArgs(IrcMessage ircMessage)
        {
            IrcMessage = ircMessage;
            PrivateMessage = new PrivateMessage(IrcMessage);
        }
    }
}
