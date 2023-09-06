using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Backyard
{
    public partial class Backyard
    {
        Dictionary<string, Reminder> LastReminder = new Dictionary<string, Reminder>();
        
        void ListReminders(string args, string source, string n)
        {
            var relevant_reminders = RemindManager.Reminders.Where(r => r.Nick.ToLower() == n.ToLower()).ToList();

            if(!relevant_reminders.Any())
            {
                SendMessage("You have no reminders yet. Try setting one with .remind <time> <message>!", source);
                return;
            }

            SendMessage(string.Format("You have {0}. I'm sending them to you over NOTICE.", OsirisBase.Utilities.BetterPlural(relevant_reminders.Count, "reminder")), source);

            var timezone = GetTimezone(source, n);

            foreach(var reminder in relevant_reminders)
            {
                SendNotice(string.Format("\"{0}\", set for {1} from now({2}), created {3} ago({4})", 
                    reminder.Message, 
                    OsirisBase.Utilities.TimeSpanToPrettyString(reminder.EndDate - DateTime.UtcNow), reminder.EndDate + timezone,
                    OsirisBase.Utilities.TimeSpanToPrettyString(DateTime.UtcNow - reminder.StartDate), reminder.StartDate + timezone), source, n);
            }
        }
        
        void SnoozeReminder(string args, string source, string n)
        {
            try
            {
                args = args.Substring(".snooze".Length).Trim();

                if (args == "help")
                {
                    SendMessage("Usage: .snooze <time>. Examples for <time>: 5 minutes, 2 weeks, 300 days, 13:00, december 5(set timezones with .tz).", source);
                    return;
                }

                if (!LastReminder.ContainsKey(n))
                {
                    SendMessage("You haven't had a reminder yet.", source);
                    return;
                }

                Console.WriteLine(args);
                var timezone = GetTimezone(source, n);
                var time = RemindManager.Get(args, timezone, out string left);

                if (time < DateTime.UtcNow)
                {
                    Console.WriteLine(time);
                    SendMessage("Usage: .snooze <time>. Examples for <time>: 5 minutes, 2 weeks, 300 days, 13:00, december 5(set timezones with .tz).", source);
                    return;
                }

                var last_reminder = LastReminder[n];
                var diff = OsirisBase.Utilities.TimeSpanToPrettyString(time - DateTime.UtcNow);
                var snooze_notice = string.Format("(snoozed from {0})", last_reminder.EndDate + timezone);

                var new_msg = Regex.IsMatch(last_reminder.Message, @"(\(snoozed from .*?\))$") ? Regex.Replace(last_reminder.Message, @"(\(snoozed from .*?\))$", snooze_notice) : last_reminder.Message + " " + snooze_notice;

                RemindManager.Add(time, new_msg, n, source);
                SendMessage(string.Format("I've rescheduled your reminder \"{0}\" to {1} from now -- {2}.", last_reminder.Message, diff, time + timezone), source);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Something happened: {0}", ex.Message);
                Console.WriteLine(ex);
            }
        }
        
        void Remind(string args, string source, string n)
        {
            SendMessage("Reminders are currently disabled. Please check back later!", source);
            return;
            
            args = args.Substring("!remind".Length).Trim();

            if(args == "" || args == "help")
            {
                SendMessage("Usage: .remind <time> \"message\". Examples for <time>: 5 minutes, 2 weeks, 300 days, 13:00, december 5(set timezones with .tz). If the command is failing, try surrounding the message with double quotes(\").", source);
                return;
            }

            StringBuilder msg = new StringBuilder();
            StringBuilder t = new StringBuilder();
            bool in_quotes = false;

            for(int i = 0; i < args.Length; i++)
            {
                if(args[i] == '"')
                {
                    in_quotes = !in_quotes;
                    continue;
                }

                if (!in_quotes)
                {
                    t.Append(args[i]);
                }
                else
                    msg.Append(args[i]);
            }

            var time = RemindManager.Get(t.ToString(), GetTimezone(source, n), out string left);

            if (!args.Contains('"'))
            {
                if (left.StartsWith("remind me to "))
                    left = left.Substring("remind me to ".Length);

                if (left.StartsWith("me to "))
                    left = left.Substring("me to ".Length);

                if (left.StartsWith("me in "))
                    left = left.Substring("me in ".Length);

                if (left.StartsWith("to "))
                    left = left.Substring("to ".Length);

                if (left.EndsWith(" at"))
                    left = left.Substring(0, left.Length - " at".Length);
                else if (left.EndsWith(" by"))
                    left = left.Substring(0, left.Length - " by".Length);

                msg = new StringBuilder(left);
            }

            if ((time - DateTime.UtcNow).TotalSeconds < 1)
            {
                SendMessage("Usage: !remind <time> \"message\". Examples for <time>: 5 minutes, 2 weeks, 300 days, 13:00, december 5(set timezones with .tz). If the command is failing, try surrounding the message with double quotes(\").", source);
                return;
            }

            //SendMessage("Message: " + msg.ToString(), source);
            SendMessage(string.Format("I'll remind you{2} at {0}, which is {1} from now.", (time + GetTimezone(source, n)).ToString(), OsirisBase.Utilities.TimeSpanToPrettyString(time - DateTime.UtcNow), msg.Length != 0 ? " of \"" + msg.ToString() + "\"" : ""), source);
            //SendMessage("Token: " + MakePermanent(source), source);

            RemindManager.Add(time, msg.ToString(), n, source);
        }

    }
}