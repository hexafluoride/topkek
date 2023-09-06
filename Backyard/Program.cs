using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HeimdallBase;
using InfixParser.Functions;
using InfixParser.Operators;
using InfixParser.Units;
using Newtonsoft.Json.Linq;
using OsirisBase;
using UserLedger;

namespace Backyard
{
    class Program
    {
        static void Main(string[] args)
        {
            new Backyard().Start(args);
        }
    }

    public partial class Backyard : LedgerOsirisModule
    {
        public QuoteUtil Quotes { get; set; } = new();
        public TellManager TellManager { get; set; }
        public RemindManager RemindManager { get; set; }
        public HashSet<string> DoNotUseIndex { get; set; } = new();
        public (string, string, string)[] DoNotUse { get; set; }
        public HashSet<string> PolicedSources { get; set; } = new();
        
        public void Start(string[] args)
        {
            Name = "backyard";
            Commands = new Dictionary<string, MessageHandler>()
            {
                {".c ", Calculate},
                {".remind", Remind},
                {".reminders", ListReminders},
                {".snooze", SnoozeReminder},
                {".tz", SetTimezone},
                {".metric", SetUnits},
                {".imperial", SetUnits},
                {".units", GetUnits},
                {".insult", Insult},
                {".compliment", Compliment},
                {".tell ", Tell},
                {".ctell ", Tell},
                {".yiff", Yiff},
                {"$yiffall", YiffAll},
                {"$yiffforever", YiffAll},
                {"$config", Configuration},
                {"$rehash", Rehash},
                {".comic", GenerateComic},
                {".ttscomic", GenerateComic},
                {".comicimg ", SetComicImage},
                {".comicvoice", SetComicVoice},
                {"s/", TrySubstitute},
                {"", ChannelMessage},
                {".ram", ShowRam},
                {".quote", BetterQuote},
            };

            
            Init(args, BackyardMain);
        }

        void BackyardMain()
        {
            UnitDefinitions.Quantities["Time"].BaseUnit = UnitDefinitions.Quantities["Time"]["Second"];
            UnitDefinitions.Quantities["Temperature"].BaseUnit = UnitDefinitions.Quantities["Temperature"]["Celsius"];
            UnitDefinitions.Quantities["Length"].BaseUnit = UnitDefinitions.Quantities["Length"]["Meter"];
            UnitDefinitions.Quantities["Mass"].BaseUnit = UnitDefinitions.Quantities["Mass"]["Gram"];
            UnitDefinitions.Quantities["Voltage"].BaseUnit = UnitDefinitions.Quantities["Voltage"]["Volt"];
            UnitDefinitions.Quantities["Current"].BaseUnit = UnitDefinitions.Quantities["Current"]["Ampere"];
            UnitDefinitions.Quantities["Amount"].BaseUnit = UnitDefinitions.Quantities["Amount"]["Mole"];
            UnitDefinitions.Quantities["Data Amount"].BaseUnit = UnitDefinitions.Quantities["Data Amount"]["Byte"];

            OperatorRegistry.Initialize();
            FunctionRegistry.Initialize();
            
            TellManager = new TellManager();
            TellManager.Load();

            RemindManager = new RemindManager();
            RemindManager.Load();
            // RemindManager.ReminderDone += (r) =>
            // {
            //     LastReminder[r.Nick] = r;
            //
            //     lock (Connection.Client)
            //     {
            //         if (!string.IsNullOrWhiteSpace(r.Message))
            //             SendMessage(
            //                 $"{r.Nick}, you asked to be reminded of \"{r.Message}\" at {r.StartDate + GetTimezone(r.Token, r.Nick)}.",
            //                 r.Token);
            //         else
            //             SendMessage(
            //                 $"{r.Nick}, you asked for a reminder at {r.StartDate + GetTimezone(r.Token, r.Nick)}.",
            //                 r.Token);
            //     }
            // };
            
            // Task.Factory.StartNew(RemindManager.TimingLoop);

            if (File.Exists("yiffy.json"))
                YiffDatabase = JObject.Parse(File.ReadAllText("yiffy.json"));
            
            if (File.Exists("wetfish.csv"))
                Quotes.LoadFromCsv("Wetfish", "wetfish.csv");
            
            if (File.Exists("donotuse.json"))
            {
                var local = JsonDocument.Parse(File.ReadAllText("donotuse.json")).RootElement.EnumerateArray()
                    .Select(j => j.EnumerateArray().Select(p => p.GetString()).ToArray()).ToArray();

                var len = local[0].Length;
                var tuples = new List<(string, string, string)>();
                
                for (int i = 0; i < len; i++)
                {
                    (var dontUse, var instead, var description) = (local[0][i], local[1][i], local[2][i]);

                    if (dontUse is null || instead is null || description is null)
                        continue;
                    
                    if (dontUse.Contains('('))
                    {
                        dontUse = dontUse.Remove(dontUse.IndexOf('('), dontUse.IndexOf(')') - (dontUse.IndexOf('(') - 1)).Trim();
                    }

                    var candidates = new List<string>() { dontUse };
                    if (dontUse.Contains('/'))
                        candidates = dontUse.Split('/').ToList();

                    if (dontUse.Contains(','))
                        candidates = dontUse.Split(',').ToList();

                    candidates = candidates.Select(c => c.ToLowerInvariant().Trim()).ToList();

                    description = description.Replace("\n", "");
                    
                    foreach (var candidate in candidates)
                    {
                        if (DoNotUseIndex.Add(candidate))
                            tuples.Add((candidate, instead, description));
                    }
                }

                DoNotUse = tuples.ToArray();

                foreach (var tuple in DoNotUse)
                {
                    Console.WriteLine($"Do not say {tuple.Item1}, use {tuple.Item2} instead. {tuple.Item3}");
                }

                foreach (var source in Config.GetArray<string>("police.sources"))
                    PolicedSources.Add(source);
            }

            Connection.SendMessage("t", "tt", "router");
            var modules = GetModules();
            Console.WriteLine(modules.Length);
            Connection.SendMessage("t", "ttt", "router");
            // Connection.SendMessage("t", "tttt", "router");
        }

        void BetterQuote(string args, string source, string n)
        {
            var trimmedSource = source.Split('/')[0];

            if (Quotes?.Quotes?.Any(q => q.Source == trimmedSource) != true)
                return;

            var query = args.Substring(".quote".Length).Trim().ToLowerInvariant();
            Quote result = null;

            if (int.TryParse(query, out int quoteId) && quoteId > 0 && quoteId <= Quotes.HighestId)
            {
                result = Quotes.Quotes.FirstOrDefault(q => q.Source == trimmedSource && q.Id == quoteId);

                if (result == null)
                {
                    SendMessage($"Couldn't find quote #{quoteId}.", source);
                    return;
                }
            }
            else
            {
                result = Quotes.GetNextQuote(query, trimmedSource);   
            }

            if (result != null)
            {
                SendMessage($"[#{result.Id}] [{result.Time}] {(result.Score != 0 ? $"[Score: {result.Score}] " : "")}{result.Contents}", source);
            }
            else
            {
                SendMessage("End of results.", source);
            }
        }
        
        void ChannelMessage(string args, string source, string n)
        {
            lock (LastLines)
            {
                if (!LastLines.ContainsKey(source))
                    LastLines[source] = new();
                
                LastLines[source].Add((n, args));
                if (LastLines[source].Count > 50)
                    LastLines[source].RemoveAt(0);
            }
            
            if(RemindManager.SeenTrackedNicks.Contains(n))
                if (RemindManager.SeenTracker.Any(s => s.Key.Nick == n))
                    foreach (var p in RemindManager.SeenTracker.Where(s => s.Key.Nick == n))
                        p.Value.Set();

            if (PolicedSources.Contains(source))
            {
                foreach ((var forbidden, var instead, var description) in DoNotUse)
                {
                    var index = args.IndexOf(forbidden, StringComparison.InvariantCultureIgnoreCase);
                    if (index == -1)
                        continue;
                    
                    var end = index + forbidden.Length;
                    if ((index == 0 || !char.IsLetterOrDigit(args[index - 1])) && (end == args.Length || !char.IsLetterOrDigit(args[end])))
                    {
                        SendMessage($"{n}: Try to avoid saying \x02{forbidden}\x02; use \x02{instead}\x02 instead. {description}", source);
                        break;
                    }
                }
            }
            
            if (!args.StartsWith(".tell") && !args.StartsWith(".ctell")) // TODO unhack
            {
                var tells = TellManager.GetTells(source, n);

                if (!tells.Any())
                    return;

                foreach (var tell in tells)
                {
                    SendMessage(tell.ToString(), source);
                    TellManager.Expire(tell);
                }

                TellManager.Save();
            }
        }

        void Calculate(string args, string source, string nick)
        {
            try
            {
                string query = args.Substring(".c".Length).Trim();
                var sides = query.Split(new[] { " to ", " in " }, StringSplitOptions.RemoveEmptyEntries);

                if (sides.Length == 0)
                {
                    SendMessage("That doesn't make much sense.", source);
                    return;
                }

                if (sides.Length == 1)
                {
                    var measure = InfixParser.Program.Evaluate(sides[0]);

                    if (measure.Value == measure.Simplify().Value)
                        SendMessage(measure.ToString(), source);
                    else
                        SendMessage(string.Format("{0} = {1}", measure, measure.Simplify()), source);
                    //SendMessage(measure.ToString(), source);
                    return;
                }

                //var to_convert = MeasureParser.ParseMeasure(sides[0]);
                var value_side = sides[0];
                var unit_side = sides[1];

                if(!sides[0].Any(char.IsDigit))
                {
                    value_side = sides[1];
                    unit_side = sides[0];
                }

                if (!value_side.Any(char.IsDigit) && value_side.StartsWith("a ", true, CultureInfo.InvariantCulture))
                    value_side = "1" + value_side.Substring(1);

                var to_convert = InfixParser.Program.Evaluate(value_side);
                var target = MeasureParser.ParseUnitCollection(unit_side);

                SendMessage(string.Format("{0} = {1}", to_convert, to_convert.ConvertTo(target)), source);
            }
            catch (Exception ex)
            {
                SendMessage("Oops: " + ex.Message, source);
            }
        }
        
        void Tell(string args, string source, string n)
        {
            //args = args.Substring(">tell".Length).Trim();
            var restrictContext = args.ToLower()[1] == 'c';
            var tellContext = source;

            if (restrictContext)
                args = args.Substring(".ctell".Length).Trim();
            else
            {
                tellContext = source.Substring(0, source.IndexOf('/'));
                args = args.Substring(".tell".Length).Trim();
            }

            if (Config.Contains("tell.disabled", source))
                return;

            string nick = args.Split(' ')[0];
            string message = string.Join(" ", args.Split(' ').Skip(1));

            string[] tell_responses = new string[] { "teehee", "rawr xD" };

            TellManager.Tell(tellContext, n, nick, message);
            SendMessage(Random.NextDouble() > 0.5 ? "okay buddy!!!" : tell_responses[Random.Next(tell_responses.Length)], source);
        }
    }
}