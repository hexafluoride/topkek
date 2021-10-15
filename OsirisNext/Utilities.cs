using System;
using System.Text;
using System.Text.RegularExpressions;

namespace OsirisNext
{public static class Utilities
    {
        public static string BOLD = "";
        public static string UNDERLINE = "";
        public static string ITALICS = "";
        public static string COLOR = "";

        public static string DisplaySeconds(this int seconds)
        {
            if (seconds == -1)
                return "invalid";

            TimeSpan span = new TimeSpan(0, 0, seconds);
            StringBuilder sb = new StringBuilder();

            if (span.Days > 0)
                sb.Append(string.Format("{0}d", span.Days));

            if (span.Hours > 0)
                sb.Append(string.Format("{0}h", span.Hours));

            if (span.Minutes > 0)
                sb.Append(string.Format("{0}m", span.Minutes));

            if (span.Seconds > 0)
                sb.Append(string.Format("{0}s", span.Seconds));

            return sb.ToString().Trim();
        }
        
        public static string Sanitize(string input)
        {
            input = input.Replace("", ""); // bold
            input = input.Replace("", ""); // underline
            input = input.Replace("", ""); // italics

            input = RemoveDuplicateColor(input);

            input = Regex.Replace(input, @"[\x02\x1F\x0F\x16]|\x03(\d\d?(,\d\d?)?)?", String.Empty);

            return input;
        }

        public static string RemoveDuplicateColor(string input)
        {
            string ret = string.Join(COLOR, input.Split(new string[] { COLOR }, StringSplitOptions.RemoveEmptyEntries));
            if (input.StartsWith(COLOR))
                ret = COLOR + ret;
            if (input.EndsWith(COLOR))
                ret = ret + COLOR;

            return ret;
        }
    }
}