using System;

namespace ChatSharp
{
    public class KickEventArgs : EventArgs
    {
        public KickEventArgs(IrcChannel channel, IrcUser kicker, IrcUser kicked, string reason)
        {
            Channel = channel;
            Kicker = kicker;
            Kicked = kicked;
            Reason = reason;
        }

        public IrcChannel Channel { get; set; }
        public IrcUser Kicker { get; set; }
        public IrcUser Kicked { get; set; }
        public string Reason { get; set; }   
    }
}

