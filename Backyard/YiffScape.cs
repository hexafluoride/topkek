using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Backyard
{
    public partial class Backyard
    {
        public JObject YiffDatabase { get; set; }

        public void YiffAll(string args, string source, string nick)
        {
            var cycles = 1;

            if (args.StartsWith("$yiffforever"))
            {
                args = ".yiffall" + args.Substring("$yiffforever".Length);
                cycles = 100;
            }
            
            if (YiffDatabase == null || !YiffDatabase.ContainsKey("yiff"))
                return;
            
            var templates = Shuffle(YiffDatabase["yiff"].Values<string>().ToArray()).ToArray();

            var target = nick;
            args = args.Substring(".yiffall".Length).Trim();

            if (!string.IsNullOrWhiteSpace(args) && HasUser(source, args))
                target = args;

            for (int i = 0; i < cycles; i++)
            {
                foreach (var template in templates)
                    SendMessage($"ACTION {ExpandYiff(template, nick, target)}", source);
                
                Thread.Sleep(100);
            }
        }
        
        public void Yiff(string args, string source, string nick)
        {
            if (YiffDatabase == null || !YiffDatabase.ContainsKey("yiff"))
                return;
            
            if (CanAct("yiff", source, nick))
                MarkAct("yiff", source, nick);
            else
            {
                SendNotice("You are rate limited.", source, nick);
                return;
            }

            var templates = YiffDatabase["yiff"].Values<string>().ToArray();
            var template = templates[Random.Next(templates.Length)];

            var target = nick;
            args = args.Substring(".yiff".Length).Trim();

            if (!string.IsNullOrWhiteSpace(args) && HasUser(source, args))
                target = args;
            
            SendMessage($"ACTION {ExpandYiff(template, nick, target)}", source);
        }
        
        public string ExpandYiff(string template, string first, string second)
        {
            var expandedPlaceholderRegex = new Regex("(%\\(.*?\\))");
            var smallPlaceholderRegex = new Regex("%([a-zA-Z{_]+}?)");

            int offset = 0;

            var secondMatches = smallPlaceholderRegex.Matches(template);
            offset = 0;
            
            foreach (Match match in secondMatches)
            {
                var capture = match.Captures[0];
                var things = template.Substring(capture.Index + offset, capture.Length);

                things = things.Substring(1);
                things = things.Trim('{', '}');

                //var terms = things.Split('|');
                var terms = YiffDatabase[things].Values<string>().ToArray();
                var replacement = terms[Random.Next(terms.Length)];
                var diff = capture.Length - replacement.Length;

                template = template.Remove(capture.Index + offset, capture.Length);
                template = template.Insert(capture.Index + offset, replacement);
                
                offset -= diff;
            }
            
            var firstMatches = expandedPlaceholderRegex.Matches(template);
            offset = 0;

            foreach (Match match in firstMatches)
            {
                var capture = match.Captures[0];
                var things = template.Substring(capture.Index + offset, capture.Length);

                things = things.Substring(2, things.Length - 3);

                var terms = things.Split('|');
                var replacement = terms[Random.Next(terms.Length)];
                var diff = capture.Length - replacement.Length;

                template = template.Remove(capture.Index + offset, capture.Length);
                template = template.Insert(capture.Index + offset, replacement);
                
                offset -= diff;
            }

            template = template.Replace("$1", first);
            template = template.Replace("$2", second);
            template = Regex.Replace(
                template,
                @"\\[Uu]([0-9A-Fa-f]{4})",
                k => char.ToString(
                    (char) ushort.Parse(k.Groups[1].Value, NumberStyles.AllowHexSpecifier)));

            return template;
        }
    }
}
