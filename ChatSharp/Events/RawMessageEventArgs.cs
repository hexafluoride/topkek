using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class RawMessageEventArgs : EventArgs
    {
        public string Message { get; set; }
        public bool Outgoing { get; set; }

        public RawMessageEventArgs(string message, bool outgoing)
        {
            Message = message;
            Outgoing = outgoing;
        }
    }
}
