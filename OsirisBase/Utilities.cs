using System;
using System.Collections.Generic;
using System.Linq;

namespace OsirisBase
{
    public class Utilities
    {
        public static Dictionary<string, int> NamedColors = new Dictionary<string, int>()
        {
            {"Black", 0},
            {"White", 0},
            {"Blue", 2},
            {"Green", 3},
            {"Red", 4},
            {"Brown", 5},
            {"Orchid", 6},
            {"Purple", 6},
            {"Copper", 7},
            {"Orange", 7},
            {"Gold", 8},
            {"Yellow", 8},
            {"Bright Green", 9},
            {"Cyan", 10},
            {"Teal", 11},
            {"Light Blue", 12},
            {"Magenta", 13},
            {"Fuchsia", 13},
            {"Dark Gray", 14},
            {"Light Gray", 15},
            {"Silver", 15},
            {"Pink", 13},
            {"Peach", 13}
        };

        public static string FormatWithColor(string str, int color)
        {
            if (color < 0)
                return str;

            return $"{color,00}{str}";
        }

        public static int GetColor(string name)
        {
            name = name.ToLower();
            var parts = name.Split(' ');
            
            foreach (var color in NamedColors)
                if (color.Key.ToLowerInvariant() == name)
                    return color.Value;

            foreach (var color in NamedColors)
                if (color.Key.ToLowerInvariant().Contains(name))
                    return color.Value;
            
            foreach (var color in NamedColors)
                if (name.Contains(color.Key.ToLowerInvariant()))
                    return color.Value;

            if (parts.Length > 1)
                foreach (var part in parts)
                    if (GetColor(part) > -1)
                        return GetColor(part);

            return -1;
        }

        public static string TimezoneToString(TimeSpan span)
        {
            return string.Format("UTC{2}{0}{1}", span.Hours, span.Minutes != 0 ? string.Format(":{0:00", Math.Abs(span.Minutes)) : "", span.Hours >= 0 ? "+" : "");
        }
        
        public static string Bold(string str)
        {
            if (str == null)
                return null;

            return "\"" + OsirisNext.Utilities.BOLD + str + OsirisNext.Utilities.BOLD + "\"";
        }
        
        public static string Italics(long str, int italics, int bold)
        {
            var actualStr = str.ToString("#,##");
            
            if (italics == 0 && bold == 0)
                return actualStr;
            
            if (bold == 0)
                return OsirisNext.Utilities.ITALICS + actualStr + OsirisNext.Utilities.ITALICS;
            
            if (italics == 0)
                return OsirisNext.Utilities.BOLD + actualStr + OsirisNext.Utilities.BOLD;

            return OsirisNext.Utilities.BOLD + OsirisNext.Utilities.ITALICS + actualStr +
                   OsirisNext.Utilities.BOLD + OsirisNext.Utilities.ITALICS;
        }

        public static string BetterPlural(double amount, string unit, int italics = 0, int color = -1, int bold = 0)
        {
            return BetterPlural((long)amount, unit, italics, color);
        }
        
        public static string BetterPlural(long amount, string unit, int italics = 0, int color = -1, int bold = 0)
        {
            if (amount == 1 || amount == -1)
                return string.Format("{2}{0}{3} {1}", Italics(amount, italics, bold), unit, color > -1 ? "" + color.ToString("00") : "", color > -1 ? "" : "");

            if (unit.EndsWith("y") && !unit.EndsWith("ay"))
                unit = unit.Substring(0, unit.Length - 1) + "ie";
            return string.Format("{2}{0}{3} {1}s", Italics(amount, italics, bold), unit, color > -1 ? "" + color.ToString("00") : "", color > -1 ? "" : "");
        }
        
        public static string TimeSpanToPrettyString(TimeSpan span, bool truncate_singular = false, bool extreme_granularity = false)
        {
            double total_years = span.TotalDays / 365d;
            double total_months = span.TotalDays / 30d;
            double total_weeks = span.TotalDays / 7d;

            int display_months = (int)(total_months % 12) + ((total_months >= 12) ? ((int)total_years == 0 ? 12 : 0) : 0);
            int display_weeks = (int)(total_weeks % 4) + ((total_weeks >= 4) ? (((int)total_months % 12) == 0 ? 4 : 0) : 0);

            Dictionary<string, int> lengths = new Dictionary<string, int>()
            {
                {Utilities.BetterPlural(total_years, "year"), (int)(total_years) },
                {Utilities.BetterPlural(display_months, "month"), display_months },
                {Utilities.BetterPlural(display_weeks, "week"), display_weeks },
                {Utilities.BetterPlural(span.TotalDays % 7, "day"), (int)(span.TotalDays % 7) },
                {Utilities.BetterPlural(span.TotalHours % 24, "hour"), (int)(span.TotalHours % 24) },
                {Utilities.BetterPlural(span.TotalMinutes % 60, "minute"), (int)(span.TotalMinutes % 60) },
                {Utilities.BetterPlural(span.TotalSeconds % 60, "second"), (int)(span.TotalSeconds % 60) },
            };

            if(extreme_granularity && span.TotalSeconds < 1)
            {
                lengths[Utilities.BetterPlural(span.TotalMilliseconds % 1000, "millisecond")] = (int)(span.TotalMilliseconds % 60);
                lengths[Utilities.BetterPlural(span.Ticks % 10, "microsecond")] = (int)(span.Ticks % 10);
            }

            var valid_segments = lengths.Where(p => p.Value > 0);

            if (valid_segments.Count() >= 2)
                return string.Join(" ", valid_segments.Select(p => p.Key).Take(2));
            else if (valid_segments.Any())
            {
                var segment = valid_segments.Single();

                if (segment.Value == 1 && truncate_singular)
                    return segment.Key.Split(' ')[1];
                else
                    return segment.Key;
            }

            return span.ToString();
        }
    }
}