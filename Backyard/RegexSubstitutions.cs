using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Backyard
{
    public partial class Backyard
    {
        private List<(string, string, string)> IgnoreLines = new List<(string, string, string)>();
        
        public void TrySubstitute(string args, string source, string n)
        {
            var targetNick = n;
            var origArgs = args;

            var highlightChars = new[] {',', ':'};

            foreach (var highlightChar in highlightChars)
            {
                if (args.Contains(highlightChar))
                {
                    var possibleNick = args.Substring(0, args.IndexOf(highlightChar));
                    if (HasUser(source, possibleNick))
                    {
                        targetNick = possibleNick;
                        args = args.Substring(possibleNick.Length + 1).Trim();
                        break;
                    }
                }
            }

            if (!args.StartsWith("s"))
                return;

            var separator = args[1];

            if (separator != '/')
            {
                return;
            }
            
            var parts = args.Split(separator);

            if (parts.Length < 3)
                return;
            
            IgnoreLines.Add((source, n, origArgs));
            if (IgnoreLines.Count > 150)
                IgnoreLines.RemoveAt(0);

            var flags = parts.Length == 4 ? parts[3] : "";
            var search = parts[1];
            var replace = parts[2];

            RegexOptions parsedFlags = RegexOptions.None;
            bool replaceAll = false;
            int replaceNth = 0;

            if (flags.Contains('i'))
                parsedFlags |= RegexOptions.IgnoreCase;

            if (flags.Any(char.IsDigit))
            {
                var digits = new string(flags.Select(f => char.IsDigit(f) ? f : ' ').ToArray()).Replace(" ", "");
                if (int.TryParse(digits, out int replaceIndex) && replaceIndex >= 0)
                    replaceNth = replaceIndex;
            }

            if (flags.Contains('g'))
                replaceAll = true;

            var regex = new Regex(search, parsedFlags, TimeSpan.FromSeconds(0.5));
            var haystack = LastLines[source].Reverse<(string, string)>().ToArray();
            string replaced = null;

            foreach (var hay in haystack)
            {
                if (hay.Item1 != targetNick)
                    continue;

                if (IgnoreLines.Contains((source, hay.Item1, hay.Item2)))
                    continue;

                try
                {
                    var matches = regex.Matches(hay.Item2);
                
                    if (matches.Count == 0)
                        continue;

                    if (!replaceAll)
                    {
                        if (matches.Count <= replaceNth)
                            continue;
                        else
                        {
                            var nthMatch = matches[replaceNth];
                            replaced = hay.Item2;
                            replaced = replaced.Remove(nthMatch.Index, nthMatch.Length);
                            replaced = replaced.Insert(nthMatch.Index, replace);
                            break;
                        }
                    }
                    else
                    {
                        replaced = hay.Item2;
                        var offset = 0;
            
                        foreach (Match match in matches)
                        {
                            var diff = match.Length - replace.Length;
                            replaced = replaced.Remove(match.Index + offset, match.Length);
                            replaced = replaced.Insert(match.Index + offset, replace);
                
                            offset -= diff;
                        }

                        break;
                    }
                }
                catch
                {
                    SendMessage("Fuck you.", source);
                    return;
                }
            }

            if (replaced != null)
            {
                if (targetNick == n)
                    SendMessage($"{n} meant to say: {replaced}", source);
                else
                    SendMessage($"{n} thinks {targetNick} meant to say: {replaced}", source);
            }
        }
    }
}