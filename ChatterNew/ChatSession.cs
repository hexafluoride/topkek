using System.Globalization;
using System.Text.Json.Serialization;
using Lanbridge;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace ChatterNew;

// ReACT chat agent

public class ChatSession : IChatSession
{
    public string Name { get; set; }
    public const int NewlineToken = 13;
    [JsonIgnore]
    public GenerationContext Context { get; private set; }

    public DateTime LastNotified { get; set; }
    public ConfigBlock Config { get; set; } = new();
    public IList<IChatLine> History => HistoryInternal;
    private List<IChatLine> HistoryInternal { get; set; } = new();
    private bool KeepDecoding = true;
    private int NextTrim = -1;

    public int HistoryLength => HistoryInternal.Count;

    private static Memory<double> Buffer = new double[32000];
    private string[] PromptPaths = new string[0];

    public ChatSession()
    {
    }

    public ChatSession(string name)
    {
        Name = name;
        PromptPaths = GetPromptFilenames(name);
    }

    public static ChatSession? FromFile(string filename)
    {
        using var fs = File.OpenRead(filename);
        var session = JsonSerializer.Deserialize<ChatSession>(fs);

        if (session is not null)
        {
            session.PromptPaths = GetPromptFilenames(session.Name);
        }

        return session;
    }

    public static string[] GetPromptFilenames(string source)
    {
        var paths = new List<string>();
        var sourceParts = source.Split('/').ToList();
        
        if (sourceParts.Count == 2)
        {
            while (sourceParts.Any())
            {
                string promptPath = $"prompts/{string.Join('/', sourceParts)}/prompt.txt";
                if (File.Exists(promptPath))
                {
                    paths.Add(promptPath);
                }

                sourceParts.RemoveAt(sourceParts.Count - 1);
            }
        }
        
        paths.Add("prompts/prompt.txt");
        return paths.ToArray();
    }

    public string? GetPromptTemplate()
    {
        if (Config.Prompt is not null)
        {
            return Config.Prompt;
        }
        
        string? activePromptPath = PromptPaths.FirstOrDefault(File.Exists);
        if (activePromptPath is null)
        {
            Console.WriteLine($"No prompt found, checked paths {string.Join(", ", PromptPaths)}");
            return null;
        }

        return File.ReadAllText(activePromptPath);
    }

    private DateTime LastPromptRead = DateTime.MinValue;
    private string? LastPrompt = null;
    private int[]? LastPromptTokens = null;
    public int[]? GetPromptTokens()
    {
        string? activePromptPath = PromptPaths.FirstOrDefault(File.Exists);
        if (activePromptPath is null)
        {
            Console.WriteLine($"No prompt found, checked paths {string.Join(", ", PromptPaths)}");
            return null;
        }
        
        TimeSpan cacheAge = DateTime.UtcNow - LastPromptRead;
        bool stale = LastPrompt is null;
        stale = stale || cacheAge.TotalSeconds > 10;

        if (stale)
        {
            LastPrompt = GetPromptTemplate() ?? throw new Exception("what the fuck");

            LastPrompt = LastPrompt.Replace("{{ self_nick }}", Config.AssignedNick);
            LastPrompt = LastPrompt.Replace("{{ time }}", DateTime.Now.ToString(CultureInfo.InvariantCulture));
            LastPrompt = LastPrompt.Replace("{{ day_of_week }}", DateTime.Now.DayOfWeek.ToString());

            LastPromptRead = DateTime.UtcNow;
            LastPromptTokens = Context.Tokenize(LastPrompt).ToArray();
        }

        return LastPromptTokens;
    }

    public void Save(string filename)
    {
        FileStream fs;
        if (!File.Exists(filename))
        {
            fs = File.Create(filename);
        }
        else
        {
            fs = File.Open(filename, FileMode.Truncate);
        }
        JsonSerializer.Serialize(fs, this);
        fs.Close();
        fs.Dispose();
    }

    public void WipeHistory()
    {
        HistoryInternal.Clear();
        SynchronizeHistoryToContext();
    }

    private bool ShouldPruneHistory => Context.InputTokens.Count >= Config.TrimStart;
    private void PruneHistory(int room = 0)
    {
        SynchronizeHistoryToContext();
        
        int totalTokens = Context.InputTokens.Count;
        int prevTokens = totalTokens;
        while (totalTokens > Math.Max(0, Config.TrimTarget - room) && HistoryInternal.Any())
        {
            IChatLine line = HistoryInternal[0];
            totalTokens -= line.Tokens.Count;
            HistoryInternal.RemoveAt(0);
        }
        
        SynchronizeHistoryToContext();
        SendToSleep();
        // TODO: notify
        Console.WriteLine($"Pruned {Name} from {prevTokens} to {totalTokens}/{Context.InputTokens.Count}:\n{Context.Detokenize(Context.InputTokens)}");
    }

    public void SendToSleep()
    {
        Console.WriteLine($"{Name} going to sleep");
        KeepDecoding = false;
        Context.FreezeBuffer();
    }

    public void AddHistoryLine(IChatLine line, bool decodeImmediately = true)
    {
        if (ShouldPruneHistory)
        {
            PruneHistory(line.Tokens.Count);
        }
        
        HistoryInternal.Add((ChatLine)line); // TODO: i am evil
        
        string rendered = line.ToString() ?? throw new Exception("could not render line");
        List<int> tokens = TokenizeSpecial(rendered);
        tokens.Add(NewlineToken);
        
        Context.AddTokens(tokens);
        
        // TODO: send channels to sleep
        if (KeepDecoding && decodeImmediately)
        {
            Console.WriteLine($"{Name} keep decoding, {Context.InputTokens.Count} tokens");
            Context.DecodeTokens();
        }
    }
    
    private List<List<int>> LastChatPrefixes = new();
    
    public IChatLine? SimulateChatFromPerson(string person)
    {
        if (ShouldPruneHistory)
        {
            PruneHistory(0);
        }
        
        string prompt = $"<{person}>";
        List<int> promptTokens = TokenizeSpecial(prompt);
        Context.AddTokens(promptTokens);

        // TODO: better sampling loop
        List<int> sampledTokens = new List<int>();

        int maxTokens = 80;
        int minTokens = Random.Shared.Next(1, 10);
        
        for (int i = 0; i < maxTokens; i++)
        {
            int[]? bannedTokens = null;
            if (i == 0)
            {
                bannedTokens = LastChatPrefixes.SelectMany(i => i).Distinct().ToArray();
            }

            int sampled = Context.CalculateLogits(Buffer, keepWarm: true, trimPast: NextTrim,
                temperature: Config.Temperature, topK: 50, topP: 0.95, repetitionPenalty: Config.RepetitionPenalty,
                repetitionPenaltyDecay: Config.RepetitionPenaltyDecay,
                repetitionPenaltySustain: Config.RepetitionPenaltySustain, bannedTokens: bannedTokens);
            
            NextTrim = -1;

            // Context.ProcessLogits(Buffer, Buffer);
            // int sampled = TokenUtilities.SampleLogit(Buffer);

            sampledTokens.Add(sampled);
            Context.CommitToken(sampled);
            
            // Console.WriteLine($"{Name} Current context: {Context.Detokenize(Context.InputTokens)}");
            
            if (sampled == NewlineToken)
            {
                break;
            }
        }

        if (sampledTokens.Last() != NewlineToken)
        {
            sampledTokens.Add(NewlineToken);
            Context.CommitToken(NewlineToken);
        }
        
        // actually don't parse
        string generationResult = Context.Detokenize(sampledTokens);
        if (generationResult.EndsWith('\n'))
        {
            generationResult = generationResult.TrimEnd('\n');
        }

        if (generationResult.StartsWith(' '))
        {
            generationResult = generationResult.Substring(1);
        }

        if (sampledTokens.Any())
        {
            var prefix = sampledTokens.Take(Math.Min(sampledTokens.Count, 1)).ToList();
            LastChatPrefixes.Add(prefix);
            while (LastChatPrefixes.Count > Config.NoRepeatLines && LastChatPrefixes.Any())
            {
                LastChatPrefixes.RemoveAt(0);
            }
        }
        
        ChatLine line = new ChatLine(person, generationResult, promptTokens.Concat(sampledTokens).ToList());
        HistoryInternal.Add(line);
        KeepDecoding = true;
        return line;
    }

    public void RollbackHistory(int n)
    {
        for (int i = 0; i < n; i++)
        {
            if (!HistoryInternal.Any())
            {
                // hmm
                return;
            }

            HistoryInternal.RemoveAt(HistoryInternal.Count - 1);
        }
        
        SynchronizeHistoryToContext();
    }

    private void SynchronizeHistoryToContext()
    {
        NextTrim = 0;
        Context.InputTokens.Clear();
        Context.InputTokens.Add(1);

        int[]? promptTokens = GetPromptTokens();
        if (promptTokens is not null)
        {
            Console.WriteLine($"Adding prompt tokens: {string.Join(", ", promptTokens)}");
            Console.WriteLine(Context.Detokenize(promptTokens));
            Context.InputTokens.AddRange(promptTokens);
        }
        
        Context.InputTokens.AddRange(HistoryInternal.SelectMany(line => line.Tokens));
        Console.WriteLine($"Synchronized history to context, {Context.InputTokens.Count} tokens");
    }

    private Dictionary<string, List<int>> _tokenizeCache = new();
    public List<int> TokenizeSpecial(string text)
    {
        if (_tokenizeCache.ContainsKey(text))
        {
            return _tokenizeCache[text];
        }
        
        var tokens = Context.Tokenize(text);
        if (text.StartsWith('<'))
        {
            if (tokens[0] == 529)
            {
                tokens[0] = 29966;
            }
        }

        if (_tokenizeCache.Count > 10000)
        {
            // hack
            _tokenizeCache.Clear();
        }
        _tokenizeCache[text] = tokens;
        return tokens;
    }
    
    public IChatLine? ProcessChatLine(string args, string source, string nick, string ownNick)
    {
        args = args.Replace(ownNick, Config.AssignedNick, StringComparison.InvariantCultureIgnoreCase);
        List<int> tokens = new List<int>();
        ChatLine chatLine = new ChatLine(nick, args, tokens);
        tokens.AddRange(TokenizeSpecial(chatLine.ToString() + "\n"));
        return chatLine;
    }

    public void UseGptInstance(NetworkedGptInstance instance)
    {
        GenerationContext newContext = new(instance);
        Context = newContext;
        SynchronizeHistoryToContext();
        UpdateConfig();
    }

    public void UpdateConfig()
    {
        Console.WriteLine($"Updating config: {JsonSerializer.Serialize(Config)}");
        Context.LogitTransforms.Clear();
        AddLogitTransforms(Context.LogitTransforms);
    }

    private void AddLogitTransforms(IList<ILogitTransform> list)
    {
        list.Add(new BannedTokensLogitTransform(new[] {1, 2}));
        list.Add(new RepetitionPenaltyLogitTransform(Config.RepetitionPenalty));
        list.Add(new TemperatureLogitTransform(Config.Temperature));
        list.Add(new TopTokensLogitTransform(0.95, 50));
    }
}