using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public class WhoIs
    {
        internal WhoIs()
        {
            User = new IrcUser();
            SecondsIdle = -1;
            Channels = new string[0];
        }

        public IrcUser User { get; set; }
        public string[] Channels { get; set; }
        public bool IrcOp { get; set; }
        public int SecondsIdle { get; set; }
        public string Server { get; set; }
        public string ServerInfo { get; set; }
        public string LoggedInAs { get; set; }
    }
}
