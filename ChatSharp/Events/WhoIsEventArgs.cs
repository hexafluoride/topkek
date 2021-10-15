using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class WhoIsReceivedEventArgs : EventArgs
    {
        public WhoIs WhoIsResponse
        {
            get;
            set;
        }

        public WhoIsReceivedEventArgs(WhoIs whoIsResponse)
        {
            WhoIsResponse = whoIsResponse;
        }
    }
}
