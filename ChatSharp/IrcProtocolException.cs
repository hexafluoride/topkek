using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public class IrcProtocolException : Exception
    {
        public IrcProtocolException()
        {
        }

        public IrcProtocolException(string message) : base(message)
        {
            
        }
    }
}
