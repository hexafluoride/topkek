using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public class PrivateMessage
    {
        public PrivateMessage(IrcMessage message)
        {
            Source = message.Parameters[0];
            Message = message.Parameters[1];

            User = new IrcUser(message.Prefix);
            if (Source.StartsWith("#"))
                IsChannelMessage = true;
            else
                Source = User.Nick;
        }

        public IrcUser User { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public bool IsChannelMessage { get; set; }
    }
}
