using System.Collections.Concurrent;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HeimdallBase;
using OsirisBase;
using UserLedger;

namespace ChatterNew
{
    class Program
    {
        static void Main(string[] args)
        {
            new ChatterNew().Start(args);
        }
    }

    public partial class ChatterNew : LedgerOsirisModule
    {
        public ChatterUtil ChatterUtil;
        
        public void Start(string[] args)
        {
            Name = "chatter+";
            Directory.CreateDirectory("source_states");

            Commands = new Dictionary<string, MessageHandler>()
            {
                {".temp", HandleTemp},
                {".nick", HandleNick},
                {".wipe", HandleWipe},
                {".history", HandleHistory},
                {".timings", PrintTimings},
                {".chat", TriggerChat},
                // {".complete ", EnqueueChatWrapper},
                // {".clean", EnqueueChatWrapper},
                {"", RecordLine},
                {".encode ", Tokenize},
                {".tokenize ", Tokenize},
                {".decode ", Detokenize},
                {".detokenize ", Detokenize},
                // {"$benchmark", Benchmark},
                // {".usepast", ChatWithPast},
                {".spoof", AddHistory},
                // {".ponder", HandlePonder},
                // {".fill", FillForm},
                // {".gptram", GetRam},
                // {".notify", Notify}
                {".gptfuse", GptDiffuse},
                {".dump", DumpSource},
                {".rollback", Rollback},
                {".prompt", HandlePrompt},
                {".editprompt", EnterPromptEditing},
                {".endprompt", QuitPromptEditing},
                {".repeat", HandleRepeatFilter},
                {".reroll", Reroll}
            };

            // new Thread(ChatLoop).Start();
            ChatterUtil = new ChatterUtil(this);

            Init(args);
        }

        private Dictionary<string, StringBuilder> PendingPrompts = new();
        private Dictionary<string, string> PromptAuthors = new();
        private Dictionary<string, DateTime> PromptEditTime = new();

        public void GptDiffuse(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }

            Complete($".complete {session.Config.AssignedNick} .diffuse copies=4 \"", source, n);
        }

        public void Complete(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }

            args = args.Substring(".complete".Length).TrimStart();
            string targetNick = args.Split(' ')[0];
            string completionPrefix = args.Substring(targetNick.Length + 1).TrimStart();
            

            UsageRecord record = GetThrottleWarning(args, source, n, involuntary: false);
            if (record.Warn)
            {
                SendMessage("You are spamming me a bit too much. Give me some room to breathe between requests.", source);
                return;
            }

            if (!record.Allow)
            {
                Console.WriteLine($"Usage not allowed: {source}");
                return;
            }

            IChatLine? generatedLine = session.SimulateChatFromPerson(targetNick, completionPrefix);
            if (generatedLine is null)
            {
                SendMessage("oops", source);
                return;
            }
            SendMessage(generatedLine.Contents, source);
        
            RecordUsage(record);
            ChatterUtil.SaveSession(source);
        }

        public void Reroll(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }
            
            UsageRecord record = GetThrottleWarning(args, source, n, involuntary: true);
            // bool shouldReply = shouldReply && record.Allow;
            if (!record.Allow)
            {
                SendMessage("Let's cool down for a bit.", source);
                return;
            }

            int times = 1;
            if (!int.TryParse(args.Substring(".reroll".Length).Trim(), out times) || times < 1 || times > 4)
            {
                times = 1;
            }

            var lastSelfLine = session.History.LastOrDefault(line => line.Origin == "generated");
            if (lastSelfLine is null)
            {
                SendMessage("I can't see any messages to reroll.", source);
                return;
            }

            var lastSelfLineIndex = session.History.IndexOf(lastSelfLine);
            var subsequentLines = session.History.Skip(lastSelfLineIndex + 1).ToList();
            session.RollbackHistory(subsequentLines.Count);

            for (int i = 0; i < times; i++)
            {
                session.RollbackHistory(1);
                var nextSelfLine = session.SimulateChatFromPerson(session.Config.AssignedNick);
                if (nextSelfLine is null)
                {
                    // restore history
                    session.AddHistoryLine(lastSelfLine, false);
                    subsequentLines.ForEach(l => session.AddHistoryLine(l, false));
                    SendMessage("oops", source);
                    return;
                }

                SendMessage(nextSelfLine.Contents, source);
            }

            subsequentLines.ForEach(l => session.AddHistoryLine(l, false));
            RecordUsage(record);
        }
        
        public void QuitPromptEditing(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }

            var config = session.Config;
            if (!config.AllowPromptOverride)
            {
                SendMessage("The prompt cannot be changed in this channel.", source);
                return;
            }

            if (!PendingPrompts.ContainsKey(source))
            {
                SendMessage("No prompt is currently being entered.", source);
                return;
            }

            if (PromptAuthors[source] != n)
            {
                var age = DateTime.UtcNow - PromptEditTime[source];
                if (age.TotalSeconds < 60)
                {
                    SendMessage($"{n} is the one who is editing the prompt. Try again in {65 - age.TotalSeconds} seconds to abort this edit.", source);
                    return;
                }
                else
                {
                    SendMessage("Ending prompt without saving.", source);
                    PromptEditTime.Remove(source);
                    PromptAuthors.Remove(source);
                    PendingPrompts.Remove(source);
                    return;
                }
            } 

            string finalizedPrompt = PendingPrompts[source].ToString();
            PromptEditTime.Remove(source);
            PromptAuthors.Remove(source);
            PendingPrompts.Remove(source);

            Console.WriteLine($"Finalized prompt: \"{finalizedPrompt}\"");
            
            if (finalizedPrompt.Length > 2048)
            {
                SendMessage($"That prompt is way too long (>2k chars).", source);
                return;
            }

            List<int> promptTokens = session.Context.Tokenize(finalizedPrompt);
            int maxTokens = 1000;
            if (promptTokens.Count > maxTokens)
            {
                SendMessage($"That prompt is way too long ({promptTokens.Count} tokens > {maxTokens} max)", source);
                return;
            }

            session.Config.Prompt = finalizedPrompt;
            session.Config.PromptAuthor = n;
            ChatterUtil.SaveSession(source);
            SendMessage($"{n}: Saved new prompt, {promptTokens.Count} tokens. Current context length is {session.HistoryLength} tokens, you might wanna .wipe the context for a fresh start", source);
        }
        
        public void EnterPromptEditing(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }

            var config = session.Config;
            if (!config.AllowPromptOverride)
            {
                SendMessage("The prompt cannot be changed in this channel.", source);
                return;
            }

            if (PendingPrompts.ContainsKey(source))
            {
                return;
            }

            string promptFragment = args.Substring(".editprompt".Length);
            if (promptFragment.Length > 0)
            {
                promptFragment = promptFragment.Substring(1);
            }
            PendingPrompts[source] = new StringBuilder(promptFragment);
            PromptAuthors[source] = n;
            PromptEditTime[source] = DateTime.UtcNow;
            
            SendMessage("OK, waiting for you to say .endprompt", source);
        }

        private Dictionary<string, DateTime> LastPromptPrint = new();
        public void HandlePrompt(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }

            var config = session.Config;
            if (!config.AllowPromptOverride)
            {
                SendMessage("The prompt cannot be changed in this channel.", source);
                return;
            }

            args = args.Substring(".prompt".Length);

            if (args.Trim() == "default")
            {
                SendMessage("Resetting this channel's prompt to the default prompt.", source);
                session.Config.Prompt = null;
                session.Config.PromptAuthor = null;
                ChatterUtil.SaveSession(source);
                return;
            }
            if (args.Length > 0)
            {
                SendMessage("To edit the current prompt, use '.editprompt'.", source);
                return;
            }

            if (LastPromptPrint.ContainsKey(source))
            {
                if ((DateTime.UtcNow - LastPromptPrint[source]).TotalSeconds < 3)
                {
                    SendMessage("Slow down, buddy.", source);
                    return;
                }
            }

            StringBuilder output = new();
            string currentPromptString = session.GetPromptTemplate();
            int[] currentPromptTokens = session.Context.Tokenize(currentPromptString).ToArray();
            // int[] currentPromptTokens = session.GetPromptTokens() ?? new int[0];
            // string currentPromptString = session.Context.Detokenize(currentPromptTokens);
            if (config.Prompt == null)
            {
                output.Append($"The default prompt is currently active. ");
            }
            else
            {
                output.Append($"The current prompt was set by {config.PromptAuthor ?? "???"}. ");
            }

            output.AppendLine($"It is {currentPromptString.Count(c => c == '\n')} lines and {currentPromptTokens.Length} tokens long:");
            output.AppendLine(currentPromptString);
            output.AppendLine("That concludes the current prompt.");
            LastPromptPrint[source] = DateTime.UtcNow;
            
            SendMessage(output.Replace('\n', '\0').ToString(), source);
        }
        
        public void Rollback(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }

            int rollbackNumber = 1;
            args = args.Substring(".rollback".Length).Trim();
            if (!args.Any())
            {
                // roll back to last self-utterance
                while (session.History.Count > rollbackNumber &&
                       session.History[^rollbackNumber].Nick != session.Config.AssignedNick)
                {
                    rollbackNumber++;
                }
            }
            else if (!int.TryParse(args, out rollbackNumber))
            {
                rollbackNumber = -1;
            }

            if (rollbackNumber > 0 && rollbackNumber < session.History.Count)
            {
                session.RollbackHistory(rollbackNumber);
                IChatLine? lastLine = session.History.LastOrDefault();
                if (lastLine is null)
                {
                    SendMessage($"History wiped after rolling back {rollbackNumber} lines.", source);
                }
                else
                {
                    string lastLineSnippet = lastLine.Contents.Substring(Math.Max(0, lastLine.Contents.Length - 4));
                    SendMessage(
                        $"Rolled back {rollbackNumber} lines, last line in new history ends with {(lastLine.Contents.Length > 7 ? "..." : "")}{lastLineSnippet}",
                        source);
                }
            }
            else
            {
                SendMessage($"I don't like that number {rollbackNumber} :(", source);
            }
        }

        public void DumpSource(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                SendMessage("no session", source);
                return;
            }
            
            Console.WriteLine($"Dumping session:");

            var tokens = session.Context.InputTokens ?? new();

            void PrintRange(int start, int end)
            {
                for (int i = start; i < end && i < tokens.Count; i++)
                {
                    Console.Write($"{tokens[i]} (\"{session.Context.TokenToText(tokens[i])}\") ");
                }
                Console.WriteLine();
                // Console.WriteLine(session.Context.Tokenize());
            }
            // var lineTokens = new List<int>();
            int lastIndex = 0;
            while (true)
            {
                var nextNewLine = tokens.IndexOf(13, lastIndex);
                if (nextNewLine == -1)
                {
                    PrintRange(lastIndex, tokens.Count - 1);
                    break;
                }
                
                PrintRange(lastIndex, nextNewLine);
                lastIndex = nextNewLine + 1;
            }

            SendMessage("printed", source);
        }

        public class UsageRecord
        {
            public string Nick { get; set; }
            public string Source { get; set; }
            public DateTime TimeReceived { get; set; }
            public DateTime TimeFulfilled { get; set; }
            public bool Fulfilled { get; set; }
            public bool Warn { get; set; }
            public bool Allow { get; set; }

            public void MarkFulfilled()
            {
                TimeFulfilled = DateTime.UtcNow;
                Fulfilled = true;
            }
        }

        public List<UsageRecord> ChatUsages = new();
        public Dictionary<string, DateTime> LastNotified = new();

        public int GetUsagesInLastHour(string source)
        {
            var currentHour = DateTime.UtcNow.Hour;
            return ChatUsages.Count(u => u.Allow && u.Fulfilled && u.Source == source && u.TimeFulfilled.Hour == currentHour);
        }
        
        public UsageRecord GetThrottleWarning(string args, string source, string n, bool involuntary = false)
        {
            var thisUsage = new UsageRecord()
            {
                Nick = n,
                Source = source,
                TimeReceived = DateTime.UtcNow
            };

            var lastRelevantUsages = new List<UsageRecord>() { thisUsage };

            for (int i = 0; i < 50; i++)
            {
                var targetIndex = ChatUsages.Count - (i + 1);
                if (targetIndex < 0)
                {
                    continue;
                }

                var usage = ChatUsages[targetIndex];
                if (string.Equals(usage.Nick, n, StringComparison.InvariantCultureIgnoreCase))
                {
                    var previousUsage = lastRelevantUsages.LastOrDefault();
                    if (previousUsage is null)
                    {
                        lastRelevantUsages.Add(usage);
                    }
                    else
                    {
                        var gap = Math.Abs((previousUsage.TimeReceived - usage.TimeFulfilled).TotalSeconds);
                        Console.WriteLine($"Gap between {usage} and {previousUsage}: {gap:0.00}s");
                        
                        if (gap < 1d)
                        {
                            lastRelevantUsages.Add(usage);
                        }
                        else
                        {
                            Console.WriteLine($"Large gap, breaking at {lastRelevantUsages.Count} usages");
                            break;
                        }
                    }
                }
            }

            lastRelevantUsages.Reverse();

            if (lastRelevantUsages.Count >= Config.GetInt("chatter.antispam.user"))
            {
                Console.WriteLine($"Relevant spammy usage count {lastRelevantUsages.Count} exceeds limit");
                thisUsage.MarkFulfilled();
                ChatUsages.Add(thisUsage);

                thisUsage.Allow = false;
                if (!LastNotified.ContainsKey(n) || (DateTime.UtcNow - LastNotified[n]).TotalSeconds > 5)
                {
                    LastNotified[n] = DateTime.UtcNow;
                    // return "You are spamming me a bit too much. Give me some room to breathe between requests.";
                    thisUsage.Warn = true;
                }
                else
                {
                }
            }
            else
            {
                thisUsage.Allow = true;
            }
                
            // ChatQueue.Enqueue((args, source, n));
            return thisUsage;
        }

        public void RecordUsage(UsageRecord record)
        {
            record.MarkFulfilled();
            ChatUsages.Add(record);

            while (ChatUsages.Count > 1000)
            {
                ChatUsages.RemoveAt(0);
            }
        }

        public void TriggerChat(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }

            int chatCount = 1;
            string nick = session.Config.AssignedNick;
            if (args.StartsWith(".chatas"))
            {
                args = args.Substring(".chatas".Length).Trim();
                nick = args;
            }
            else if (args.StartsWith(".chat "))
            {
                args = args.Substring(".chat".Length).Trim();
                if (int.TryParse(args, out chatCount))
                {
                    if (chatCount < 1 || chatCount > 4)
                    {
                        chatCount = 1;
                    }
                }
                else
                {
                    chatCount = 1;
                }
            }

            UsageRecord record = GetThrottleWarning(args, source, n, involuntary: false);
            if (record.Warn)
            {
                SendMessage("You are spamming me a bit too much. Give me some room to breathe between requests.", source);
                return;
            }

            if (!record.Allow)
            {
                Console.WriteLine($"Usage not allowed: {source}");
                return;
            }

            for (int i = 0; i < chatCount; i++)
            {
                IChatLine? generatedLine = session.SimulateChatFromPerson(nick);
                if (generatedLine is null)
                {
                    SendMessage("oops", source);
                    return;
                }
                SendMessage(generatedLine.Contents, source);
            }

            Thread.Sleep(500);

            RecordUsage(record);
            ChatterUtil.SaveSession(source);
        }
        
        public void RecordLine(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            
            ConfigBlock config = session.Config;
            if (PendingPrompts.ContainsKey(source) && PromptAuthors.ContainsKey(source) && PromptAuthors[source] == n && !args.StartsWith(".endprompt"))
            {
                PendingPrompts[source].AppendLine(args);
                Console.WriteLine($"Current pending prompt: {PendingPrompts[source]}");
                return;
            }
            
            // record line
            if (ChatterUtil.ShouldRecordLine(args, source, n))
            {
                IChatLine? chatLine = session.ProcessChatLine(args, source, n, "gpter");

                if (chatLine is not null)
                {
                }
                
                bool shouldReply = args.Contains("gpter", StringComparison.InvariantCultureIgnoreCase) && !Config.GetValue<bool>($"chatter.noreply.{source}");
                if (!shouldReply)
                {
                    // trigger random speak if chance
                    if (Config.Contains("chatter.spontaneous", source))
                    {
                        var chatChance = Config.GetDouble($"chatter.chance.{source}");
                        if (chatChance == 0)
                            chatChance = 0.02d;

                        shouldReply = Random.Shared.NextDouble() < chatChance;
                    }
                }

                UsageRecord record = GetThrottleWarning(args, source, n, involuntary: true);
                shouldReply = shouldReply && record.Allow;

                if (shouldReply && Config.GetInt($"chatter.throttle.{source}") > 0)
                {
                    var usageCount = GetUsagesInLastHour(source);
                    if (usageCount > Config.GetInt($"chatter.throttle.{source}"))
                    {
                        if (session.LastNotified == DateTime.MinValue || (DateTime.UtcNow - session.LastNotified).TotalHours > 3)
                        {
                            SendMessage($"{n}: I am sorry, we cannot continue chatting here due to the channel rate limit. You can find me in #gpt, #gpt2, #gpt3, and #gpt4. This notice won't repeat for a while.", source);
                            session.LastNotified = DateTime.UtcNow;
                        }
                        
                        shouldReply = false;
                    }
                }
                
                if (shouldReply)
                {
                    session.AddHistoryLine(chatLine, false);
                    IChatLine? generatedLine = session.SimulateChatFromPerson(session.Config.AssignedNick);
                    if (generatedLine is null)
                    {
                        SendMessage("oops", source);
                        return;
                    }

                    SendMessage(generatedLine.Contents, source);
                    RecordUsage(record);
                }
                else
                {
                    session.AddHistoryLine(chatLine, true);
                }
                ChatterUtil.SaveSession(source);
            }
        }
        
        public void AddHistory(string args, string source, string n)
        {
            args = args.Substring(".spoof".Length).Trim();
            var parts = args.Split(' ');
            var targetNick = parts[0];
            var rest = string.Join(' ', parts.Skip(1));
            // var channel = GptUtil.GetChannel(source);
            var session = ChatterUtil.GetChatSession(source);
            if (session is null)
            {
                SendMessage("oops", source);
                return;
            }
            // var instance = GptUtil.GetModelInstance(source);
            var tokens = new List<int>();
            // var line = new ChatLine(targetNick, rest, tokens);
            if (targetNick == "gpter")
            {
                targetNick = session.Config.AssignedNick;
            }
            var line = session.ProcessChatLine(rest, source, targetNick, "gpter");
            tokens.AddRange(session.Context.Tokenize(line.ToString()));
            tokens.Add(ChatSession.NewlineToken);
            // GptUtil.StartInstanceIfNotStarted(source);
            // GptUtil.AnnotateTokens(line);
            // channel.CommitLine(line);
            session.AddHistoryLine(line, true);
            
            SendMessage($"{n}: okay, I'll pretend as if {targetNick} said that.", source);
        }
        
        public void HandleTemp(string args, string source, string n)
        {
            args = args.Substring("$temp".Length).Trim();
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }

            if (args.Length > 0)
            {
                var argParts = args.Split(' ');

                bool success = true;
                if (double.TryParse(argParts[0], out double temp))
                {
                    session.Config.Temperature = temp;
                    session.UpdateConfig();
                    if (argParts.Length >= 2)
                    {
                        if (double.TryParse(argParts[1], out double repetitionPenalty))
                        {
                            session.Config.RepetitionPenalty = repetitionPenalty;
                            session.UpdateConfig();
                        }
                        else
                        {
                            success = false;
                        }
                    }
                }
                else
                {
                    success = false;
                }

                SendMessage(success ? "success" : "oops", source);
                ChatterUtil.SaveSession(source);
            }
            
            SendMessage($"temperature = {session.Config.Temperature}, repetition_penalty = {session.Config.RepetitionPenalty}", source);
        }
        
        void HandleNick(string args, string source, string n)
        {
            args = args.Substring($"$nick".Length).Trim();
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            
            ConfigBlock config = session.Config;
            if (args.Any())
            {
                var tokenCount = ChatterUtil.CountTokens(source, args);

                if (tokenCount < 10)
                {
                    config.AssignedNick = args;
                    config.NickTokens = tokenCount;
                    session.UpdateConfig();
                    ChatterUtil.SaveSession(source);
                }
                else
                {
                    SendMessage($"{n}: Your nick was not good", source);
                }
            }
            
            SendMessage($"Speaking as {config.AssignedNick}", source);
        }

        void HandleWipe(string args, string source, string n)
        {
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }

            if (args.Trim().ToLowerInvariant() == ".wipeself")
            {
                int currentLineCount = session.History.Count;
                var cleanedHistory = session.History.Where(l => l.Origin != "generated").ToList();
                session.WipeHistory();
                for (int i = 0; i < cleanedHistory.Count; i++)
                {
                    session.AddHistoryLine(cleanedHistory[i], i == cleanedHistory.Count - 1);
                }
                SendMessage($"okay, {currentLineCount} -> {session.History.Count} lines", source);
            }
            else
            {
                session.WipeHistory();
                SendMessage($"okay", source);
            }
        }

        void HandlePonder(string args, string source, string n)
        {
            args = args.Substring(".ponder".Length).Trim();
            // var channel = GptUtil.GetChannel(source);
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            ConfigBlock config = session.Config;
            if (int.TryParse(args, out int newPonder) && newPonder >= 0)
            {
                config.PonderancesPerReply = newPonder;
                session.UpdateConfig();
                ChatterUtil.SaveSession(source);
            }
            SendMessage($"ponderances = {config.PonderancesPerReply}", source);
        }
        
        public void HandleHistory(string args, string source, string n)
        {
            args = args.Substring(".history".Length).Trim();
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            ConfigBlock config = session.Config;
            if (args.Length > 0)
            {
                var argParts = args.Split(' ');

                int nextStart = 0, nextTarget = 0;
                bool success = true;
                success = success && int.TryParse(argParts[0], out nextStart);
                if (argParts.Length >= 2)
                {
                    success = success && int.TryParse(argParts[1], out nextTarget);
                }

                if (nextStart < 50 || nextTarget < 50 || nextStart > 2000 || nextTarget > 1000 ||
                    nextStart <= nextTarget || nextStart - nextTarget < 50)
                {
                    success = false;
                }

                if (success)
                {
                    config.TrimStart = nextStart;
                    config.TrimTarget = nextTarget;
                    session.UpdateConfig();
                    ChatterUtil.SaveSession(source);
                }
                
                SendMessage(success ? "success" : "oops", source);
            }
            
            SendMessage($"trim_start = {config.TrimStart}, trim_target = {config.TrimTarget}, current context length = {session.Context.InputTokens.Count} ({session.HistoryLength} lines)", source);
        }

        public void HandleRepeatFilter(string args, string source, string n)
        {
            args = args.Substring(".repeat".Length).Trim();
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            ConfigBlock config = session.Config;
            if (args.Length > 0)
            {
                if (int.TryParse(args, out int newRepeat) && newRepeat is >= 0 and < 10)
                {
                    config.NoRepeatLines = newRepeat;
                    ChatterUtil.SaveSession(source);
                }
                else
                {
                    SendMessage("oops", source);
                }
            }
            
            SendMessage($"repeat_filter = {config.NoRepeatLines} (the first tokens of the last {config.NoRepeatLines} line(s) will be banned from starting a new line)", source);
        }

        void PrintTimings(string args, string source, string n)
        {
            args = args.Substring(".timings".Length).Trim();
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            ConfigBlock config = session.Config;
            config.PrintTimings = !config.PrintTimings;
            session.UpdateConfig();
            ChatterUtil.SaveSession(source);
            SendMessage($"Now {(config.PrintTimings ? "printing" : "not printing")} timing info", source);
        }
        void Notify(string args, string source, string n)
        {
            args = args.Substring(".notify".Length).Trim();
            IChatSession? session = ChatterUtil.GetChatSession(source, n, args);
            if (session is null)
            {
                return;
            }
            ConfigBlock config = session.Config;
            config.NotifyCompaction = !config.NotifyCompaction;
            session.UpdateConfig();
            ChatterUtil.SaveSession(source);
            SendMessage($"{(config.NotifyCompaction ? "Will" : "Won't")} notify you when context is compacted", source);
        }
        
        public void Tokenize(string args, string source, string n)
        {
            var textToTokenize = args.Substring(args.Split(' ')[0].Length + 1);
            if (textToTokenize.Length == 0)
            {
                SendMessage("Too short", source);
                return;
            }

            // var instance = GptUtil.GetModelInstance(source);
            IChatSession? session = ChatterUtil.GetChatSession(source); 
            if (session is null)
            {
                SendMessage("Don't have instance", source);
                return;
            }

            textToTokenize = textToTokenize.Replace("\\n", "\n");
            // SendMessage(instance.PollForResponse(instance.RequestTokenize(textToTokenize), true)?.ToString() ?? "null", source);
            SendMessage(string.Join(", ", session.Context.Tokenize(textToTokenize)), source);
        }

        public void Detokenize(string args, string source, string n)
        {
            var textToTokenize = args.Substring(args.Split(' ')[0].Length + 1);
            if (textToTokenize.Length == 0)
            {
                SendMessage("Too short", source);
                return;
            }

            textToTokenize += " ";

            var tokens = new List<int>();
            var tempDigit = new StringBuilder();
            for (int i = 0; i < textToTokenize.Length; i++)
            {
                char c = textToTokenize[i];
                if (char.IsDigit(c))
                {
                    tempDigit.Append(c);
                }
                else
                {
                    tokens.Add(int.Parse(tempDigit.ToString()));
                    tempDigit.Clear();
                }
            }

            IChatSession? session = ChatterUtil.GetChatSession(source);
            if (session is null)
            {
                SendMessage("oops", source);
                return;
            }
            
            SendMessage(session.Context.Detokenize(tokens), source);
        }
    }
}