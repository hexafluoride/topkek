using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OsirisNext
{
    public enum MessageMatchType
    {
        StartsWith,
        Contains,
        EndsWith
    }

    [Serializable]
    public class MessageMatcher
    {
        public string ID { get; set; }
        public string MatchString { get; set; }
        public MessageMatchType MatchType { get; set; }
        public string Node { get; set; }
        public bool OwnerOnly { get; set; }
        public string Nick { get; set; }
        public bool Notice { get; set; }
        public bool Join { get; set; }
        public bool Mode { get; set; }
        public bool EndExecution { get; set; }
        public bool ExecuteIfNoMatch { get; set; }
        public bool Internal { get; set; }

        public MessageMatcher()
        { 
        }
        
        public static MessageMatcher FromCommand(string label)
        {
            char[] command = new char[] { '.', '$', '!', '?', '>', '½' };
            
            if (!command.Any(c => label.StartsWith(c.ToString())))
                return new(label, label, MessageMatchType.Contains, false, false);
            
            if (label.StartsWith("~"))
                return new(label, label, MessageMatchType.Contains, join: true);
            
            if (label.StartsWith("^"))
                return new(label, label, MessageMatchType.Contains, mode: true);
            
            return new(label, label, MessageMatchType.StartsWith, label.StartsWith("$"));
        }

        public MessageMatcher(string id, string match_str, MessageMatchType type, bool owner_only = false,
            bool last_to_execute = false, bool join = false, bool mode = false)
        {
            ID = id;
            MatchString = match_str;
            MatchType = type;
            OwnerOnly = owner_only;
            ExecuteIfNoMatch = last_to_execute;
            Join = join;
            Mode = mode;
        }

        public bool Matches(string target)
        {
            switch(MatchType)
            {
                case MessageMatchType.StartsWith:
                    return target.StartsWith(MatchString);
                case MessageMatchType.Contains:
                    return target.Contains(MatchString);
                case MessageMatchType.EndsWith:
                    return target.EndsWith(MatchString);
                default:
                    return false;
            }
        }
    }
}