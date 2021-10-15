using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class IrcNoticeEventArgs : EventArgs
    {
        public IrcMessage Message { get; set; }
        public string Notice { get { return Message.Parameters[1]; } }
        public string Source { get { return Message.Prefix; } }

        public IrcNoticeEventArgs(IrcMessage message)
        {
            if (message.Parameters.Length != 2)
                throw new IrcProtocolException("NOTICE was delivered in incorrect format");
            Message = message;
        }
    }
}
