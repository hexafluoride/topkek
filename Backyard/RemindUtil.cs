using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using HeimdallBase;

namespace Backyard
{
    public delegate void ReminderDone(Reminder r);

    public class RemindManager
    {
        public List<Reminder> Reminders = new List<Reminder>();
        public List<string> SeenTrackedNicks = new List<string>();
        public Dictionary<Reminder, ManualResetEvent> SeenTracker = new Dictionary<Reminder, ManualResetEvent>(); 
        public ManualResetEvent Added = new ManualResetEvent(false);
        public event ReminderDone ReminderDone;

        public void Load(string path = "./remind")
        {
            if (!File.Exists("./remind"))
                return;

            BinaryFormatter formatter = new BinaryFormatter();
            var stream = File.OpenRead(path);

            try
            {
                Reminders = (List<Reminder>)formatter.Deserialize(stream);
            }
            catch (Exception ex)
            {
                throw;
            }
            finally
            {
                stream.Close();
            }
        }

        public void Save()
        {
            BinaryFormatter formatter = new BinaryFormatter();
            var stream = File.OpenWrite("./remind.tmp");

            formatter.Serialize(stream, Reminders);

            stream.Close();
            stream = File.OpenRead("./remind.tmp");

            if (formatter.Deserialize(stream) == Reminders)
            {
                throw new Exception("Inconsistent database");
            }
            else
            {
                stream.Close();
                File.Copy("./remind.tmp", "./remind", true);
            }
        }

        public void Add(DateTime time, string message, string nick, string ptoken)
        {
            Reminder r = new Reminder()
            {
                EndDate = time,
                StartDate = DateTime.UtcNow,
                Nick = nick,
                Token = ptoken,
                Message = message
            };

            Reminders.Add(r);
            Save();
            Added.Set();
        }

        public void TimingLoop()
        {
            while (true)
            {
                try
                {
                    Reminders.RemoveAll(r => r.GetSpan().TotalSeconds < 0.1);

                    if (Reminders.Any())
                    {
                        var nearest = Reminders.Min(r => r.GetSpan());
                        Console.WriteLine(nearest);
                        Added.BetterWaitOne(nearest);
                    }
                    else
                    {
                        Console.WriteLine($"Waiting for reminder to be added");
                        Added.WaitOne();
                    }

                    Added.Reset();

                    var eligible = Reminders.Where(r => r.GetSpan().TotalSeconds < 2).ToList();
                    Console.WriteLine($"Have {eligible.Count} eligible reminders");

                    foreach (var reminder in eligible)
                    {
                        ReminderDone?.Invoke(reminder);

                        SeenTracker[reminder] = new ManualResetEvent(false);
                        SeenTrackedNicks.Add(reminder.Nick);
                        var trimmed = reminder.Token.Substring(0, reminder.Token.IndexOf('/'));

                        new Thread((ThreadStart)delegate
                        {
                            if (!SeenTracker[reminder].WaitOne(Config.GetInt("remind.telldelay")))
                            {
                                if(!string.IsNullOrWhiteSpace(reminder.Message))
                                    TellManager.Tell(trimmed, "topkek_next", reminder.Nick, string.Format("You asked to be reminded of \"{0}\" at {1}, but missed the reminder", reminder.Message, reminder.StartDate));
                                else
                                    TellManager.Tell(trimmed, "topkek_next", reminder.Nick, string.Format("You asked for a reminder at {1}, but missed it", reminder.Message, reminder.StartDate));
                            }

                            SeenTracker.Remove(reminder);
                            SeenTrackedNicks.Remove(reminder.Nick);
                        }).Start();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        }

        public bool GetLongestParsableDateTime(string str, TimeSpan timezone, out string unconsumed, out DateTime time)
        {
            var words = str.Split(' ');

            int longest_len = -1;
            unconsumed = str;
            time = DateTime.UtcNow;

            for(int i = 0; i < words.Length; i++)
            {
                for(int j = 1; j < words.Length - (i - 1); j++)
                {
                    string substring = string.Join(" ", words.Skip(i).Take(j));
                    int length = j;

                    if (double.TryParse(substring, out double temp_double) && !substring.StartsWith("0") && !substring.EndsWith("0"))
                        continue;

                    if(DateTime.TryParse(substring, out DateTime temp_time) && length > longest_len)
                    {
                        time = temp_time - timezone;
                        longest_len = length;
                        unconsumed = string.Join(" ", words.Take(i).Concat(words.Skip(i + j)));
                    }

                    //Console.WriteLine("{0}, {1}, {2}", i, j, string.Join(" ", words.Skip(i).Take(j)));
                }
            }

            return longest_len != -1;
        }

        public DateTime Get(string str, TimeSpan timezone, out string left)
        {
            var days = new string[]
            {
                "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
                "mon", "tue", "wed", "thu", "fri", "sat", "sun"
            };

            var dayofweeks = new DayOfWeek[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            };

            str = str.Replace("tomorrow", DateTime.Now.AddDays(1).ToShortDateString());
            str = str.Replace("midnight", "00:00");

            DateTime n = DateTime.UtcNow + timezone;

            if (GetLongestParsableDateTime(str, timezone, out left, out n)) // get UTC time from local human readable time
            {
                while (n < DateTime.UtcNow)
                    n = n.AddDays(1);

                return n;
            }
            n = DateTime.UtcNow;

            DateTime base_date = DateTime.UtcNow + timezone;

            var words = str.Split(' ').Select(s => s.ToLower()).ToArray();
            bool[] to_remove = new bool[words.Length];

            for (int i = 0; i < words.Length - 1; i++)
            {
                if (words[i] == "next")
                {
                    if (days.Contains(words[i + 1]))
                    {
                        var day = dayofweeks[(Array.IndexOf(days, words[i + 1]) % 7)];

                        DateTime orig = n;

                        n = n.AddDays(7);

                        while (n.DayOfWeek != day)
                            n = n.AddDays(1);

                        base_date = n;
                        //break;

                        to_remove[i] = true;
                        to_remove[i + 1] = true;
                    }
                }
                else if (words[i] == "this")
                {
                    if (days.Contains(words[1]))
                    {
                        var day = dayofweeks[(Array.IndexOf(days, words[i + 1]) % 7)];

                        DateTime orig = n;

                        if (n.DayOfWeek == day)
                            n = n.AddDays(1);

                        while (n.DayOfWeek != day)
                            n = n.AddDays(1);

                        base_date = n;

                        to_remove[i] = true;
                        to_remove[i + 1] = true;
                        //break;
                    }
                }

                int hour = -1;
                int minute = 0;

                if(words[i].EndsWith("am") || words[i].EndsWith("pm"))
                {
                    if (int.TryParse(words[i].Substring(0, words[i].Length - 2), out hour))
                    {
                        if (words[i].EndsWith("pm"))
                            hour += 12;

                        to_remove[i] = true;
                    }
                    else
                    {
                        continue;
                    }
                }
                else if(i > 0 && words[i] == "am" || words[i] == "pm")
                {
                    if (int.TryParse(words[i - 1], out hour))
                    {
                        if (words[i].EndsWith("pm"))
                            hour += 12;

                        to_remove[i] = true;
                        to_remove[i - 1] = true;
                    }
                    else
                        continue;
                }
                else if(words[i].Contains(":"))
                {
                    var fragments = words[i].Split(':');

                    if(fragments.Length == 2)
                    {
                        if(int.TryParse(fragments[0], out int protohour) && protohour <= 23 && protohour >= 0)
                        {
                            if (int.TryParse(fragments[1], out int protominute) && protohour <= 59 && protohour >= 0)
                            {
                                hour = protohour;
                                minute = protominute;

                                to_remove[i] = true;
                            }
                        }
                    }
                }

                if (hour != -1)
                {
                    base_date = new DateTime(base_date.Year, base_date.Month, base_date.Day, hour, minute, 0);

                    while (base_date < DateTime.UtcNow)
                        base_date = base_date.AddDays(1);
                }
            }

            base_date -= timezone;

            List<string> left_parts = new List<string>();

            for(int i = 0; i < words.Length; i++)
            {
                if (to_remove[i])
                    continue;

                left_parts.Add(words[i]);
            }

            left = string.Join(" ", left_parts);
            //return DateTime.Now;

            var amount = Parse(left, out left);

            return base_date + amount + TimeSpan.FromMilliseconds(10);
        }

        public TimeSpan Parse(string str, out string left)
        {
            var b_d = DateTime.UtcNow;
            var base_date = b_d;
            var words = str.Split(' ');
            bool[] to_remove = new bool[words.Length];

            Dictionary<string, string> mappings = new Dictionary<string, string>()
            {
                {"y", "year" },
                {"m", "minute" },
                {"s", "second" },
                {"d", "day" },
                {"w", "week" },
                {"h", "hour" }
            };

            for (int i = 0; i < words.Length - 1; i++)
            {
                double amount = 0;
                string unit = words[i + 1].ToLower().TrimEnd('s');

                if (!double.TryParse(words[i], out amount))
                {
                    if (double.TryParse(words[i].Substring(0, words[i].Length - 1), out amount) &&
                        mappings.ContainsKey(words[i][words[i].Length - 1].ToString()))
                    {
                        unit = mappings[words[i][words[i].Length - 1].ToString()];
                        to_remove[i] = true;
                        to_remove[i + 1] = true;
                    }
                    else
                        continue; 
                }
                else
                    to_remove[i] = true;

                to_remove[i + 1] = !to_remove[i + 1];

                switch (unit)
                {
                    case "year":
                        while (amount >= 1)
                        {
                            base_date = base_date.AddYears(1);
                            amount -= 1;
                        }

                        base_date = base_date.AddDays(amount * (DateTime.IsLeapYear(base_date.Year) ? 366 : 365));
                        break;
                    case "month":
                        while (amount >= 1)
                        {
                            base_date = base_date.AddMonths(1);
                            amount -= 1;
                        }

                        base_date = base_date.AddDays(amount * DateTime.DaysInMonth(base_date.Year, base_date.Month));
                        break;
                    case "week":
                        base_date = base_date.AddDays(amount * 7);
                        break;
                    case "day":
                        base_date = base_date.AddDays(amount);
                        break;
                    case "hour":
                        base_date = base_date.AddHours(amount);
                        break;
                    case "minute":
                        base_date = base_date.AddMinutes(amount);
                        break;
                    case "second":
                        base_date = base_date.AddSeconds(amount);
                        break;
                    case "millisecond":
                        base_date = base_date.AddMilliseconds(amount);
                        break;
                    case "shrek":
                        base_date = base_date.AddMinutes(amount * 90);
                        break;
                    case "millishrek":
                        base_date = base_date.AddMinutes(amount * 0.09);
                        break;
                }

                i++;
            }

            List<string> left_parts = new List<string>();

            for (int i = 0; i < words.Length; i++)
            {
                if (to_remove[i])
                {
                    if(i < words.Length - 2 && words[i + 1] == "from" && words[i + 2] == "now")
                    {
                        to_remove[i + 1] = to_remove[i + 2] = true;
                    }
                    continue;
                }

                left_parts.Add(words[i]);
            }

            left = string.Join(" ", left_parts);

            return base_date - b_d;
        }
    }

    [Serializable]
    public class Reminder
    {
        public string Nick { get; set; }
        public string Token { get; set; }
        public string Message { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public Reminder()
        {

        }

        public TimeSpan GetSpan()
        {
            return EndDate - DateTime.UtcNow;
        }
    }

    public static class Extensions
    {
        public static bool BetterWaitOne(this WaitHandle handle, TimeSpan span)
        {
            if(span.TotalMilliseconds < int.MaxValue)
            {
                return handle.WaitOne(span);
            }

            var max_span = new TimeSpan(0, 0, 0, 0, int.MaxValue);

            while(span > max_span)
            {
                span -= max_span;

                if (handle.WaitOne(max_span))
                    return true;
            }

            return handle.WaitOne(span);
        }
    }
}