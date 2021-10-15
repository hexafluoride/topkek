using System;
using System.Linq;
using HeimdallBase;
using Newtonsoft.Json.Linq;
using OsirisBase;

namespace Backyard
{
    public partial class Backyard
    {
        bool PrefersMetric(string source, string nick)
        {
            if (source.Contains('/'))
                source = source.Substring(0, source.IndexOf('/'));
            
            return GetUserDataForSourceAndNick<string>(source, nick, "unit_preference") == "metric";
        }
        
        void GetUnits(string args, string source, string n)
        {
            SendMessage(string.Format("You are currently using {0} units.", PrefersMetric(source, n) ? "metric" : "imperial"), source);
        }

        void SetUnits(string args, string source, string n)
        {
            args = args.Substring(1).Trim().ToLower();
            var trimmedSource = source.Contains('/') ? source.Substring(0, source.IndexOf('/')) : source;

            try
            {
                if (args == "imperial")
                {
                    SetUserDataForSourceAndNick(trimmedSource, n, "unit_preference", "imperial");
                    SendMessage(string.Format("You are now using {0} units.", PrefersMetric(source, n) ? "metric" : "imperial"), source);
                }
                else if (args == "metric")
                {
                    SetUserDataForSourceAndNick(trimmedSource, n, "unit_preference", "metric");
                    SendMessage(string.Format("You are now using {0} units.", PrefersMetric(source, n) ? "metric" : "imperial"), source);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
        
        TimeSpan GetTimezone(string source, string nick)
        {
            if (source.Contains('/'))
                source = source.Substring(0, source.IndexOf('/'));
            
            return TimeSpan.Parse(GetUserDataForSourceAndNick<string>(source, nick, "timezone") ?? "00:00:00");
        }

        void SetTimezone(string args, string source, string n)
        {
            args = args.Substring(".tz".Length).Trim().ToLower();

            if(args.Length == 0)
            {
                SendMessage(string.Format("Your current timezone is {0}.", Utilities.TimezoneToString(GetTimezone(source, n))), source);
                return;
            }

            try
            {
                if(!DateTimeOffset.TryParse("12:00 " + args, out DateTimeOffset offset))
                {
                    var current_tz = GetTimezone(source, n);

                    SendMessage("You need to specify your timezone in the format \"±hh:mm\". Examples: -4:00(EST), -5:00(CST), +1:00(CET), +0:00(UTC).", source);
                    SendMessage(string.Format("Your current timezone is {0}.", Utilities.TimezoneToString(current_tz)), source);
                    return;
                }

                string offset_str = offset.Offset.ToString();
                var trimmedSource = source.Contains('/') ? source.Substring(0, source.IndexOf('/')) : source;
                
                SetUserDataForSourceAndNick(trimmedSource, n, "timezone", offset_str);
                SendMessage(string.Format("Your timezone is now {0}.", Utilities.TimezoneToString(GetTimezone(source, n))), source);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }
}