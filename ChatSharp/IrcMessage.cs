using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ChatSharp
{
    public class IrcMessage
    {
        public string RawMessage { get; private set; }
        public string Prefix { get; private set; }
        public string Command { get; private set; }
        public string[] Parameters { get; private set; }

        public IrcMessage(string rawMessage)
        {
            RawMessage = rawMessage;

            if (rawMessage.StartsWith(":"))
            {
                Prefix = rawMessage.Substring(1, rawMessage.IndexOf(' ') - 1);
                rawMessage = rawMessage.Substring(rawMessage.IndexOf(' ') + 1);
            }

            if (rawMessage.Contains(' '))
            {
                Command = rawMessage.Remove(rawMessage.IndexOf(' '));
                rawMessage = rawMessage.Substring(rawMessage.IndexOf(' ') + 1);
                // Parse parameters
                var parameters = new List<string>();
                while (!string.IsNullOrEmpty(rawMessage))
                {
                    if (rawMessage.StartsWith(":"))
                    {
                        parameters.Add(rawMessage.Substring(1));
                        break;
                    }
                    if (!rawMessage.Contains(' '))
                    {
                        parameters.Add(rawMessage);
                        rawMessage = string.Empty;
                        break;
                    }
                    parameters.Add(rawMessage.Remove(rawMessage.IndexOf(' ')));
                    rawMessage = rawMessage.Substring(rawMessage.IndexOf(' ') + 1);
                }
                Parameters = parameters.ToArray();
            }
            else
            {
                // Violates RFC 1459, but we'll parse it anyway
                Command = rawMessage;
                Parameters = new string[0];
            }
        }
    }
}
