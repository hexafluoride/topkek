using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class ServerMOTDEventArgs : EventArgs
    {
        public string MOTD { get; set; }

        public ServerMOTDEventArgs(string motd)
        {
            MOTD = motd;
        }
    }
}
