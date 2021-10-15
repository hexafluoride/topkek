using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace ChatSharp.Events
{
    public class SocketErrorEventArgs : EventArgs
    {
        public SocketError SocketError { get; set; }

        public SocketErrorEventArgs(SocketError socketError)
        {
            SocketError = socketError;
        }
    }
}
