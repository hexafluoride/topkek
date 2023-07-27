using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HeimdallBase;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

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

public record GenerationResult
{
    public GenerationResult(GenerationRequest? request, string? result, Dictionary<string, double>? timings, double[][]? probabilities)
    {
        Request = request;
        Result = result;
        Timings = timings;
        Probabilities = probabilities;
    }

    public readonly GenerationRequest? Request;
    public readonly string? Result;
    public readonly Dictionary<string, double>? Timings;
    public readonly double[][]? Probabilities;

    public override string ToString()
    {
        return Result ?? "(null)";
    }
}

public interface IGptInstance
{
    bool ModelDead { get; }
    int[] Tokenize(string input);
    int RequestTokenize(string input);
    int RequestDetokenize(IEnumerable<int> input);
    int RequestGeneration(GenerationRequest request);
    GenerationResult? PollForResponse(int requestId, bool wait);
    void Kill();
}

public class NetworkedGptInstance : IGptInstance
{
    
    public class Message
    {
        public JsonElement? Body { get; set; }
        public int Id { get; set; }
        public string Method { get; set; }
    }
    
    public bool ModelDead { get; private set; }
    
    private TcpClient Connection { get; set; }
    private StreamReader Reader { get; set; }
    private StreamWriter Writer { get; set; }

    public Dictionary<int, GenerationRequest> Requests = new();

    public static JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };
    
    public NetworkedGptInstance(string host, int port)
    {
        Connection = new(host, port);
        var stream = Connection.GetStream();
        Reader = new StreamReader(stream);
        Writer = new StreamWriter(stream) {AutoFlush = true};
    }

    public Message? ConsumeMessage()
    {
        if (!Connection.Connected)
        {
            Kill();
        }

        if (ModelDead)
        {
            return null;
        }
        
        var line = Reader.ReadLine();
        if (line is null)
        {
            return null;
        }
        return JsonSerializer.Deserialize<Message>(line, JsonSerializerOptions);
    }

    public void SendMessage(Message message)
    {
        if (!Connection.Connected)
        {
            Kill();
        }

        if (ModelDead)
        {
            return;
        }
        
        var serialized = JsonSerializer.Serialize(message, JsonSerializerOptions);
        Writer.WriteLine(serialized);
    }

    public void SubmitRequest(JsonElement body, int id)
    {
        var request = new Message()
        {
            Method = "submit",
            Id = id,
            Body = body
        };
        
        SendMessage(request);
        var response = ConsumeMessage() ?? throw new Exception("Failed to consume reply to request submission");
        if (response.Id != id)
        {
            throw new Exception($"Expected reply to {id}, got {response.Id}");
        }

        if (response.Method != "ok")
        {
            throw new Exception($"Expected \"ok\", got {response.Method}");
        }
    }
    
    public int[] Tokenize(string input)
    {
        throw new NotImplementedException();
    }

    public int RequestTokenize(string input)
    {
        lock (Connection)
        {
            var id = Random.Shared.Next();
            var request = JsonSerializer.SerializeToElement(new
            {
                type = "tokenize",
                id,
                text = input
            });

            SubmitRequest(request, id);
            return id;
        }
    }

    public int RequestDetokenize(IEnumerable<int> input)
    {
        var id = Random.Shared.Next();
        var request = JsonSerializer.SerializeToElement(new
        {
            type = "detokenize",
            id,
            tokens = input.ToList()
        });
        
        SubmitRequest(request, id);
        return id;
    }

    public int RequestGeneration(GenerationRequest request)
    {
        var id = Random.Shared.Next();
        request.id = id;
        var requestEncoded = JsonSerializer.SerializeToElement(request);
        
        Console.WriteLine($"Requested once with id {id}");
        lock (Requests)
        {
            Requests[id] = request;
        }
        SubmitRequest(requestEncoded, id);
        return id;
    }

    public GenerationResult? PollForResponse(int requestId, bool wait)
    {
        lock (Connection)
        {
            var pollRequest = new Message()
            {
                Method = "read",
                Id = requestId
            };

            // SendMessage(pollRequest);
            Message? nextResponse = null;

            do
            {
                if (nextResponse is not null)
                {
                    Thread.Sleep(100);
                }

                SendMessage(pollRequest);
                nextResponse = ConsumeMessage() ?? throw new Exception($"Could not read reply to poll");

                if (nextResponse.Id != requestId)
                {
                    throw new Exception($"Expected reply to {requestId}, got {nextResponse.Id}");
                }
            } while (wait && nextResponse.Method == "no" && !ModelDead);

            if (nextResponse.Method == "no")
            {
                return null;
            }
            else if (nextResponse.Method != "result")
            {
                throw new Exception($"Expected \"result\" or \"no\", got {nextResponse.Method}");
            }

            var resultElement = nextResponse.Body.Value.GetProperty("result");
            string? resultText = null;

            if (resultElement.TryGetProperty("response_decoded", out JsonElement responseDecodedElement))
            {
                resultText = responseDecodedElement.GetString();

                Console.WriteLine($"Raw output for request {requestId}:");
                Console.WriteLine(resultText);
                resultText = resultText.Replace("</s>", "");
                Console.WriteLine($"Substituted: {resultText}");
            }

            Dictionary<string, double> timings = new();
            double[][]? probabilities = null;

            if (resultElement.TryGetProperty("timings", out JsonElement timingsElement))
            {
                timings = timingsElement.Deserialize<Dictionary<string, double>>() ?? timings;
            }

            if (resultElement.TryGetProperty("probs", out JsonElement probsElement))
            {
                probabilities = probsElement.EnumerateArray()
                    .Select(sub => sub.EnumerateArray().Select(d => d.GetDouble()).ToArray()).ToArray();
            }

            lock (Requests)
            {
                var request = Requests.ContainsKey(requestId) ? Requests[requestId] : null;
                var resultObject = new GenerationResult(request, resultText, timings, probabilities);
                return resultObject;
            }
        }
    }

    public void Kill()
    {
        Connection.Close();
        ModelDead = true;
    }
}

public class GptInstance : IGptInstance
{
    
    public string Model { get; set; }
    public readonly Process Process;

    public bool ModelDead { get; private set; }

    private readonly StreamReader Reader;
    private readonly StreamWriter Writer;

    // private Dictionary<int, string> Responses = new();
    // public Dictionary<int, Dictionary<string, double>> Timings = new();

    public Dictionary<int, GenerationRequest> Requests = new();
    public Dictionary<int, GenerationResult> Results = new();

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

    public int[] Tokenize(string input)
    {
        return PollForResponse(RequestTokenize(input), true).ToString().Trim('[', ']').Split(',')
            .Select(p => int.Parse(p.Trim())).ToArray();
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

    public void Kill()
    {
        MarkDead();
    }

    public int RequestGeneration(GenerationRequest request)
    {
        lock (Process)
        {
            var requestId = Random.Shared.Next();
            request.id = requestId;
            Writer.WriteLine(JsonConvert.SerializeObject(request));
            Console.WriteLine($"Requested once with id {requestId}");
            Requests[requestId] = request;
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

            Dictionary<string, double> timings = new();
            double[][]? probabilities = null;

            if (resultElement?.TryGetProperty("timings", out JsonElement timingsElement) ?? false)
            {
                timings = timingsElement.Deserialize<Dictionary<string, double>>() ?? timings;
            }

            if (resultElement?.TryGetProperty("probs", out JsonElement probsElement) ?? false)
            {
                probabilities = probsElement.EnumerateArray()
                    .Select(sub => sub.EnumerateArray().Select(d => d.GetDouble()).ToArray()).ToArray();
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

            lock (Results)
            {
                var request = Requests.ContainsKey(requestId) ? Requests[requestId] : null;
                Results[requestId] = new GenerationResult(request, result, timings, probabilities);
                Requests.Remove(requestId);
            }

            if (File.Exists(GetTempPath(requestId)))
            {
                File.Delete(GetTempPath(requestId));
            }
        }
    }

    string GetTempPath(int requestId) => $"/tmp/gpt/{requestId}";

    public GenerationResult? PollForResponse(int requestId, bool wait)
    {
        if (File.Exists(GetTempPath(requestId)))
        {
            ConsumeNextResponse();
        }

        lock (Results)
        {
            if (Results.ContainsKey(requestId))
            {
                var ret = Results[requestId];
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
            lock (Results)
            {
                if (Results.ContainsKey(requestId))
                {
                    var ret = Results[requestId];
                    // Results.Remove(requestId);
                    return ret;
                }
            }
            Thread.Sleep(100);

            if (Process.HasExited || (DateTime.UtcNow - waitStart).TotalSeconds > 120)
            {
                Console.WriteLine($"Model has died");
                ModelDead = true;
                return null;
            }
        }
    }
}

public class GenerationRequest
{
    public string type { get; set; }
    public string prompt { get; set; }
    public int[] tokens { get; set; }
    public double temperature { get; set; }
    public double repetition_penalty { get; set; }
    public bool force_funny { get; set; }
    public int num_tokens { get; set; }
    public int id { get; set; }
    public int eos_token { get; set; }
    public int use_past { get; set; }
    public int save_past { get; set; }
    public bool decode_only { get; set; }
    public bool interruptible { get; set; }
    public int trim_past { get; set; }
    public string decode_method { get; set; }
    public int[][] choices { get; set; } = new int[0][];
}

public class ChatLine
{
    public const char LeftDelimiter = '<';
    public const char RightDelimiter = '>';
    public DateTime Time { get; set; }
    public string Nick { get; set; }
    public string Source { get; set; }
    public string Message { get; set; } = "";
    public int Tokens { get; set; }

    public string ToStringWithChannel(ChannelState channel)
    {
        var oldMessage = Message;
        var oldNick = Nick;
        Message = Message.Replace(channel.OwnNick, channel.Config.AssignedNick);
        if (Nick == channel.OwnNick)
        {
            Nick = channel.Config.AssignedNick;
        }
        
        var ret = this.ToString();s
        Nick = oldNick;
        Message = oldMessage;
        return ret;
    }
    public override string ToString()
    {
        // return $"[{Time.ToLocalTime():HH:mm:ss}] {LeftDelimiter}{Nick}{RightDelimiter}{(Message.Length == 0 ? Message : (" " + Message))}";
        return $"{LeftDelimiter}{Nick}{RightDelimiter}{(Message.Length == 0 ? Message : (" " + Message))}";
    }

    public static bool TryParse(string str, out ChatLine line)
    {
        line = new();
        try
        {
            var result = str.Trim();
            if (result.Length == 0)
                return false;

            var resultParts = result.Split(' ');
            var firstPart = resultParts[0];

            if (firstPart.First() != LeftDelimiter || firstPart.Last() != RightDelimiter)
            {
                // Cannot resolve time
                return false;
            }

            line.Time = DateTime.Now.Date;
            bool timeTouched = false;
            if (false && firstPart.Length == 10)
            {
                var timeParts = firstPart.Trim('[', ']').Split(':');
                if (timeParts.Length == 3)
                {
                    timeTouched = true;
                    if (int.TryParse(timeParts[0], out int hours) && 0 <= hours && hours <= 23)
                    {
                        line.Time = line.Time.AddHours(hours);
                    }

                    if (int.TryParse(timeParts[1], out int minutes) && 0 <= minutes && minutes <= 59)
                    {
                        line.Time = line.Time.AddMinutes(minutes);
                    }

                    if (int.TryParse(timeParts[2], out int seconds) && 0 <= seconds && seconds <= 59)
                    {
                        line.Time = line.Time.AddSeconds(seconds);
                    }
                }
            }

            if (timeTouched)
            {
                line.Time = line.Time.ToUniversalTime();
            }
            else
            {
                line.Time = DateTime.UtcNow;
            }

            if (resultParts.Length >= 1)
            {
                var secondPart = resultParts[0];
                if (secondPart.First() == LeftDelimiter && secondPart.Last() == RightDelimiter)
                {
                    line.Nick = secondPart.Substring(1, secondPart.Length - 2);
                    line.Message = string.Join(' ', resultParts.Skip(1));
                }
                else
                {
                    line.Message = string.Join(' ', resultParts.Skip(0));
                }
            }
            else
            {
                line.Message = string.Join(' ', resultParts.Skip(0));
            }

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"ChatLine.Parse threw on \"{str}\": {e}");
            return false;
        }
        finally
        {
            try
            {
                Console.WriteLine($"ChatLine.Parse returned {line} for \"{str}\"");
            }
            catch (Exception e)
            {
                Console.WriteLine($"ChatLine.Parse threw on \"{str}\": {e}");
            }
        }
    }
}

public class ConfigBlock
{
    // public int HistorySize = 7;
    public int TrimStart { get; set; } = 900;
    public int TrimTarget { get; set; }  = 300;
    public double Temperature { get; set; } = /*0.9175*/ 0.32; // 0.99
    public double RepetitionPenalty { get; set; } = 1.2; // 1.075
    public string AssignedNick { get; set; } = "SmartGenius";
    public int NickTokens { get; set; } = 4;
    public string ModelName { get; set; }
    public bool PrintTimings { get; set; }
    public bool NotifyCompaction { get; set; }
    public int PonderancesPerReply { get; set; } = 0;
}

public class ChannelState
{
    public string Name { get; set; }
    public string? OwnNick { get; set; }

    public List<ChatLine> Lines { get; set; } = new();
    // public List<ChatLine> CandidateLines = new();
    public ConfigBlock Config { get; set; } = new();
    public long LastDecodedTokenHeight { get; set; }
    public long CurrentTokenHeight { get; set; }
    public long TokenBurden => CurrentTokenHeight - LastDecodedTokenHeight;
    public bool TokenCacheInvalid { get; set; } = true;
    private int _nextTrim = -1;
    public int NextTrim
    {
        get => _nextTrim;
        set => _nextTrim = Math.Min(value, _nextTrim < 0 ? value : _nextTrim);
    }
    public long CurrentContextLength { get; set; }
    public DateTime LastReply { get; set; } = DateTime.UtcNow;
    public int RepliesThisHour { get; set; }
    // public bool NotifiedThisHour = false;
    public DateTime LastNotified { get; set; }= DateTime.MinValue;
    public DateTime LastResponded { get; set; } = DateTime.MinValue;
    public bool ContextHasCompacted { get; set; }
    public string ContextCompactionMessage { get; set; } = "";
    public int RemainingPonderances = 0;
    public bool KeepDecoding => (DateTime.UtcNow - LastResponded).TotalSeconds < 900 && !ContextHasCompacted;
    // public bool KeepDecoding;

    public ChannelState()
    {
    }

    public ChannelState(string source)
    {
        Name = source;
    }

    public bool HasAnyLines()
    {
        lock (Lines)
        {
            return Lines.Any();
        }
    }

    public IEnumerable<ChatLine> GetLines()
    {
        lock (Lines)
        {
            return new List<ChatLine>(Lines);
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions()
    {
        IgnoreReadOnlyProperties = true,
        IgnoreReadOnlyFields = true,
        WriteIndented = true
    };

    public static string GetPath(string source) => $"source_states/{source.Replace('/','_')}.json";
    public static ChannelState? Deserialize(string file)
    {
        file = GetPath(file);
        try
        {
            using var stream = File.OpenRead(file);
            var channelState = JsonSerializer.Deserialize<ChannelState>(stream, SerializerOptions) ?? throw new Exception();
            channelState.TokenCacheInvalid = true;
            Console.WriteLine($"successfully deserialized state for {channelState.Name}");
            return channelState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not deserialize channel state from {file}: {ex}");
            return null;
        }
    }

    public void Serialize()
    {
        var file = GetPath(Name);
        try
        {
            using var stream = File.Open(file, FileMode.Create);
            JsonSerializer.Serialize(stream, this, typeof(ChannelState));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"could not serialize {Name} state to {file}: {ex}");
        }
    }
    
    public bool CheckRate(int maxRate)
    {
        if (DateTime.UtcNow.Hour != LastReply.Hour)
        {
            LastReply = DateTime.UtcNow;
            RepliesThisHour = 1;
            return true;
        }

        RepliesThisHour++;
        return RepliesThisHour < maxRate;
    }

    public void CommitLine(ChatLine line)
    {
        lock (Lines)
        {
            Lines.Add(line);
            CurrentTokenHeight += line.Tokens;
            CurrentContextLength += line.Tokens;
            Serialize();
        }
    }

    public void MarkDecodeEvent()
    {
        LastDecodedTokenHeight = CurrentTokenHeight;
    }

    public void MarkResponse()
    {
        LastResponded = DateTime.UtcNow;
        RemainingPonderances = Config.PonderancesPerReply;
    }

    public bool CanPonder()
    {
        Console.WriteLine($"Asked if can ponder, {RemainingPonderances}: {RemainingPonderances > 0}");
        return RemainingPonderances > 0;
    }

    public void MarkPonderance()
    {
        RemainingPonderances--;
    }

    public void WipeHistory()
    {
        lock (Lines)
        {
            Lines.Clear();
            TokenCacheInvalid = true;
            CurrentContextLength = 0;
            NextTrim = -1;
        }
    }

    public void PruneHistoryToTokenCount(int tokens)
    {
        lock (Lines)
        {
            var start = Lines.Sum(l => l.Tokens);
            var removed = 0;
            while (Lines.Sum(l => l.Tokens) > tokens)
            {
                removed++;
                CurrentContextLength -= Lines[0].Tokens;
                TokenCacheInvalid = true;
                NextTrim = -1;
                Lines.RemoveAt(0);
            }

            var final = Lines.Sum(l => l.Tokens);
            CurrentContextLength = final;
            // Console.WriteLine();
            var message = $"* Compacting context, expect drop in performance. Removed {removed} lines, {start} -> {final} tokens.";

            if (Lines.Any())
            {
                var firstLine = Lines.First().ToStringWithChannel(this);
                if (firstLine.Length > 60)
                {
                    firstLine = firstLine.Substring(0, 60) + "..";
                }
                
                message += $" First line of new context: \"{firstLine}\"";
            }

            message += " Please wait...";
            
            ContextHasCompacted = true;
            ContextCompactionMessage = message;
        }
    }
}

public class GptUtil
{
    public static int NewlineToken = 13; // 50118; // 198
    private readonly Chatter Chatter;
    
    public GptUtil(Chatter chatter)
    {
        // Diffuser = diffuser ?? throw new ArgumentNullException(nameof(diffuser));
        Chatter = chatter ?? throw new ArgumentNullException(nameof(chatter));
    }

    public GenerationRequest CreateGenerationRequest(string? prompt = null,
        int[]? tokens = null,
        int numTokens = 64,
        bool stopAtNewline = false,
        double temperature = 1,
        double repetitionPenalty = 1,
        int loadIndex = -1,
        int storeIndex = -1,
        bool decodeOnly = false,
        int trimPast = -1,
        bool decodeWithEmbeddings = false)
    {
        var requestId = Random.Shared.Next();
        var requestObj = new GenerationRequest
        {
            type = "generate_once", prompt = prompt ?? "", tokens = tokens ?? new int[0],
            temperature = temperature,
            repetition_penalty = repetitionPenalty,
            force_funny = true,
            num_tokens = numTokens,
            id = requestId,
            eos_token = stopAtNewline ? NewlineToken : -1,
            use_past = loadIndex,
            save_past = storeIndex,
            decode_only = decodeOnly,
            interruptible = decodeOnly,
            trim_past = trimPast,
            decode_method = decodeWithEmbeddings ? "embeddings" : "additive"
        };

        return requestObj;
    }
    private Dictionary<string, IGptInstance> GptProcesses = new();
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
        {
            Channels[source] = ChannelState.Deserialize(source) ?? new ChannelState(source);   
        }

        Channels[source].OwnNick ??= OwnNick;
        return Channels[source];
    }
    public ConfigBlock GetConfig(string source) => GetChannel(source).Config;

    public IGptInstance? GetModelInstance(string source)
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

        if (nick.Contains("topkek") && (args.Contains('█') || args.Contains('─')))
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
        StartInstanceIfNotStarted(source);
        AnnotateTokens(line);
        channel.CommitLine(line);
        
        // while (channel.Lines.Count > 150)
        //     channel.Lines.RemoveAt(0);

        if (channel.KeepDecoding && channel.TokenBurden != channel.CurrentContextLength)
        {
            var requestId = GetPromptResponseForSource(source, onlyOneLine: true, decodeOnly: true,
                writeState: true);
            Console.WriteLine($"Actively decoding source {source}, request id {requestId}, context length {channel.CurrentContextLength}, token burden {channel.TokenBurden}");
        }

        if (Config.Contains("chatter.spontaneous_channels", source))
        {
            var chatChance = Config.GetDouble($"chatter.chance.{source}");
            if (chatChance == 0)
                chatChance = 0.02d;

            bool canReply = args.Contains(OwnNick, StringComparison.InvariantCultureIgnoreCase) && !Config.GetValue<bool>($"chatter.noreply.{source}");
            if (Random.Shared.NextDouble() < chatChance || canReply)
            {
                var enqueueResult = Chatter.EnqueueChatOneShot($"{canReply}\n{args}", source, nick);
                if (canReply && enqueueResult is not null)
                {
                    throw new ApplicationException(enqueueResult);
                }
            }
        }
    }

    public int CountTokens(string source, string message)
    {
        var instance = GetModelInstance(source);
        var tokenizeRequest = instance.RequestTokenize(message);
        var tokenized = instance.PollForResponse(tokenizeRequest, true)?.ToString();
        if (tokenized is null)
            return -1;
        var tokenCount = tokenized.Count(c => c == ' ') + 1;
        return tokenCount;
    }

    public void AnnotateTokens(ChatLine line)
    {
        if (line.Tokens > 0)
            return;
        var instance = GetModelInstance(line.Source);
        var tokenizeRequest = instance.RequestTokenize(line.ToString() + "\n");
        var tokenized = instance.PollForResponse(tokenizeRequest, true)?.ToString();
        if (tokenized is null)
            return;
        var tokenCount = tokenized.Count(c => c == ' ') + 1;
        line.Tokens = tokenCount;
    }

    public int GetChannelId(string source)
    {
        var channelBytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var channelId = (int)(BitConverter.ToUInt32(channelBytes) >> 10);
        return channelId;
    }
    
    public int GetPromptResponseForSource(string source, IEnumerable<ChatLine>? tempAddLine = null, bool forceExtra = false, string? asNick = null, string? complete = null, bool ignoreHistory = false, bool onlyOneLine = false, int numTokens = -1, bool writeState = true, bool decodeOnly = false, string? append = null, bool omitNewline = false)
    {
        var model = GetModel(source);
        if (string.IsNullOrWhiteSpace(model))
            throw new Exception("No model found for source");
        
        StartInstanceIfNotStarted(source);
        var instance = GptProcesses[model];

        var lines = new List<ChatLine>();
        var config = GetConfig(source);
        var channel = GetChannel(source);

        if (channel.HasAnyLines())
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

            if (channel.CurrentContextLength > config.TrimStart)
            {
                Console.WriteLine($"{source}: Context length {channel.CurrentContextLength} > trim_start {config.TrimStart}, marking cache invalid and trimming history");
                channel.PruneHistoryToTokenCount(config.TrimTarget);
            }
            else
            {
                Console.WriteLine($"{source}: Context length {channel.CurrentContextLength}");
            }
            
            lines.AddRange(channel.GetLines());
        }

        if (tempAddLine is not null)
        {
            // if (lines.Any())
            //     lines.RemoveAt(0);
            // lines.Add(tempAddLine);
            lines.AddRange(tempAddLine);
        }
        
        // var linesComposed = (ignoreHistory ? "" : string.Join('\n', lines.Select(l => $"{ChatLine.LeftDelimiter}{(l.Nick == OwnNick ? config.AssignedNick : l.Nick)}{ChatLine.RightDelimiter} {l.Message.Replace(OwnNick, config.AssignedNick, StringComparison.InvariantCultureIgnoreCase)}"))) + (omitNewline ? "" : "\n");
        var linesComposed = (ignoreHistory ? "" : string.Join('\n', lines.Select(l => l.ToStringWithChannel(channel)))) + (omitNewline ? "" : "\n");

        if (asNick is not null)
        {
            // linesComposed += $"{ChatLine.LeftDelimiter}{asNick}{ChatLine.RightDelimiter}";
            linesComposed += (new ChatLine() {Message = complete ?? "", Nick = asNick, Time = DateTime.UtcNow, Source = source}).ToStringWithChannel(channel);
        }

        if (append is not null)
        {
            linesComposed += append;
        }
        
        // TODO: probe newline likelihood backwards

        var longPrefixes = new[] { "!tldr", ".wiki", ".ud" };

        var extra = forceExtra ||
                    (lines.Any() && longPrefixes.Any(p => lines.LastOrDefault()?.Message?.StartsWith(p) ?? false));

        Console.WriteLine($"Prompt key is {Config.GetString($"gpt.prompt.{source}")}");
        Console.WriteLine($"Prompt key^2 is {Config.GetString(Config.GetString($"gpt.prompt.{source}"))}");
        var promptFile = Config.GetString(Config.GetString($"gpt.prompt.{source}"));
        if (File.Exists(promptFile))
        {
            linesComposed = File.ReadAllText(promptFile).Replace("$NICK", config.AssignedNick).Replace("$TIME", DateTime.UtcNow.ToString("R")) + linesComposed;
        }

        if (numTokens <= 0)
        {
            numTokens = extra ? 150 : onlyOneLine ? 78 : 64;
        }

        var channelId = GetChannelId(source);
        var storeId = channelId;
        var loadId = channelId;
        bool useEmbeddings = false;
        if (channel.TokenCacheInvalid)
        {
            loadId = -1;
            channel.TokenCacheInvalid = false;
            useEmbeddings = true;
        }

        if (ignoreHistory)
        {
            loadId = storeId = -1;
        }

        if (!writeState)
        {
            storeId = -1;
        }

        var trimLoad = -1;
        if (loadId != -1 && channel.NextTrim != -1)
        {
            Console.WriteLine($"{source}: Trimming to {channel.NextTrim}");
            trimLoad = channel.NextTrim;
            channel.NextTrim = -1;
        }

        var request = CreateGenerationRequest(linesComposed, numTokens: numTokens, stopAtNewline: onlyOneLine,
            temperature: config.Temperature, repetitionPenalty: config.RepetitionPenalty, loadIndex: loadId, storeIndex: storeId, decodeOnly: decodeOnly, trimPast: trimLoad, decodeWithEmbeddings: useEmbeddings);
        var requestId = instance.RequestGeneration(request);
        return requestId;
    }

    IGptInstance? CreateModelFromProcess(string model)
    {
        
        var binary = Config.GetString("gpt.binary");
        if (!File.Exists(binary))
            throw new Exception();
            
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

        if (GptProcesses.ContainsKey(model) && !GptProcesses[model].ModelDead)
        {
            GptProcesses[model].Kill();
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

            return instance;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
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

                if (process.ModelDead)
                {
                    // Console.WriteLine($"Exited: {process.Process.HasExited}, dead: {process.ModelDead}");
                    try
                    {
                        process.Kill();
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

            try
            {
                // GptProcesses[model] = CreateModelFromProcess(model) ?? throw new Exception();
                GptProcesses[model] = new NetworkedGptInstance("127.0.0.1", 8952);
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

                        var firstBracket = line.IndexOf(ChatLine.LeftDelimiter);
                        var lastBracket = line.IndexOf(ChatLine.RightDelimiter);

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
                var instance = GptProcesses[GetModel(source)];

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
                        if (instance.ModelDead)
                        {
                            // Console.WriteLine($"Exited: {instance.Process.HasExited}, dead: {instance.ModelDead}");
                            StartInstanceIfNotStarted(source);
                            instance = GptProcesses[GetModel(source)];
                            currentRequestId = -1;
                        }
                        
                        if (currentRequestId == -1)
                        {
                            Console.WriteLine($"Requesting more, {bufferedLines.Count} lines in buffer");
                            // currentRequestId = instance.RequestNextOverlappingOutput();
                            currentRequestId = -1;
                        }
                    }
                    
                    void ConsumeLine()
                    {
                        if (currentRequestId == -1)
                            return;

                        //var line = outReader.ReadLine() ?? throw new Exception("Read null line");
                        var result = instance.PollForResponse(currentRequestId, false)?.ToString();
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

                                if (nextReadyLine.StartsWith(ChatLine.LeftDelimiter))
                                {
                                    var endingBracket = nextReadyLine.IndexOf(ChatLine.RightDelimiter);
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
                                    if (instance.ModelDead)
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
                    while (!instance.ModelDead)
                    {
                        var requestId = -1; // TODO: hehe
                        var lines = instance.PollForResponse(requestId, true)?.ToString().Split('\n');

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
            // if (GptProcesses.ContainsKey(model))
            //     GptProcesses[model].Process.Kill(true);

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
}