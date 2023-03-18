using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeimdallBase;
using Newtonsoft.Json;

namespace Chatter;

public class IrcInstance
{
    public string CurrentNick { get; set; }
    public bool Dead;
    public DateTime LastSent = DateTime.UtcNow;
    
    private string MutatedNick;
    
    private readonly TcpClient Socket;
    private readonly NetworkStream NetworkStream;
    private readonly SslStream SslStream;
    private readonly StreamWriter Writer;
    private readonly StreamReader Reader;

    private readonly string Channel; 
    
    public IrcInstance(string host, int port, string channel, string nick)
    {
        Socket = new TcpClient(host, port);
        NetworkStream = Socket.GetStream();
        SslStream = new SslStream(NetworkStream);
        SslStream.AuthenticateAsClient(host);

        Writer = new StreamWriter(SslStream) { AutoFlush = true};
        Reader = new StreamReader(SslStream);

        CurrentNick = nick;
        Channel = channel;
        CreateNickMutation();
        Init();
    }

    private void CreateNickMutation()
    {
        MutatedNick = CurrentNick.Replace(' ', ' ');
        MutatedNick = MutatedNick.Replace('!', 'ǃ');
        MutatedNick = MutatedNick.Replace(':', 'ː');
        for (int i = 0; i < MutatedNick.Length; i++)
        {
            if (Random.Shared.NextDouble() > 0.5)
                MutatedNick = MutatedNick.Insert(i, "⁣");
        }

        MutatedNick = new string('⁣', Random.Shared.Next(1, 5)) + MutatedNick +
                      new string('⁣', Random.Shared.Next(1, 5));
    }

    public void Init()
    {
        Writer.WriteLine($"USER fake fake fake fake");
        Writer.WriteLine($"NICK {MutatedNick}");
        Writer.WriteLine($"JOIN {Channel}");
    }

    public void SetNick(string newNick)
    {
        CurrentNick = newNick;
        CreateNickMutation();
        Writer.WriteLine($"NICK {MutatedNick}");
    }

    public void DispatchMessage(string line)
    {
        LastSent = DateTime.UtcNow;
        try
        {
            Writer.WriteLine($"PRIVMSG {Channel} :{line}");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            Dead = true;
        }
    }

    public void ProcessOutstanding()
    {
        if (!Socket.Connected)
        {
            Dead = true;
            return;
        }
        Writer.WriteLine("PONG");
        
        while (NetworkStream.DataAvailable)
        {
            var nextLine = Reader.ReadLine();
            if (nextLine is null)
            {
                Dead = true;
                return;
            }

            var spaceIndex = nextLine.IndexOf(' ');
            if (spaceIndex > -1)
            {
                var firstWordSkipped = nextLine.Substring(spaceIndex + 1);
                if (firstWordSkipped.StartsWith("433"))
                {
                    SetNick(CurrentNick);
                    Writer.WriteLine($"JOIN {Channel}");
                }
            }
        }
    }
}

public class IrcInstanceManager
{
    private readonly string Host;
    private readonly string Channel;
    private readonly int Port;

    private readonly Dictionary<string, IrcInstance> Instances = new();

    private int MaximumInstances = 30;
    private int TimeoutSeconds = 300;

    public IrcInstanceManager(string host, int port, string channel)
    {
        Host = host;
        Port = port;
        Channel = channel;
    }

    public void DispatchLine(string nick, string line)
    {
        var nickLower = nick.ToLowerInvariant();
        
        if (!Instances.ContainsKey(nickLower))
        {
            if (Instances.Count < MaximumInstances)
            {
                var newInstance = new IrcInstance(Host, Port, Channel, nick);
                Instances[nickLower] = newInstance;
            }
            else
            {
                var oldestInstance = Instances.OrderByDescending(i => DateTime.UtcNow - i.Value.LastSent).First().Value;
                Instances.Remove(oldestInstance.CurrentNick.ToLowerInvariant());
                oldestInstance.SetNick(nick);
                Instances[nickLower] = oldestInstance;
            }
        }

        Instances[nickLower].DispatchMessage(line);
    }

    public void ProcessAllInstances()
    {
        var removeKeys = new List<string>();
        foreach ((var key, var instance) in Instances)
        {
            try
            {
                instance.ProcessOutstanding();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Instance {instance.CurrentNick} threw exception {e}");
                instance.Dead = true;
                removeKeys.Add(key);
            }
        }
        
        removeKeys.ForEach(k => Instances.Remove(k));
    }
}

public class GptInstance
{
    public static int NewlineToken = 13; // 50118; // 198
    
    public string Model { get; set; }
    public readonly Process Process;

    public bool ModelDead = false;

    private readonly StreamReader Reader;
    private readonly StreamWriter Writer;

    private Dictionary<int, string> Responses = new();
    public Dictionary<int, Dictionary<string, double>> Timings = new();

    private List<string> LastResponses = new();

    public GptInstance(Process process)
    {
        if (!Directory.Exists($"/tmp/gpt"))
        {
            Directory.CreateDirectory("/tmp/gpt");
        }
        Process = process ?? throw new ArgumentNullException(nameof(process));

        Reader = Process.StandardOutput;
        Writer = Process.StandardInput;
    }

    public int RequestTokenize(string input)
    {
        lock (Process)
        {
            var requestId = Random.Shared.Next();
            var requestObj = new
            {
                type = "tokenize",
                id = requestId,
                text = input
            };
            
            Writer.WriteLine(JsonConvert.SerializeObject(requestObj));
            Console.WriteLine($"Requested tokenize with id {requestId}");
            return requestId;
        }
    }
    
    public int RequestDetokenize(IEnumerable<int> input)
    {
        lock (Process)
        {
            var requestId = Random.Shared.Next();
            var requestObj = new
            {
                type = "detokenize",
                id = requestId,
                tokens = input.ToList()
            };
            
            Writer.WriteLine(JsonConvert.SerializeObject(requestObj));
            Console.WriteLine($"Requested detokenize with id {requestId}");
            return requestId;
        }
    }

    public int RequestNextOverlappingOutput(double temperature = 1, double repetitionPenalty = 1)
    {
        lock (Process)
        {
            var requestId = Random.Shared.Next();
            var requestObj = new
            {
                type = "generate_overlapping",
                id = requestId,
                temperature = temperature,
                repetition_penalty = repetitionPenalty,
            };
            
            Writer.WriteLine(JsonConvert.SerializeObject(requestObj));
            Console.WriteLine($"Requested overlapping with id {requestId}");
            // var response = JsonDocument.Parse(Reader.ReadLine());
            // var result = response.RootElement.GetProperty("result").GetString();
            // return result;
            return requestId;
        }
    }

    void MarkDead(string message = "(none)")
    {
        Console.WriteLine($"Marking model dead: {message}");
        ModelDead = true;
        try
        {
            Process.Kill(true);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    public int RequestOutputForPrompt(string? prompt = null, int[]? tokens = null, int numTokens = 64, bool stopAtNewline = false, double temperature = 1, double repetitionPenalty = 1, int loadIndex = -1, int storeIndex = -1)
    {
        lock (Process)
        {
            if (Process.HasExited)
            {
                MarkDead("Process exited");
                return -1;
            }

            var requestId = Random.Shared.Next();
            var requestObj = new
            {
                type = "generate_once",
                prompt = prompt ?? "",
                tokens = tokens ?? new int[0],
                temperature = temperature,
                repetition_penalty = repetitionPenalty,
                force_funny = true,
                num_tokens = numTokens,
                id = requestId,
                eos_token = stopAtNewline ? NewlineToken : -1,
                use_past = loadIndex,
                save_past = storeIndex
            };

            Writer.WriteLine(JsonConvert.SerializeObject(requestObj));
            Console.WriteLine($"Requested once with id {requestId}");
            return requestId;
        }
    }

    // public string GetOutputForPrompt(string prompt, int numTokens = 64, bool stopAtNewline = false, double temperature = 1, double repetitionPenalty = 1)
    // {
    //     var requestId = RequestOutputForPrompt(prompt: prompt, numTokens: numTokens, stopAtNewline: stopAtNewline, temperature: temperature, repetitionPenalty: repetitionPenalty);
    //     return PollForResponse(requestId, true) ?? throw new Exception();
    // }

    void ConsumeNextResponse()
    {
        lock (Process)
        {
            // var pingId = Random.Shared.Next();
            // var pingRequest = JsonConvert.SerializeObject(new {type = "ping", id = pingId});
            // Writer.WriteLine(pingRequest);

            string? respText = null;
            JsonDocument? response = null;
            int requestId = -1;
            JsonElement? resultElement = null;

            void Consume()
            {
                respText = Reader.ReadLine();
                response = JsonDocument.Parse(respText);
                requestId = response.RootElement.GetProperty("id").GetInt32();
                resultElement = response.RootElement.GetProperty("result");
                Console.WriteLine($"  Read request {requestId}");
            }

            Consume();
            //
            // while (resultElement?.ValueKind == JsonValueKind.String && resultElement?.GetString() == "pong" && requestId != pingId)
            // {
            //     Console.WriteLine($"Consumed pong {requestId}");
            //     Consume();
            // }
            //
            // if (requestId == pingId)
            // {
            //     Console.WriteLine($"Consumed own pong {requestId}");
            //     return;
            // }

            var result = resultElement?.GetProperty("response_decoded").GetString() ?? throw new Exception();
            Console.WriteLine(respText);
            Console.WriteLine($"Raw output for request {requestId}:");
            Console.WriteLine(result);
            result = result.Replace("</s>", "");
            Console.WriteLine($"Substituted: {result}");

            if (resultElement?.TryGetProperty("timings", out JsonElement timingsElement) ?? false)
            {
                Timings[requestId] = timingsElement.Deserialize<Dictionary<string, double>>();
            }

            if (result.Length > 100)
            {
                if (LastResponses.Count > 15)
                {
                    LastResponses.RemoveAt(0);
                }

                bool converged = result.Trim().Length == 0;
                converged = converged || LastResponses.Any(resp =>
                    resp.Equals(result, StringComparison.InvariantCultureIgnoreCase));

                if (converged)
                {
                }
                
                LastResponses.Add(result);
            }

            lock (Responses)
            {
                Responses[requestId] = result;
            }

            if (File.Exists(GetTempPath(requestId)))
            {
                File.Delete(GetTempPath(requestId));
            }
        }
    }

    string GetTempPath(int requestId) => $"/tmp/gpt/{requestId}";

    public string? PollForResponse(int requestId, bool wait)
    {
        if (File.Exists(GetTempPath(requestId)))
        {
            ConsumeNextResponse();
        }

        lock (Responses)
        {
            if (Responses.ContainsKey(requestId))
            {
                var ret = Responses[requestId];
                Responses.Remove(requestId);
                return ret;
            }
        }

        if (!wait)
            return null;

        var waitStart = DateTime.UtcNow;
        while (true)
        {
            if (File.Exists(GetTempPath(requestId)))
            {
                ConsumeNextResponse();
            }
            lock (Responses)
            {
                if (Responses.ContainsKey(requestId))
                {
                    var ret = Responses[requestId];
                    Responses.Remove(requestId);
                    return ret;
                }
            }
            Thread.Sleep(100);

            if ((DateTime.UtcNow - waitStart).TotalSeconds > 200)
            {
                Console.WriteLine($"Model has died");
                ModelDead = true;
                return "";
            }
        }
    }
}

public class ChatLine
{
    public DateTime Time { get; set; }
    public string Nick { get; set; }
    public string Source { get; set; }
    public string Message { get; set; }
}

public class ConfigBlock
{
    public int HistorySize = 7;
    public double Temperature = /*0.9175*/ 0.35; // 0.99
    public double RepetitionPenalty = 1.2; // 1.075
    public string AssignedNick = "SmartGenius";
    public string ModelName;
    public bool PrintTimings;
}

public class ChannelState
{
    public List<ChatLine> Lines = new();
    // public List<ChatLine> CandidateLines = new();
    public ConfigBlock Config = new();
    public long LastDecodedTokenHeight;
    public long CurrentTokenHeight;
    public bool TokenCacheInvalid = true;
}

public class GptUtil
{
    private readonly Chatter Chatter;
    
    public GptUtil(Chatter chatter)
    {
        // Diffuser = diffuser ?? throw new ArgumentNullException(nameof(diffuser));
        Chatter = chatter ?? throw new ArgumentNullException(nameof(chatter));
    }

    private Dictionary<string, GptInstance> GptProcesses = new();
    //
    // public Dictionary<string, List<ChatLine>> Lines = new();
    //
    // public Dictionary<string, List<ChatLine>> CandidateLines = new();
    // public Dictionary<string, ConfigBlock> Configs = new();
    // public Dictionary<string, long> LastDecodedTokenHeight = new();

    // public string AssignedNick = "gpter";
    public string OwnNick = "gpter";

    public Dictionary<string, ChannelState> Channels = new();

    public ChannelState GetChannel(string source)
    {
        if (!Channels.ContainsKey(source))
            Channels[source] = new();
        return Channels[source];
    }
    public ConfigBlock GetConfig(string source) => GetChannel(source).Config;

    public GptInstance? GetModelInstance(string source)
    {
        lock (Channels)
        {
            var modelName = GetModel(source);
            if (string.IsNullOrWhiteSpace(modelName))
                return null;

            return GptProcesses[modelName];
        }
    }
    
    public void RecordLine(string args, string source, string nick)
    {
        if (args.StartsWith("!!") && args.Length > 2 && args[2] != '!')
        {
            return;
        }
        
        if (nick == "diffuser")
        {
            return;
        }
        // if (args.StartsWith(".chat") || args.StartsWith(".diffuse") || args.StartsWith(".complete") || args.StartsWith(".clean"))
        //     return;
        foreach ((var commandKey, _) in Chatter.Commands)
        {
            if (commandKey.StartsWith('.') && args.StartsWith(commandKey))
                return;
        }

        if (string.IsNullOrWhiteSpace(GetModel(source)))
            return;
        
        // if (!Lines.ContainsKey(source))
        //     Lines[source] = new List<ChatLine>();
        var channel = GetChannel(source);

        // if (channel.CandidateLines.Any())
        // {
        //     foreach (var candidateLine in channel.CandidateLines)
        //     {
        //         var age = DateTime.UtcNow - candidateLine.Time;
        //
        //         if (age.TotalSeconds < 20)
        //         {
        //             Console.WriteLine($"Adding {age.TotalSeconds}s old line \"{candidateLine.Message}\"");
        //             channel.Lines.Add(candidateLine);
        //         }
        //         else
        //         {
        //             Console.WriteLine($"Not adding {age.TotalSeconds}s old line \"{candidateLine.Message}\"");
        //         }
        //     }
        //
        //     channel.CandidateLines.Clear();
        // }

        var line = new ChatLine() { Message = args, Source = source, Nick = nick, Time = DateTime.UtcNow};
        channel.Lines.Add(line);
        
        while (channel.Lines.Count > 150)
            channel.Lines.RemoveAt(0);

        if (Config.Contains("chatter.spontaneous_channels", source))
        {
            var chatChance = Config.GetDouble($"chatter.chance.{source}");
            if (chatChance == 0)
                chatChance = 0.02d;

            bool canReply = args.Contains(OwnNick) && !Config.GetValue<bool>($"chatter.noreply.{source}");

            if (Random.Shared.NextDouble() < chatChance || canReply)
            {
                Chatter.ChatOneShot(args, source, nick);
            }
        }
    }

    public int GetPromptResponseForSource(string source, IEnumerable<ChatLine>? tempAddLine = null, bool forceExtra = false, string? asNick = null, string? complete = null, bool ignoreHistory = false, bool onlyOneLine = false, int numTokens = -1)
    {
        var model = GetModel(source);
        if (string.IsNullOrWhiteSpace(model))
            throw new Exception("No model found for source");
        
        StartInstanceIfNotStarted(source);
        var instance = GptProcesses[model];

        var lines = new List<ChatLine>();
        var config = GetConfig(source);
        var channel = GetChannel(source);

        if (channel.Lines.Any())
        {
            // var realLines = channel.Lines;
            // for (int i = 0; i < config.HistorySize; i++)
            // {
            //     var histIndex = (realLines.Count - config.HistorySize) + i;
            //     if (histIndex < 0)
            //         continue;
            //     
            //     lines.Add(realLines[histIndex]);
            // }
            lines.AddRange(channel.Lines);
        }

        if (tempAddLine is not null)
        {
            // if (lines.Any())
            //     lines.RemoveAt(0);
            // lines.Add(tempAddLine);
            lines.AddRange(tempAddLine);
        }
        
        var linesComposed = (ignoreHistory ? "" : string.Join('\n', lines.Select(l => $"<{(l.Nick == OwnNick ? config.AssignedNick : l.Nick)}> {l.Message.Replace(OwnNick, config.AssignedNick, StringComparison.InvariantCultureIgnoreCase)}"))) + "\n";

        if (asNick is not null)
        {
            linesComposed += $"<{asNick}>";

            if (complete is not null)
            {
                linesComposed += $" {complete}";
            }
        }

        var longPrefixes = new[] { "!tldr", ".wiki", ".ud" };

        var extra = forceExtra ||
                    (lines.Any() && longPrefixes.Any(p => lines.LastOrDefault()?.Message?.StartsWith(p) ?? false));

        Console.WriteLine($"Prompt key is {Config.GetString($"gpt.prompt.{source}")}");
        Console.WriteLine($"Prompt key^2 is {Config.GetString(Config.GetString($"gpt.prompt.{source}"))}");
        var promptFile = Config.GetString(Config.GetString($"gpt.prompt.{source}"));
        if (File.Exists(promptFile))
        {
            linesComposed = File.ReadAllText(promptFile).Replace("$NICK", asNick) + linesComposed;
        }

        if (numTokens <= 0)
        {
            numTokens = extra ? 150 : onlyOneLine ? 128 : 64;
        }

        var channelBytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var channelId = (int)BitConverter.ToUInt16(channelBytes);

        var storeId = channelId;
        var loadId = channelId;
        if (channel.TokenCacheInvalid)
        {
            loadId = -1;
        }

        if (ignoreHistory)
        {
            loadId = storeId = -1;
        }
        
        var requestId = instance.RequestOutputForPrompt(linesComposed, numTokens: numTokens, stopAtNewline: onlyOneLine,
            temperature: config.Temperature, repetitionPenalty: config.RepetitionPenalty, loadIndex: loadId, storeIndex: storeId);
        
        return requestId;
    }

    public void StartInstanceIfNotStarted(string source)
    {
        lock (GptProcesses)
        {
            // args = args ?? "0.95 1.05";
            var model = GetModel(source);

            if (string.IsNullOrWhiteSpace(model))
                throw new Exception("No model found for source");

            GetChannel(source).Config.ModelName = model;
            if (GptProcesses.ContainsKey(model))
            {
                var process = GptProcesses[model];

                if (process.Process.HasExited || process.ModelDead)
                {
                    Console.WriteLine($"Exited: {process.Process.HasExited}, dead: {process.ModelDead}");
                    try
                    {
                        process.Process.Kill();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                    GptProcesses.Remove(model);
                }
                else
                {
                    return;
                }
            }

            var binary = Config.GetString("gpt.binary");

            if (!File.Exists(binary))
                throw new Exception();

            bool inRealTime = false;

            Dictionary<string, string> annoyingNickMappings = new()
            {
                {"meleeman", "very_good_programmer"},
                {"meleeman|m", "very_good_programmer"},
                {"hatInTheCat", "very_good_programmer"},
                {"hatInTheCat_", "very_good_programmer"}
            };

            Dictionary<string, string> replacements = new()
            {
            };

            foreach (var kv in annoyingNickMappings)
                replacements[kv.Key] = kv.Value;

            var psi = new ProcessStartInfo(binary, $"{model}");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardInput = true;

            if (GptProcesses.Count == 0)
            {
                psi.Environment["CUDA_VISIBLE_DEVICES"] = "0";
            }
            else
            {
                psi.Environment["CUDA_VISIBLE_DEVICES"] = "1";
            }
            //psi.

            if (GptProcesses.ContainsKey(model) && !GptProcesses[model].Process.HasExited)
            {
                GptProcesses[model].Process.Kill(true);
            }

            try
            {
                var GptProcess = Process.Start(psi) ?? throw new Exception();
                var instance = new GptInstance(GptProcess);

                var line = GptProcess.StandardOutput.ReadLine();

                while (line != "ready")
                {
                    Console.WriteLine(line);
                    line = GptProcess.StandardOutput.ReadLine();
                }

                Console.WriteLine($"Model marked ready");

                GptProcesses[model] = instance;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }

    public void StartGpt(string source, string args)
    {
        void RealFunc()
        {
            var model = Config.GetString($"gpt.target.{source}");

            if (string.IsNullOrWhiteSpace(model))
            {
                Chatter.SendMessage($"No model configured for {source}.", source);
                return;
            }
            
            try
            {
                bool inRealTime = true;
                Dictionary<string, string> annoyingNickMappings = new()
                {
                    {"meleeman", "very_good_programmer"},
                    {"meleeman|m", "very_good_programmer"},
                    {"hatInTheCat", "very_good_programmer"},
                    {"hatInTheCat_", "very_good_programmer"}
                };
                Dictionary<string, string> replacements = new()
                {
                };

                foreach (var kv in annoyingNickMappings)
                    replacements[kv.Key] = kv.Value;
                                
                string CleanLine(string result)
                {
                    var resultWriter = new StringBuilder();
                    var susRanges = new List<(int, int)>();

                    foreach ((var nick, _) in replacements)
                    {
                        if (nick == "Mog")
                            continue;
                        
                        var index = result.IndexOf(nick, StringComparison.InvariantCultureIgnoreCase);

                        // if (index != -1)
                        // {
                        //     
                        // }
                            // Console.WriteLine($"Index {index} for \"{nick}\" over \"{result}\"");

                        while (index != -1)
                        {
                            Console.WriteLine($"Next index {index} for \"{nick}\" over \"{result}\"");
                            susRanges.Add((index, index + nick.Length));
                            index = result.IndexOf(nick, index + 1, StringComparison.InvariantCultureIgnoreCase);
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
                string ProcessLineWithNickColors(string line)
                {
                    try
                    {
                        line = line.Trim();

                        var firstBracket = line.IndexOf('<');
                        var lastBracket = line.IndexOf('>');

                        if (firstBracket == -1 || lastBracket == -1 || lastBracket <= firstBracket)
                        {
                            return line;
                        }
                        
                        if (line.Length > lastBracket)
                        {
                            if (line[lastBracket + 1] == '.' || line[lastBracket + 1] == '!')
                            {
                                line = line.Insert(lastBracket + 1, " ");
                            }
                        }

                        var lineContents = line.Substring(lastBracket + 1);
                        // foreach (var kv in replacements)
                        // {
                        //     lineContents = lineContents.Replace(kv.Key, kv.Value);
                        // }
                        
                        
                        // var lineContentWords = lineContents.Split(' ');
                        //
                        // for (int i = 0; i < lineContentWords.Length; i++)
                        // {
                        //     var wordMap = lineContentWords[i].ToLowerInvariant();
                        //     if (replacements.ContainsKey(wordMap))
                        //     {
                        //         lineContentWords[i] = replacements[wordMap];
                        //     }
                        // }
                        //
                        // lineContents = string.Join(' ', lineContentWords);
                        line = line.Substring(0, lastBracket + 1) + lineContents;

                        var nick = line.Substring(firstBracket + 1, (lastBracket - firstBracket) - 1);

                        if (annoyingNickMappings.ContainsKey(nick))
                            nick = annoyingNickMappings[nick];
                        
                        var nickColor = (Math.Abs(nick.GetHashCode()) % 10) + 3;
                        int seed = 2;
                        while (nickColor == 8)
                        {
                            nickColor = (Math.Abs(nick.GetHashCode() / seed++) % 10) + 3;
                        }

                        // var escapedNick = string.Join('​', nick.ToCharArray());
                        
                        if (nick.Length > 1)
                            replacements[nick.ToLowerInvariant()] = nick;

                        line = line.Remove(firstBracket + 1, (lastBracket - firstBracket) - 1);
                        line = line.Insert(firstBracket + 1, $"\x03{nickColor.ToString().PadLeft(2, '0')}{nick}\x03");

                        return CleanLine(line);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        return line;
                    }
                }

                //var outReader = GptProcess.StandardOutput;
                //outReader.BaseStream.
                
                StartInstanceIfNotStarted(source);
                GptInstance instance = GptProcesses[GetModel(source)];

                int lastTime = 0;
                int cumulativeTimeCounter = 0;
                int lastCumulativeTime = 0;

                IrcInstanceManager? ircInstanceManager = null;
                string? connectionHost = null;
                
                if ((connectionHost = Config.GetString($"gpt.connect.server.{source}")) is not null)
                {
                    var connectionPort = 6697;
                    var channel = Config.GetString($"gpt.connect.channel.{source}");

                    ircInstanceManager = new IrcInstanceManager(connectionHost, connectionPort, channel);
                }
                
                if (inRealTime)
                {
                    List<string> bufferedLines = new();
                    string lastLinePartial = "";
                    var eogMark = "<|END-OF-GENERATION|>";
                    var lastLineTimeStr = "";
                    bool lastLineInstant = false;

                    int currentRequestId = -1;

                    void MakeRequest()
                    {
                        if (instance.ModelDead || instance.Process.HasExited)
                        {
                            Console.WriteLine($"Exited: {instance.Process.HasExited}, dead: {instance.ModelDead}");
                            StartInstanceIfNotStarted(source);
                            instance = GptProcesses[GetModel(source)];
                            currentRequestId = -1;
                        }
                        
                        if (currentRequestId == -1)
                        {
                            Console.WriteLine($"Requesting more, {bufferedLines.Count} lines in buffer");
                            currentRequestId = instance.RequestNextOverlappingOutput();
                        }
                    }
                    
                    void ConsumeLine()
                    {
                        if (currentRequestId == -1)
                            return;

                        //var line = outReader.ReadLine() ?? throw new Exception("Read null line");
                        var result = instance.PollForResponse(currentRequestId, false);
                        if (result is null)
                        {
                            return;
                        }

                        currentRequestId = -1;
                        var lines = result.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            var line = lines[i];
                            if (!string.IsNullOrWhiteSpace(lastLinePartial))
                            {
                                line = lastLinePartial + line;
                                lastLinePartial = "";

                                if (line.Contains('\n'))
                                {
                                    var newlinePos = line.IndexOf('\n');
                                    lastLinePartial = line.Substring(newlinePos + 1);
                                    line = line.Substring(0, newlinePos);
                                }
                            }

                            if (i == lines.Length - 1)
                            {
                                lastLinePartial += line;
                                //GptProcess.StandardInput.Write('a');
                                return;
                            }

                            int maxLine = 2048;

                            if (line.Length > maxLine)
                            {
                                lastLinePartial += line.Substring(maxLine);
                                line = line.Substring(0, maxLine);
                            }

                            bufferedLines.Add(line);
                            Console.WriteLine($"Added line: {line}");
                        }
                    }

                    var instantNicks = new[]
                    {
                        "topkek_cloud",
                        "topkek_next",
                        "diffuser",
                        "asuka",
                        "coinman",
                        "╠╗",
                        "Mog",
                        "cat",
                        "ćòĩń",
                        "gasman",
                        "streamlover",
                        "streamlover69"
                    };

                    while (true)
                    {
                        ConsumeLine();
                        if ((lastLineInstant && bufferedLines.Count > 10) || bufferedLines.Count > 15)
                        {
                            var nextReadyLine = bufferedLines[0];
                            bufferedLines.RemoveAt(0);
                            
                            try
                            {                   
                                // var lineTimeStr = nextReadyLine.Split(' ')[0].Trim('[', ']');
                                //     var lineTime = lineTimeStr.Split(':');
                                // var lineTimeSeconds = int.Parse(lineTime[0]) * 3600 + int.Parse(lineTime[1]) * 60 +
                                //                       int.Parse(lineTime[2]);
                                // nextReadyLine = nextReadyLine.Substring(lineTimeStr.Length + 2).Trim();
                                //
                                // if (lineTimeSeconds < lastTime)
                                // {
                                //     cumulativeTimeCounter += 24 * 60 * 60;
                                // }
                                // lastTime = lineTimeSeconds;
                                // lineTimeSeconds += cumulativeTimeCounter;
                                // int waitTime = lastCumulativeTime == 0 ? 0 : (lineTimeSeconds - lastCumulativeTime);
                                // waitTime = Math.Min(10, waitTime);
                                // waitTime = Math.Max(0, waitTime);
                                // Console.WriteLine($"Waiting {waitTime}s from {lastCumulativeTime} ({lastLineTimeStr}) to {lineTimeSeconds} ({lineTimeStr}) for line {nextReadyLine}...");
                                // lastCumulativeTime = lineTimeSeconds;
                                // lastLineTimeStr = lineTimeStr;
                                nextReadyLine = nextReadyLine.TrimStart();
                                string? lineNick = null;
                                string? lineContents = null;

                                if (nextReadyLine.StartsWith('<'))
                                {
                                    var endingBracket = nextReadyLine.IndexOf('>');
                                    lineNick = nextReadyLine.Substring(1, endingBracket - 1);
                                    lineContents = nextReadyLine.Substring(endingBracket + 1);
                                    
                                    if (lineContents.StartsWith(' '))
                                        lineContents = CleanLine(lineContents.Substring(1));

                                    replacements[lineNick.ToLowerInvariant()] = lineNick;
                                }

                                if (lineNick is null)
                                {
                                    continue;
                                }
                                
                                int waitTime = Random.Shared.Next(2, 40);
                                if (instantNicks.Any(nick =>
                                        lineNick.Equals(nick, StringComparison.InvariantCulture) ||
                                        lineNick.Equals($"{nick}_", StringComparison.InvariantCulture)))
                                {
                                    waitTime = 0;
                                }

                                nextReadyLine = ProcessLineWithNickColors(nextReadyLine);
                                lastLineInstant = waitTime == 0;
                                if (lastLineInstant)
                                {
                                    Thread.Sleep(20);
                                }

                                // if (waitTime > 5)
                                // {
                                //     var sw = Stopwatch.StartNew();
                                //     ConsumeLine();
                                //     waitTime -= (int)(sw.ElapsedMilliseconds / 1000);
                                //
                                //     if (waitTime < 0)
                                //     {
                                //         waitTime = 0;
                                //         if (sw.ElapsedMilliseconds < 0)
                                //             Thread.Sleep(20);
                                //     }
                                //
                                // }
                                
                                for (int i = 0; i < waitTime; i++)
                                {
                                    Thread.Sleep(250);
                                    if (instance.Process.HasExited)
                                    {
                                        Chatter.SendMessage("Process exited", source);
                                        MakeRequest();
                                        break;
                                    }
                                }

                                if (ircInstanceManager is not null)
                                {
                                    ircInstanceManager.DispatchLine(lineNick, lineContents);
                                    ircInstanceManager.ProcessAllInstances();
                                }
                                else
                                {
                                    Chatter.SendMessage(nextReadyLine, source);   
                                }
                                ConsumeLine();
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                            }
                        }

                        if (bufferedLines.Count < 20 || (bufferedLines.Count < 200 && bufferedLines.Count % 2 == 0))
                        {
                            MakeRequest();
                        }
                    }
                }
                else
                {
                    var leftover = "";
                    while (!instance.Process.HasExited)
                    {
                        var lines = instance.PollForResponse(instance.RequestNextOverlappingOutput(), true).Split('\n');

                        foreach (var l in lines)
                        {
                            var line = leftover + l;
                            leftover = "";

                            int maxLine = 512;

                            if (line.Length > maxLine)
                            {
                                leftover += line.Substring(maxLine);
                                line = line.Substring(0, maxLine);
                            }

                            var lineParts = line.Split(' ');

                            if (lineParts.Length > 1 && lineParts[0].StartsWith('[') && lineParts[0].EndsWith(']'))
                            {
                                var latterLine = string.Join(' ', lineParts.Skip(1));
                                latterLine = ProcessLineWithNickColors(latterLine);
                                line = $"{lineParts[0]} {latterLine}";
                            }

                            //GptProcess.StandardInput.Write('a');

                            //Console.WriteLine(line);
                            Chatter.SendMessage(line, source);
                            Thread.Sleep(300);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Chatter.SendMessage(e.ToString(), source);
                //throw;
            }
        }
        
        new System.Threading.Thread((ThreadStart)RealFunc).Start();
    }

    public string? GetModel(string source)
    {
        var model = Config.GetString($"gpt.target.{source}");
        return model;
    }

    public void StopGpt(string source)
    {
        try
        {
            var model = GetModel(source);
            if (GptProcesses.ContainsKey(model))
                GptProcesses[model].Process.Kill(true);

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}