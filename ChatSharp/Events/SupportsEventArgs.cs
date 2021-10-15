using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class SupportsEventArgs : EventArgs
    {
        public ServerInfo ServerInfo { get; set; }

        public SupportsEventArgs(ServerInfo serverInfo)
        {
            ServerInfo = serverInfo;
        }
    }
}
