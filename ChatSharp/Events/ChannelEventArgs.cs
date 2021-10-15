using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class ChannelEventArgs : EventArgs
    {
        public IrcChannel Channel { get; set; }

        public ChannelEventArgs(IrcChannel channel)
        {
            Channel = channel;
        }
    }
}
