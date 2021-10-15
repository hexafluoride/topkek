using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoData
{
    public class TimeUtils
    {
        public static DateTime Get(string str, bool subtract = false)
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

            DateTime n = DateTime.Now;
            if (DateTime.TryParse(str, out n))
            {
                return n;
            }
            n = DateTime.Now;

            DateTime base_date = DateTime.Now;

            var words = str.Split(' ').Select(s => s.ToLower()).ToArray();

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
                            }
                        }
                    }
                }

                if(hour != -1)
                    base_date = new DateTime(base_date.Year, base_date.Month, base_date.Day, hour, minute, 0);
            }

            var amount = Parse(str);

            if (subtract)
                return base_date - amount;
            else
                return base_date + amount;
        }

        static TimeSpan Parse(string str)
        {
            var b_d = DateTime.Now;
            var base_date = b_d;
            var words = str.Split(' ');

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
                    }
                    else
                        continue;
                }

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
                }

                i++;
            }

            return base_date - b_d;
        }
    }
}
