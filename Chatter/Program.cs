using System.Collections.Concurrent;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HeimdallBase;
using OsirisBase;
using UserLedger;

namespace Chatter
{
    class Program
    {
        static void Main(string[] args)
        {
            new Chatter().Start(args);
        }
    }    
    
    public partial class Chatter : LedgerOsirisModule
    {
        private GptUtil GptUtil { get; set; }
        private MisskeyUtil MisskeyUtil { get; set; }
        
        public void Start(string[] args)
        {
            Name = "chatter";
            GptUtil = new GptUtil(this);
            MisskeyUtil = new MisskeyUtil(GptUtil);
            Directory.CreateDirectory("source_states");

            Commands = new Dictionary<string, MessageHandler>()
            {
                {"$gpt ", HandleGpt},
                {".temp", HandleTemp},
                {".nick", HandleNick},
                {".wipe", HandleWipe},
                {".history", HandleHistory},
                {".timings", PrintTimings},
                {".chat", EnqueueChatWrapper},
                {".complete ", EnqueueChatWrapper},
                {".clean", EnqueueChatWrapper},
                {"", HandleTtsBroken},
                {".tokenize ", Tokenize},
                {".decode ", Detokenize},
                {".post", PostToMisskey},
                {"$benchmark", Benchmark},
                {".usepast", ChatWithPast},
                {".spoof", AddHistory},
                {".ponder", HandlePonder},
                {".fill", FillForm},
                {".gptram", GetRam},
                {".notify", Notify}
            };

            // new Thread(ChatLoop).Start();

            Init(args);
        }

        private Dictionary<int, string> PendingCaptions = new();
        private Dictionary<int, string> Results = new();
        private readonly HttpClient HttpClient = new();

        public void GetRam(string args, string source, string n)
        {
            // nvidia-smi --query-compute-apps=pid,used_memory --format=csv,noheader,nounits
            var nvidiaPsi = new ProcessStartInfo("/usr/bin/nvidia-smi",
                "--query-compute-apps=pid,used_memory --format=csv,noheader,nounits");
            nvidiaPsi.RedirectStandardOutput = true;
            nvidiaPsi.UseShellExecute = false;
            var nvidiaProcess = Process.Start(nvidiaPsi);
            var output = nvidiaProcess.StandardOutput.ReadToEnd();
            var lines = output.Split('\n');

            // var pid = GptUtil.GetModelInstance(source).Process.Id;
            var vramUsage = 0d;
            var targetPid = -1;

            foreach (var line in lines)
            {
                var parts = line.Split(',');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int linePid) ||
                    !int.TryParse(parts[1], out int lineUsage))
                    continue;

                // if (linePid == pid)
                // if (Process.GetProcessById(linePid))
                targetPid = linePid;
                {
                    vramUsage += lineUsage;
                }
            }

            var cpuUsage = Process.GetProcessById(targetPid).WorkingSet64 / (1048576d * 1024d);
            vramUsage /= 1024d;
            
            SendMessage($"{cpuUsage:0.00} GiB system RAM, {vramUsage:0.00} GiB VRAM", source);
        }
        
        public void FillForm(string args, string source, string n)
        {
            args = args.Substring(".fill".Length).Trim();
            var parts = args.Split(' ');
            var formName = parts[0];
            var formPath = $"forms/{formName}.json";
            if (!File.Exists(formPath))
            {
                SendMessage($"Could not find form {formName}.", source);
                return;
            }

            var form = LMForm.FromFile(formPath);
            GptUtil.StartInstanceIfNotStarted(source);
            var instance = GptUtil.GetModelInstance(source);

            args = args.Substring(formName.Length).Trim();
            var inQuote = false;
            var currentRun = new StringBuilder();
            // var propertyName = "";
            var inputs = new Dictionary<string, string>();

            void Consume(string run)
            {
                var runParts = run.Split('=');
                if (runParts.Length <= 1)
                {
                    return;
                }

                var key = runParts[0];
                var value = string.Join('=', runParts.Skip(1));
                inputs[key] = value;
                Console.WriteLine($"Extracted {key} = {value}");
            }
            for (int i = 0; i < args.Length; i++)
            {
                var c = args[i];
                if (inQuote)
                {
                    if (c == '"')
                    {
                        inQuote = false;
                        continue;
                    }
                    else
                    {
                        currentRun.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuote = true;
                        continue;
                    }
                    else if (c == ' ')
                    {
                        Consume(currentRun.ToString());
                        currentRun.Clear();
                    }
                    else
                    {
                        currentRun.Append(c);
                    }
                }
            }

            if (currentRun.Length > 0)
            {
                Consume(currentRun.ToString());
            }

            Console.WriteLine($"Executing form...");
            var formSlot = GptUtil.GetChannelId($"##form/{formName}");
            var results = form.Execute(inputs, instance, formSlot, GptUtil.GetConfig(source));

            if (!results.Outputs.Any())
            {
                SendMessage($"{results.Outputs.Count} outputs", source);
            }
            else
            {
                SendMessage(string.Join('\0',results.Outputs.Select(pair => $"{pair.Key} = {pair.Value.ToString().ReplaceLineEndings("\\n")}")), source);
                if (results.Log.Length > 0)
                {
                    SendMessage(results.Log.ToString().ReplaceLineEndings("\0"), source);
                }
            }
        }
        
        public void AddHistory(string args, string source, string n)
        {
            args = args.Substring(".spoof".Length).Trim();
            var parts = args.Split(' ');
            var targetNick = parts[0];
            var rest = string.Join(' ', parts.Skip(1));
            var channel = GptUtil.GetChannel(source);
            // var instance = GptUtil.GetModelInstance(source);
            var line = new ChatLine() {Message = rest, Nick = targetNick, Source = source, Time = DateTime.UtcNow};
            GptUtil.StartInstanceIfNotStarted(source);
            GptUtil.AnnotateTokens(line);
            channel.CommitLine(line);
            
            SendMessage($"{n}: okay, I'll pretend as if {targetNick} said that.", source);
        }
        
        public void ChatWithPast(string args, string source, string n)
        {
            args = args.Substring(".usepast".Length).Trim();
            var parts = args.Split(' ');
            var id = int.Parse(parts[0]);
            var rest = string.Join(' ', parts.Skip(1));

            GptUtil.StartInstanceIfNotStarted(source);
            var channel = GptUtil.GetChannel(source);
            var instance = GptUtil.GetModelInstance(source);
            var request = GptUtil.CreateGenerationRequest(prompt: $"<{n}> {rest}\n<{channel.Config.AssignedNick}>", stopAtNewline: true, loadIndex: id,
                storeIndex: id);
            var requestId = instance.RequestGeneration(request);
            var result = instance.PollForResponse(requestId, true)?.ToString().Trim();
            
            SendMessage(result, source);
        }

        public void Benchmark(string args, string source, string n)
        {
            var instance = GptUtil.GetModelInstance(source);
            var metrics = new StringBuilder();
            var savedContext = new Dictionary<int, int>();
            var processingTime = new Dictionary<int, double>();
            var lastBiggest = -1;
            
            void BenchmarkThroughput(int contextLength, int batchSize)
            {
                var index = contextLength + 1;
                var waitSeconds = 20;
                var waitIters = 10;
                var tokens = new int[contextLength];
                for (int i = 0; i < tokens.Length; i++)
                {
                    tokens[i] = 1000;
                }

                if (contextLength >= 1)
                    tokens[0] = 1;

                if (contextLength > 0 && !savedContext.ContainsKey(contextLength))
                {
                    var storeRequest = GptUtil.CreateGenerationRequest(tokens: tokens, numTokens: batchSize, decodeOnly: true,
                        // loadIndex: lastBiggest,
                        storeIndex: index, decodeWithEmbeddings: true);
                    var storeId = instance.RequestGeneration(storeRequest);

                    if (index > lastBiggest)
                    {
                        lastBiggest = index;
                    }
                    var storeResult = instance.PollForResponse(storeId, true);
                    savedContext[contextLength] = index;
                    processingTime[contextLength] = storeResult.Timings["decode_done"] -
                                                    storeResult.Timings["start"];
                }
                
                var timer = Stopwatch.StartNew();
                var times = new List<double>();
                var timings = new List<Dictionary<string, double>>();

                while (timer.ElapsedMilliseconds < waitSeconds * 1000 && times.Count < waitIters)
                {
                    var currentTime = timer.ElapsedMilliseconds;
                    var request = GptUtil.CreateGenerationRequest(tokens: tokens, numTokens: batchSize, loadIndex: index, storeIndex: -1);
                    var requestId = instance.RequestGeneration(request);
                    var result = instance.PollForResponse(requestId, true);
                    var endTime = timer.ElapsedMilliseconds;
                    times.Add(endTime - currentTime);
                    timings.Add(result.Timings);
                }

                timer.Stop();
                var totalGenerationTime = timings.Sum(t => t["generation_done"] - t["generation_start"]);
                var totalProcessingTime = timings.Sum(t => t["generation_start"] - t["start"]);

                var iterationsPerSecond = times.Count / timer.Elapsed.TotalSeconds;
                var tokensGeneratedPerSecond = (times.Count * batchSize) / totalGenerationTime;
                var tokensProcessedPerSecond = processingTime.ContainsKey(contextLength) ? (contextLength / processingTime[contextLength]) : 0;
                
                SendMessage($"{contextLength,5:0} | {batchSize,5:0} | {1/iterationsPerSecond,5:0.00} | {tokensGeneratedPerSecond,5:0.00} | {tokensProcessedPerSecond,5:0.00}", source);
            }

            var contextSizes = new int[] { 0, 64, 128, 256, 384, 512, 768, 1024 };
            var batchSizes = new int[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 };
            
            batchSizes = new[] {4, 8, 16, 64, 128, 256};
            
            SendMessage($"ctx   | batch | iters | gen   | proc  ", source);

            for (int j = 0; j < batchSizes.Length; j++)
            {
                for (int i = 0; i < contextSizes.Length; i++)
                {
                    var batch = batchSizes[j];
                    var context = contextSizes[i];
                    
                    BenchmarkThroughput(context, batch);
                }
            }
        }
        
        public void PostToMisskey(string args, string source, string n)
        {
            args = args.Substring("$post".Length).Trim();

            if (int.TryParse(args, out int seedIndex))
            {
                if (Results.ContainsKey(seedIndex))
                {
                    var bytes = HttpClient.GetByteArrayAsync(Results[seedIndex]).Result;
                    var fileUrl = MisskeyUtil.UploadFile(bytes, "diffusion.png", "image/png").Result;
                    
                    SendMessage($"Uploaded to Misskey at {fileUrl ?? "null"}", source);

                    if (fileUrl is null)
                    {
                        SendMessage($"Not continuing because upload failed.", source);
                        return;
                    }

                    var postId = MisskeyUtil.PostAsync(PendingCaptions[seedIndex], fileUrl).Result;
                    SendMessage($"Post sent: {postId ?? "null"}", source);
                    Results.Remove(seedIndex);
                    PendingCaptions.Remove(seedIndex);
                }
                else
                {
                    SendMessage($"Could not find post {seedIndex}.", source);
                }

                return;
            }
            var requestId = GptUtil.GetPromptResponseForSource(source, asNick: "vance", complete: ".hypno",
                ignoreHistory: true, onlyOneLine: true);
            var completed = GptUtil.GetModelInstance(source).PollForResponse(requestId, true).ToString().Trim();
            // var completed = "i am deranged";
            
            var seed = Random.Shared.Next(100000, 900000);
            PendingCaptions[seed] = completed;
            SendMessage($".hypno seed={seed} \"{completed}\"", source);
        }
        
        int currentChatRequests = 0;

        public void Tokenize(string args, string source, string n)
        {
            var textToTokenize = args.Substring(".tokenize ".Length);
            if (textToTokenize.Length == 0)
            {
                SendMessage("Too short", source);
                return;
            }

            var instance = GptUtil.GetModelInstance(source);
            if (instance is null)
            {
                SendMessage("Don't have instance", source);
                return;
            }

            textToTokenize = textToTokenize.Replace("\\n", "\n");
            SendMessage(instance.PollForResponse(instance.RequestTokenize(textToTokenize), true)?.ToString() ?? "null", source);
        }

        public void Detokenize(string args, string source, string n)
        {
            var textToTokenize = args.Substring(".decode ".Length);
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

            var instance = GptUtil.GetModelInstance(source);
            if (instance is null)
            {
                SendMessage("Don't have instance", source);
                return;
            }
            SendMessage(instance.PollForResponse(instance.RequestDetokenize(tokens), true)?.ToString() ?? "null", source);
        }
        
        public void HandleTemp(string args, string source, string n)
        {
            args = args.Substring("$temp".Length).Trim();
            var config = GptUtil.GetConfig(source);
            if (args.Length > 0)
            {
                var argParts = args.Split(' ');

                bool success = true;
                if (double.TryParse(argParts[0], out double temp))
                {
                    config.Temperature = temp;
                    if (argParts.Length >= 2)
                    {
                        if (double.TryParse(argParts[1], out double repetitionPenalty))
                        {
                            config.RepetitionPenalty = repetitionPenalty;
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
            }
            
            SendMessage($"temperature = {config.Temperature}, repetition_penalty = {config.RepetitionPenalty}", source);
        }
        
        public void HandleTtsBroken(string args, string source, string n)
        {
            if (n == "diffuser")
            {
                var regex =
                    $"{GptUtil.OwnNick}: your diffusion for prompt .*? with seed (?<seed>[0-9]+): (?<url>[a-zA-Z:/\\.\\d]+)";
                var match = Regex.Match(args, regex);

                if (match.Success)
                {
                    Console.WriteLine("match: " + match);
                    Console.WriteLine("seed: " + match.Groups["seed"].Value);
                    Console.WriteLine("url: " + match.Groups["url"].Value);
                    
                    var seed = int.Parse(match.Groups["seed"].Value);
                    var url = match.Groups["url"].Value;
                    
                    
                    if (PendingCaptions.ContainsKey(seed))
                    {
                        DiffusionFulfilled(seed, url, source);
                    }
                }
            }

            try
            {
                GptUtil.RecordLine(args, source, n);
            }
            catch (ApplicationException e)
            {
                Console.WriteLine(e);
                SendMessage($"{n}: {e.Message}", source);
                // throw;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private void DiffusionFulfilled(int seed, string url, string source)
        {
            Results[seed] = url;
            SendMessage($"To post prompt \"{PendingCaptions[seed]}\" with file \"{url}\", say $post {seed}.", source);
        }

        private Dictionary<string, List<(char, string)>> CachedUsers = new();


        public ConcurrentQueue<(string, string, string)> ChatQueue = new();

        // public void ChatLoop()
        // {
        //     while (true)
        //     {
        //         (string, string, string) result;
        //         while (!ChatQueue.TryDequeue(out result))
        //         {
        //             Thread.Sleep(50);
        //         }
        //         
        //         Console.WriteLine($"Dequeued {result.Item1}, {result.Item2}, {result.Item3}");
        //         ChatOneShotPrivate(result.Item1, result.Item2, result.Item3);
        //     }
        // }

        public void EnqueueChatWrapper(string args, string source, string n)
        {
            var queueResult = EnqueueChatOneShot(args, source, n);
            if (queueResult is not null)
            {
                SendMessage($"{n}: {queueResult}", source);
            }
        }
        

        public class UsageRecord
        {
            public string Nick { get; set; }
            public string Source { get; set; }
            public DateTime TimeReceived { get; set; }
            public DateTime TimeFulfilled { get; set; }
        }

        public List<UsageRecord> ChatUsages = new();
        public Dictionary<string, DateTime> LastNotified = new();

        public string? EnqueueChatOneShot(string args, string source, string n)
        {
            Console.WriteLine($"Called with {args}, {source}, {n}");
            bool involuntary = args.Contains('\n') && (!bool.TryParse(args.Split('\n')[0], out bool a) || !a);
            // int totalQueueLength = ChatQueue.Count;
            // int thisNickQueueLength = ChatQueue.Count(e => string.Equals(e.Item3, n, StringComparison.InvariantCultureIgnoreCase));

            var thisUsage = new UsageRecord()
            {
                Nick = n,
                Source = source,
                TimeReceived = DateTime.UtcNow
            };
        
            // Console.WriteLine($"Queue lengths: {thisNickQueueLength}, {totalQueueLength}");
            //
            // if (totalQueueLength > Config.GetInt("chatter.antispam.global"))
            // {
            //     Console.WriteLine($"Total queue length {totalQueueLength} exceeds limit");
            //     return "Too many requests in global queue.";
            // }
            //
            // if (thisNickQueueLength > Config.GetInt("chatter.antispam.user"))
            // {
            //     Console.WriteLine($"User queue length {thisNickQueueLength} exceeds limit");
            //     return "Too many requests in queue from you specifically.";
            // }

            if (!involuntary)
            {
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
                    thisUsage.TimeFulfilled = DateTime.UtcNow;
                    ChatUsages.Add(thisUsage);

                    if (!LastNotified.ContainsKey(n) || (DateTime.UtcNow - LastNotified[n]).TotalSeconds > 5)
                    {
                        LastNotified[n] = DateTime.UtcNow;
                        return "You are spamming me a bit too much. Give me some room to breathe between requests.";
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            // ChatQueue.Enqueue((args, source, n));
            ChatOneShotPrivate(args, source, n);
            thisUsage.TimeFulfilled = DateTime.UtcNow;
            ChatUsages.Add(thisUsage);

            while (ChatUsages.Count > 150)
            {
                ChatUsages.RemoveAt(0);
            }
            
            return null;
        }

        public void ChatOneShotPrivate(string args, string source, string n)
        {
            bool useLock = true;
            try
            {
                const string ponder = "##ponder";
                bool recursedCall = args == ponder;
                if (recursedCall)
                {
                    args = ".chat";
                    useLock = false;
                }

                if (useLock)
                {
                    lock (GptUtil)
                    {
                        currentChatRequests++;

                        if (currentChatRequests > 3)
                        {
                            return;
                        }
                    }
                }

                GptUtil.StartInstanceIfNotStarted(source);
                var channel = GptUtil.GetChannel(source);
                var instance = GptUtil.GetModelInstance(source);

                ChatLine? tempLine = null;
                bool noHistory = false;
                bool anyCommand = false;
                bool speakManyLines = false;
                bool generateOneLine = false;
                bool recurseAtEnd = false;
                var config = GptUtil.GetConfig(source);

                if (args.StartsWith(".clean"))
                {
                    args = "." + args.Substring(".clean".Length);
                    noHistory = true;
                    anyCommand = true;
                }

                var chatLong = false;
                if (args.StartsWith(".chatlong"))
                {
                    chatLong = true;
                    anyCommand = true;
                    args = ".chat" + args.Substring(".chatlong".Length);
                }

                string? asNick = null;
                string? complete = null;

                if (args.StartsWith(".chatas") && args.Length > ".chatas".Length + 1)
                {
                    anyCommand = true;
                    generateOneLine = true;
                    args = args.Substring(".chatas".Length).Trim();
                    var parts = args.Split(' ');
                    asNick = parts[0];
                    args = $".chat {string.Join(' ', parts.Skip(1))}";
                }

                if (args.StartsWith(".chat") && args.Length > ".chat".Length + 1)
                {
                    generateOneLine = true;
                    anyCommand = true;
                    var tempLineAdd = args.Substring(".chat".Length).Trim();
                    tempLine = new ChatLine()
                    {
                        Message = tempLineAdd,
                        Nick = n,
                        Source = source,
                        Time = DateTime.UtcNow
                    };
                }

                bool useComplete = false;
                if (args.StartsWith(".complete"))
                {
                    generateOneLine = true;
                    anyCommand = true;
                    args = args.Substring(".complete".Length).Trim();
                    var parts = args.Split(' ');
                    if (parts.Length > 1)
                    {
                        useComplete = true;
                        asNick = parts[0];
                        complete = string.Join(' ', parts.Skip(1));
                    }
                }

                bool isRandomChance = false;
                
                if (!anyCommand)
                {
                    asNick = config.AssignedNick;
                    speakManyLines = true;
                    generateOneLine = true;

                    bool fromHighlight = false;

                    if (args.Contains('\n'))
                    {
                        var parts = args.Split('\n');
                        args = parts[1];
                        fromHighlight = bool.Parse(parts[0]);
                        isRandomChance = !fromHighlight;
                    }
                    
                    var rateLimit = Config.GetInt($"chatter.ratelimit.{source}");
                    if (fromHighlight)
                    {
                        channel.MarkResponse();
                    }
                    if (fromHighlight && rateLimit > 0)
                    {
                        var passedCheck = channel.CheckRate(rateLimit);
                        if (!passedCheck)
                        {
                            if (channel.LastNotified == DateTime.MinValue || (DateTime.UtcNow - channel.LastNotified).TotalHours > 3)
                            {
                                SendMessage($"{n}: I am sorry, we cannot continue chatting here due to the channel rate limit. You can find me in #gpt, #gpt2, #gpt3, and #gpt4. This notice won't repeat for a while.", source);
                                channel.LastNotified = DateTime.UtcNow;
                            }
                            return;
                        }
                    }
                }

                bool saveDecode = !useComplete;
                if (chatLong)
                {
                    generateOneLine = false;
                    saveDecode = false;
                }

                var requestId = GptUtil.GetPromptResponseForSource(source, tempAddLine: tempLine is null ? null : new[] { tempLine }, chatLong, asNick, complete, noHistory, generateOneLine, writeState: saveDecode);
                
                if (channel.ContextHasCompacted)
                {
                    channel.ContextHasCompacted = false;
                    if (config.NotifyCompaction)
                    {
                        SendMessage(channel.ContextCompactionMessage, source);
                    }
                }
                
                var requestResult = GptUtil.GetModelInstance(source).PollForResponse(requestId, true);
                var results = requestResult.Result.Split('\n');
                int additionalPossibleLines = 1;

                Console.WriteLine($"{results.Length} results");
                CachedUsers[source] = new();

                string CleanLine(string result)
                {
                    var resultWriter = new StringBuilder();
                    var susRanges = new List<(int, int)>();

                    foreach ((_, var nick) in CachedUsers[source])
                    {
                        if (nick == "Mog")
                            continue;
                        
                        var index = result.IndexOf(nick, StringComparison.InvariantCultureIgnoreCase);
                        
                        if (index != -1)
                            Console.WriteLine($"Index {index} for \"{nick}\" over \"{result}\"");

                        while (index != -1)
                        {
                            susRanges.Add((index, index + nick.Length));
                            index = result.IndexOf(nick, index + 1, StringComparison.InvariantCultureIgnoreCase);
                            Console.WriteLine($"Next index {index} for \"{nick}\" over \"{result}\"");
                        }
                    }
                    
                    int colorCooldown = 0;
                    for (int i = 0; i < result.Length; i++)
                    {
                        char c = result[i];
                        resultWriter.Append(c);

                        if (c == '')
                        {
                            colorCooldown = 4;
                        }
                        else if (colorCooldown > 0)
                        {
                            colorCooldown--;

                            if (c != ',' && !char.IsDigit(c))
                            {
                                colorCooldown = 0;
                            }
                        }

                        if (colorCooldown == 0)
                        {
                            for (int j = 0; j < susRanges.Count; j++)
                            {
                                (var start, var end) = susRanges[j];

                                if (start <= i && i < end)
                                {
                                    resultWriter.Append("\x02\x02⁣");
                                    if (i == end - 1)
                                        susRanges.RemoveAt(j);
                                    
                                    break;
                                }
                            }
                        }
                    }
                    //var resultParts = result.Split(' ');

                    var resultStr = resultWriter.ToString();

                    if (resultStr.Length > 3072)
                    {
                        resultStr = resultStr.Substring(0, 3072);
                        channel.TokenCacheInvalid = true;
                    }

                    return resultStr;
                }

                if (chatLong)
                {
                    SendMessage(string.Join('\0', results.Select(CleanLine)), source);
                }
                else
                {
                    var linesToSpeak = new List<string>();
                    bool preferredFound = false;
                    if (results.Any() && results[0].StartsWith(' '))
                    {
                        results[0] = results[0].Substring(1);
                    }
                    if (useComplete)
                    {
                        var completedChatLine = new ChatLine()
                            {Nick = asNick, Message = complete + results[0], Time = DateTime.UtcNow, Source = source};
                        results[0] = completedChatLine.ToString();
                        // results[0] = $"{ChatLine.LeftDelimiter}{asNick}{ChatLine.RightDelimiter} {complete}{results[0]}";
                        preferredFound = true;
                    }
                    else
                    {
                        if (asNick is not null)
                        {
                            var completedChatLine = new ChatLine()
                                {Nick = asNick, Message = results[0], Time = DateTime.UtcNow, Source = source};
                            results[0] = completedChatLine.ToString();
                        }
                    }
                    
                    preferredFound = true;

                    if (speakManyLines)
                    {
                        linesToSpeak.AddRange(results);
                    }
                    else
                    {
                        linesToSpeak.Add(results[0]);
                    }

                    int linesSpoken = 0;
                    
                    foreach (var lineToSpeak in linesToSpeak)
                    {
                        string result = lineToSpeak;
                        try
                        {
                            // result = lineToSpeak.Trim();
                            // if (result.Length == 0)
                            //     continue;
                            //
                            // var resultParts = result.Split(' ');
                            //
                            // var firstBracket = result.IndexOf(ChatLine.LeftDelimiter);
                            // var lastBracket = result.IndexOf(ChatLine.RightDelimiter);
                            //
                            // if (firstBracket == -1 || lastBracket == -1 || lastBracket <= firstBracket)
                            // {
                            //     throw new Exception();
                            // }
                            //
                            // if (result.Length > lastBracket)
                            // {
                            //     if (result[lastBracket + 1] == '.' || result[lastBracket + 1] == '!')
                            //     {
                            //         result = result.Insert(lastBracket + 1, " ");
                            //     }
                            // }
                            //
                            // var lineContents = result.Substring(lastBracket + 1);
                            // var rresult = result.Substring(0, lastBracket + 1) + lineContents;
                            //
                            // var nick = rresult.Substring(firstBracket + 1, (lastBracket - firstBracket) - 1);
                            // if (speakManyLines && nick != asNick)
                            // {
                            //     break;
                            // }
                            // //result = {lineContents}";
                            // result = lineContents.Trim();

                            //GptUtil.RecordLine(lineContents, source, "diffuser");
                            if (ChatLine.TryParse(result, out ChatLine chatLine))
                            {
                                result = chatLine.Message;
                            }
                            else
                            {
                                throw new Exception();
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Broke at line {lineToSpeak}: {e}");
                            break;
                        }

                        var trimmedResult = result.Substring(0, Math.Min(512, result.Length));
                        // var dirtyRegexes = new[]
                        // {
                        //     "Home.*?(News|Views|Events).*?$",
                        //     "Home>[a-zA-Z\\d\\s]+>.*?$"
                        // };
                        var dirtyRegexes = new string[] { };

                        if (File.Exists(Config.GetString("chatter.dirty")))
                        {
                            dirtyRegexes = File.ReadAllLines(Config.GetString("chatter.dirty"));
                        }
                        
                        var resultLine = new ChatLine()
                        {
                            Message = result, Nick = (!anyCommand || asNick is null) ? GptUtil.OwnNick : asNick,
                            Source = source, Time = DateTime.UtcNow
                        };
                        
                        foreach (var dirty in dirtyRegexes)
                        {
                            var match = Regex.Match(result, dirty);
                            if (match.Success)
                            {
                                var unwantedLength = match.Length;
                                var unwantedTokenCount = GptUtil.CountTokens(source, match.Value + "\n");
                                result = result.Substring(0, result.Length - unwantedLength);
                                resultLine.Message = result;
                                // GptUtil.AnnotateTokens(resultLine);
                                // var nextTrim = channel.CurrentContextLength + (resultLine.Tokens - 2);
                                if (requestResult.Timings is not null)
                                {
                                    var timings = requestResult.Timings;
                                    var totalTokenCount = (int)(timings["tokens_processed"] + timings["tokens_generated"] +
                                                          timings["tokens_skipped"]);
                                    var throwAway = unwantedTokenCount + 8;
                                    var newTokenCount = totalTokenCount - throwAway;
                                    Console.WriteLine(
                                        $"{source}: line trimmed to \"{result}\" upon triggering regex {dirty}. will throw away {throwAway} tokens. currently {totalTokenCount} tokens in context before adding in line, will trim to {newTokenCount}");
                                    channel.NextTrim = newTokenCount;
                                }
                                break;
                            }
                        }

                        if (!useComplete)
                        {
                            GptUtil.AnnotateTokens(resultLine);
                            channel.CommitLine(resultLine);
                            channel.MarkDecodeEvent();
                        }

                        var resultStr = useComplete ? result : CleanLine(result);
                        var wetfishLengthLimit = 440;
                        if ((source.StartsWith("Wetfish") || source.StartsWith("local")) && resultStr.Length > wetfishLengthLimit)
                        {
                            var brokenResult = new StringBuilder();
                            for (int i = 0; i < resultStr.Length; i += wetfishLengthLimit)
                            {
                                for (int k = 20; k >= -20; k--)
                                {
                                    if (i + k < resultStr.Length && resultStr[i + k] == ' ')
                                    {
                                        i += k;
                                        break;
                                    }
                                }
                                var remaining = resultStr.Length - i;
                                brokenResult.Append($"{resultStr.Substring(i, Math.Min(wetfishLengthLimit, remaining))}{(remaining >= wetfishLengthLimit ? "\0" : "")}");
                            }

                            resultStr = brokenResult.ToString();
                        }
                        SendMessage(resultStr, source);
                        linesSpoken++;
                        Thread.Sleep(250);
                    }

                    if (linesSpoken == 0)
                    {
                        SendMessage($"* The language model is declining to speak.", source);
                    }

                    bool repeat = false;
                    
                    if (generateOneLine)
                    {
                        if (channel.CanPonder())
                        {
                            if (requestResult.Timings is not null)
                            {
                                var timings = requestResult.Timings;
                                var totalTokens = (int) (timings["tokens_skipped"] + timings["tokens_generated"] +
                                                         timings["tokens_processed"]);
                                
                                channel.MarkPonderance();
                                channel.NextTrim = totalTokens - 7;
                                var ponderPrefix = $"[{DateTime.Now:HH:mm:ss}]";
                                var ponderId = GptUtil.GetPromptResponseForSource(source, asNick: null,
                                    onlyOneLine: true, writeState: false, numTokens: channel.Config.NickTokens + 7, omitNewline: false, append: ponderPrefix);
                                var ponderResult = ponderPrefix + instance.PollForResponse(ponderId, true);
                                Console.WriteLine(
                                    $"{source}: ponder result was \"{ponderResult.ReplaceLineEndings("\\n")}\"");
                                if (ChatLine.TryParse(ponderResult, out ChatLine ponderLine) && ponderLine.Nick == config.AssignedNick)
                                {
                                    Console.WriteLine($"{source}: triggering another chat");
                                    repeat = true;
                                }
                            }
                        }
                    }

                    var lines = new List<string>();
                    if (requestResult.Timings is not null)
                    {
                        var timings = requestResult.Timings;

                        var tokenCount = timings["tokens_skipped"];
                        if (tokenCount > config.TrimStart)
                        {
                            channel.PruneHistoryToTokenCount(config.TrimTarget);
                        }
                        
                        var ordered = timings.OrderBy(p => p.Value).ToList();
                        var last = -1d;
                        foreach (var (key, value) in ordered)
                        {
                            if (value < 16000000)
                            {
                                lines.Add($"# {key} = {value}");
                                continue;
                            }
                            
                            if (last == -1)
                            {
                                last = value;
                                continue;
                            }

                            var diff = value - last;
                            lines.Add($"{key,20}: +{diff,5:0.00}s @ {value:0.00}");
                            last = value;
                        }
                        
                        if (config.PrintTimings)
                            SendMessage(string.Join('\0', lines), source);
                        else
                        {
                            foreach (var line in lines)
                                Console.WriteLine(line);
                        }
                    }

                    if (repeat)
                    {
                        EnqueueChatOneShot(ponder, source, n);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                
                if (args.StartsWith(".chat"))
                    SendMessage(e.ToString(), source);
            }
            finally
            {
                if (useLock)
                {
                    lock (GptUtil)
                    {
                        currentChatRequests--;
                    }
                }
            }
        }

        void HandleGpt(string args, string source, string n)
        {
            args = args.Substring("$gpt".Length).Trim();
            var verb = args.Split(' ')[0];

            switch (verb)
            {
                case "start":
                    GptUtil.StartGpt(source, args.Substring(verb.Length).Trim());
                    break;
                case "stop":
                    GptUtil.StopGpt(source);
                    break;
                default:
                    SendMessage($"Unrecognized verb {verb}", source);
                    break;
            }
        }

        void HandleNick(string args, string source, string n)
        {
            args = args.Substring($"$nick".Length).Trim();
            var config = GptUtil.GetConfig(source);
            var channel = GptUtil.GetChannel(source);
            if (args.Any())
            {
                var tokenCount = GptUtil.CountTokens(source, args);

                if (tokenCount < 10)
                {
                    config.AssignedNick = args;
                    config.NickTokens = tokenCount;
                    channel.TokenCacheInvalid = true;
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
            var channel = GptUtil.GetChannel(source);
            channel.WipeHistory();
            SendMessage($"okay", source);
        }

        void HandlePonder(string args, string source, string n)
        {
            args = args.Substring(".ponder".Length).Trim();
            var channel = GptUtil.GetChannel(source);
            if (int.TryParse(args, out int newPonder) && newPonder >= 0)
            {
                channel.Config.PonderancesPerReply = newPonder;
            }
            SendMessage($"ponderances = {channel.Config.PonderancesPerReply}", source);
        }
        
        public void HandleHistory(string args, string source, string n)
        {
            args = args.Substring(".history".Length).Trim();
            var channel = GptUtil.GetChannel(source);
            var config = GptUtil.GetConfig(source);
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

                if (nextStart < 50 || nextTarget < 50 || nextStart > 1000 || nextTarget > 500 ||
                    nextStart <= nextTarget || nextStart - nextTarget < 50)
                {
                    success = false;
                }

                if (success)
                {
                    config.TrimStart = nextStart;
                    config.TrimTarget = nextTarget;
                }
                
                SendMessage(success ? "success" : "oops", source);
            }
            
            SendMessage($"trim_start = {config.TrimStart}, trim_target = {config.TrimTarget}, current context length = {channel.CurrentContextLength}", source);
        }

        void PrintTimings(string args, string source, string n)
        {
            args = args.Substring(".timings".Length).Trim();
            var config = GptUtil.GetConfig(source);
            config.PrintTimings = !config.PrintTimings;
            SendMessage($"Now {(config.PrintTimings ? "printing" : "not printing")} timing info", source);
        }
        void Notify(string args, string source, string n)
        {
            args = args.Substring(".notify".Length).Trim();
            var config = GptUtil.GetConfig(source);
            config.NotifyCompaction = !config.NotifyCompaction;
            SendMessage($"{(config.NotifyCompaction ? "Will" : "Won't")} notify you when context is compacted", source);
        }
    }
}