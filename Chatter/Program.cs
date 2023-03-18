using System.Diagnostics;
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

            Commands = new Dictionary<string, MessageHandler>()
            {
                {"$gpt ", HandleGpt},
                {".temp", HandleTemp},
                {".nick", HandleNick},
                {".wipe", HandleWipe},
                {".histlen", HandleHistoryLength},
                {".timings", PrintTimings},
                {".chat", ChatOneShot},
                {".complete ", ChatOneShot},
                {".clean", ChatOneShot},
                {"", HandleTtsBroken},
                {".tokenize ", Tokenize},
                {".decode ", Detokenize},
                {".post", PostToMisskey},
                {"$benchmark", Benchmark},
                {".usepast", ChatWithPast},
            };

            Init(args);
        }

        private Dictionary<int, string> PendingCaptions = new();
        private Dictionary<int, string> Results = new();
        private readonly HttpClient HttpClient = new();

        public void ChatWithPast(string args, string source, string n)
        {
            args = args.Substring(".usepast".Length).Trim();
            var parts = args.Split(' ');
            var id = int.Parse(parts[0]);
            var rest = string.Join(' ', parts.Skip(1));

            GptUtil.StartInstanceIfNotStarted(source);
            var channel = GptUtil.GetChannel(source);
            var instance = GptUtil.GetModelInstance(source);
            var request = instance.RequestOutputForPrompt(prompt: $"<{n}> {rest}\n<{channel.Config.AssignedNick}>", stopAtNewline: true, loadIndex: id,
                storeIndex: id);
            var result = instance.PollForResponse(request, true).Trim();
            
            SendMessage(result, source);
        }

        public void Benchmark(string args, string source, string n)
        {
            var instance = GptUtil.GetModelInstance(source);
            var metrics = new StringBuilder();
            
            void BenchmarkThroughput(int contextLength, int batchSize)
            {
                var waitSeconds = 20;
                var waitIters = 10;
                var tokens = new int[contextLength];
                for (int i = 0; i < tokens.Length; i++)
                {
                    tokens[i] = Random.Shared.Next(2000, 40000);
                }
                
                var timer = Stopwatch.StartNew();
                var times = new List<double>();

                while (timer.ElapsedMilliseconds < waitSeconds * 1000 && times.Count < waitIters)
                {
                    var currentTime = timer.ElapsedMilliseconds;
                    var requestId = instance.RequestOutputForPrompt(tokens: tokens, numTokens: batchSize);
                    var result = instance.PollForResponse(requestId, true);
                    var endTime = timer.ElapsedMilliseconds;
                    times.Add(endTime - currentTime);
                }

                timer.Stop();

                var iterationsPerSecond = times.Count / timer.Elapsed.TotalSeconds;
                var tokensGeneratedPerSecond = (times.Count * batchSize) / timer.Elapsed.TotalSeconds;
                var tokensProcessedPerSecond = (times.Count * batchSize + contextLength) / timer.Elapsed.TotalSeconds;
                
                SendMessage($"{contextLength,5:0} | {batchSize,5:0} | {iterationsPerSecond,5:0.00} | {tokensGeneratedPerSecond,5:0.00} | {tokensProcessedPerSecond,5:0.00}", source);
            }

            var contextSizes = new int[] { 0, 64, 256, 512, 768, 1024 };
            var batchSizes = new int[] { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024 };
            
            batchSizes = new[] {1, 4, 8, 16, 64, 128, 512};
            
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
            var completed = GptUtil.GetModelInstance(source).PollForResponse(requestId, true).Trim();
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
            SendMessage(instance.PollForResponse(instance.RequestTokenize(textToTokenize), true) ?? "null", source);
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
            SendMessage(instance.PollForResponse(instance.RequestDetokenize(tokens), true) ?? "null", source);
        }
        
        public void HandleTemp(string args, string source, string n)
        {
            args = args.Substring("$temp".Length).Trim();
            var config = GptUtil.GetConfig(source);
            if (args.Length > 0)
            {
                var argParts = args.Split(' ');

                bool success = true;
                success = success && double.TryParse(argParts[0], out config.Temperature);
                if (argParts.Length > 2)
                {
                    success = success && double.TryParse(argParts[1], out config.RepetitionPenalty);
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
            
            GptUtil.RecordLine(args, source, n);
        }

        private void DiffusionFulfilled(int seed, string url, string source)
        {
            Results[seed] = url;
            SendMessage($"To post prompt \"{PendingCaptions[seed]}\" with file \"{url}\", say $post {seed}.", source);
        }

        private Dictionary<string, List<(char, string)>> CachedUsers = new();

        
        public void ChatOneShot(string args, string source, string n)
        {
            try
            {
                lock (GptUtil)
                {
                    currentChatRequests++;

                    if (currentChatRequests > 3)
                    {
                        return;
                    }
                }

                var channel = GptUtil.GetChannel(source);

                ChatLine? tempLine = null;
                bool noHistory = false;
                bool anyCommand = false;
                bool speakManyLines = false;
                bool generateOneLine = false;
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
                
                if (!anyCommand)
                {
                    asNick = config.AssignedNick;
                    speakManyLines = true;
                    generateOneLine = true;
                }

                if (chatLong)
                {
                    generateOneLine = false;
                }

                var requestId = GptUtil.GetPromptResponseForSource(source, tempAddLine: tempLine is null ? null : new[] { tempLine }, chatLong, asNick, complete, noHistory, generateOneLine);
                var results = GptUtil.GetModelInstance(source).PollForResponse(requestId, true).Split('\n');
                int additionalPossibleLines = 1;

                while (!anyCommand && additionalPossibleLines-- > 0)
                {
                    // var tempLinesFromResults = results.Select(result =>
                    // {
                    //     
                    // });
                }

                Console.WriteLine($"{results.Length} results");

                CachedUsers[source] = new();
                // if (!CachedUsers.ContainsKey(source) || Random.Shared.NextDouble() < 0.1)
                // {
                //     // CachedUsers[source] = GetUsers(source).Where(u => u.Item2.Length > 1).ToList();
                // }

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
                        resultStr = resultStr.Substring(0, 3072);
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
                    if (useComplete)
                    {
                        results[0] = $"<{asNick}> {complete}{results[0]}";
                        preferredFound = true;
                    }
                    else
                    {
                        if (asNick is not null)
                        {
                            results[0] = $"<{asNick}> {results[0]}";
                        }
                    }
                    
                    // var result = results[0];
                    preferredFound = true;
                    // for (int i = 0; i < results.Length && !preferredFound; i++)
                    // {
                    //     result = results[i];
                    //
                    //     if (result.Contains("coinman") || result.Contains("╠╗"))
                    //         continue;
                    //
                    //     var wordCount = result.Split(' ',
                    //         StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length;
                    //
                    //     if (asNick is not null && !result.StartsWith($"<{asNick}>"))
                    //         continue;
                    //     
                    //     if (wordCount > 6)
                    //     {
                    //         preferredFound = true;
                    //         break;
                    //     }
                    // }

                    if (speakManyLines)
                    {
                        linesToSpeak.AddRange(results);
                    }
                    else
                    {
                        linesToSpeak.Add(results[0]);
                    }

                    // if (channel.CandidateLines.Any())
                    // {
                    //     channel.CandidateLines.Clear();
                    // }

                    foreach (var lineToSpeak in linesToSpeak)
                    {
                        string result = lineToSpeak;
                        try
                        {
                            result = lineToSpeak.Trim();
                            if (result.Length == 0)
                                continue;

                            var resultParts = result.Split(' ');
                            if (resultParts[0].StartsWith('[') && resultParts[0].EndsWith(']'))
                            {
                                result = string.Join(' ', resultParts.Skip(1));
                            }

                            var firstBracket = result.IndexOf('<');
                            var lastBracket = result.IndexOf('>');

                            if (firstBracket == -1 || lastBracket == -1 || lastBracket <= firstBracket)
                            {
                                throw new Exception();
                            }

                            if (result.Length > lastBracket)
                            {
                                if (result[lastBracket + 1] == '.' || result[lastBracket + 1] == '!')
                                {
                                    result = result.Insert(lastBracket + 1, " ");
                                }
                            }

                            var lineContents = result.Substring(lastBracket + 1);
                            var rresult = result.Substring(0, lastBracket + 1) + lineContents;

                            var nick = rresult.Substring(firstBracket + 1, (lastBracket - firstBracket) - 1);
                            if (speakManyLines && nick != asNick)
                            {
                                break;
                            }
                            //result = {lineContents}";
                            result = lineContents.Trim();

                            //GptUtil.RecordLine(lineContents, source, "diffuser");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Broke at line {lineToSpeak}: {e}");
                            break;
                        }

                        var trimmedResult = result.Substring(0, Math.Min(512, result.Length));
                        var resultLine = new ChatLine()
                        {
                            Message = trimmedResult, Nick = (!anyCommand || asNick is null) ? GptUtil.OwnNick : asNick,
                            Source = source, Time = DateTime.UtcNow
                        };

                        if (!useComplete)
                        {
                            // channel.CandidateLines.Add(resultLine);
                            channel.Lines.Add(resultLine);
                        }

                        var resultStr = useComplete ? result : CleanLine(result);
                        SendMessage(resultStr, source);
                        Thread.Sleep(250);
                    }

                    if (config.PrintTimings)
                    {
                        var instance = GptUtil.GetModelInstance(source);
                        if (instance.Timings.ContainsKey(requestId))
                        {
                            var ordered = instance.Timings[requestId].OrderBy(p => p.Value).ToList();
                            var last = -1d;
                            var lines = new List<string>();
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
                            
                            SendMessage(string.Join('\0', lines), source);
                        }
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
                lock (GptUtil)
                {
                    currentChatRequests--;
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
            if (args.Any())
            {
                config.AssignedNick = args;
            }
            
            SendMessage($"Speaking as {config.AssignedNick}", source);
        }

        void HandleWipe(string args, string source, string n)
        {
            var channel = GptUtil.GetChannel(source);
            lock (channel.Lines)
            {
                if (channel.Lines.Any())
                {
                    channel.Lines.Clear();
                    // channel.CandidateLines.Clear();
                    SendMessage($"okay", source);
                }
                else
                {
                    SendMessage($"Could not find lines for this source", source);
                }
            }
        }

        void HandleHistoryLength(string args, string source, string n)
        {
            args = args.Substring("$histlen".Length).Trim();
            var config = GptUtil.GetConfig(source);
            if (int.TryParse(args, out int newHistorySize))
            {
                if (newHistorySize < 0 || newHistorySize > 1000)
                {
                    SendMessage("Out of range", source);
                    return;
                }

                config.HistorySize = newHistorySize;
            }
            SendMessage($"Now keeping {config.HistorySize} lines", source);
        }

        void PrintTimings(string args, string source, string n)
        {
            args = args.Substring(".timings".Length).Trim();
            var config = GptUtil.GetConfig(source);
            config.PrintTimings = !config.PrintTimings;
            SendMessage($"Now {(config.PrintTimings ? "printing" : "not printing")} timing info", source);
        }
    }
}