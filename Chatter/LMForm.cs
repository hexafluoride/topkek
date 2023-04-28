using System.Text;
using System.Text.Json;

namespace Chatter;

public class LMForm
{
    public string Layout { get; set; } = "";
    public Dictionary<string, FormZone> Zones { get; set; } = new();

    public static LMForm FromFile(string file)
    {
        using var fs = File.OpenRead(file);
        return FromDocument(fs);
    }
    public static LMForm FromDocument(Stream document)
    {
        var form = new LMForm();
        var reader = new StreamReader(document, leaveOpen: true);
        var jsonPart = new StringBuilder();
        while (reader.ReadLine() is { } line)
        {
            if (line != "#")
            {
                jsonPart.AppendLine(line);
            }
            else
            {
                form.Layout = reader.ReadToEnd();
            }
        }

        var configParsed = JsonDocument.Parse(jsonPart.ToString()).RootElement;
        var zones = configParsed.GetProperty("zones");
        foreach (var zone in zones.EnumerateObject())
        {
            var zoneNames = new List<string>() { zone.Name };
            var zoneObject = zone.Value;
            if (zone.Name.Contains('|'))
            {
                zoneNames = zone.Name.Split('|').ToList();
            }

            foreach (var zoneName in zoneNames)
            {
                var parsedZone = FormZone.Parse(zoneName, zoneObject);
                if (parsedZone is null)
                {
                    Console.WriteLine($"Failed to handle zone {zoneName}");
                }
                else
                {
                    form.Zones[zoneName] = parsedZone;   
                }
            }
        }

        return form;
    }
    
    public FormExecution Execute(Dictionary<string, string> inputs, IGptInstance instance, int suggestedSlot, ConfigBlock? configBlock = default)
    {
        configBlock ??= new ConfigBlock();
        var execution = new FormExecution();
        var parts = new List<object>(); // the bad way, objects are either verbatim strings or zone ids

        var layoutCopy = Layout.ToString();
        var usedInputs = new HashSet<string>();
        while (layoutCopy.Length > 0)
        {
            var nextLeftBrace = layoutCopy.IndexOf('{');
            if (nextLeftBrace == 0)
            {
                var nextRightBrace = layoutCopy.IndexOf('}');
                if (nextRightBrace == -1)
                {
                    parts.Add(layoutCopy);
                    layoutCopy = "";
                    break;
                }

                var assembled = layoutCopy.Substring(1, nextRightBrace - 1);
                if (Zones.ContainsKey(assembled))
                {
                    var zone = Zones[assembled];
                    if (zone.Direction == ZoneDirection.In)
                    {
                        if (inputs.ContainsKey(assembled))
                        {
                            parts.Add(inputs[assembled]);
                            usedInputs.Add(assembled);
                        }
                        else
                        {
                            execution.Log.AppendLine(
                                $"Warn: arrived at zone \"{assembled}\" but no input provided (have {string.Join(", ", inputs.Keys)})");
                        }
                    }
                    else
                    {
                        parts.Add(zone);
                    }
                }
                else
                {
                    execution.Log.AppendLine($"Warn: unrecognized zone \"{assembled}\", skipping to {nextRightBrace + 1}?");
                }

                layoutCopy = layoutCopy.Substring(nextRightBrace + 1);
            }
            else if (nextLeftBrace != -1)
            {
                parts.Add(layoutCopy.Substring(0, nextLeftBrace));
                layoutCopy = layoutCopy.Substring(nextLeftBrace);
            }
            else
            {
                parts.Add(layoutCopy);
                layoutCopy = "";
                break;
            }
        }

        foreach (var (key, value) in inputs)
        {
            if (!usedInputs.Contains(key))
            {
                execution.Log.AppendLine($"Warn: unused input \"{key}\". check for misspellings? available keys for this form are: [{string.Join(", ", Zones.Where(z => z.Value.Direction == ZoneDirection.In).Select(p => p.Key))}]");
            }
        }
        
        Console.WriteLine($"Execution plan:");

        var currentString = new StringBuilder();
        for (int i = 0; i < parts.Count; i++)
        {
            if (!(parts[i] is string))
            {
                continue;
            }
            
            while (parts.Count > i && parts[i] is string stringPart)
            {
                currentString.Append(stringPart);
                parts.RemoveAt(i);
            }
            parts.Insert(i, currentString.ToString());
            currentString.Clear();
        }

        var generationRequest = new GenerationRequest()
        {
            type = "generate_once",
            temperature = configBlock.Temperature,
            repetition_penalty = configBlock.RepetitionPenalty,
            decode_method = "embeddings",
            eos_token = -1,
            use_past = -1,
            save_past = suggestedSlot,
            trim_past = -1,
            tokens = new int[0]
        };

        foreach (var part in parts)
        {
            Console.WriteLine("----------------------------");
            if (part is string print)
            {
                Console.WriteLine($"Print: {print.ReplaceLineEndings("\\n")}");
                generationRequest.prompt += print;
                generationRequest.num_tokens = 0;
                generationRequest.decode_only = true;
                var requestId = instance.RequestGeneration(generationRequest);
                var result = instance.PollForResponse(requestId, true) ?? throw new Exception();
                execution.Results.Add(result);
                generationRequest.use_past = generationRequest.save_past;
            }
            else if (part is FormZone zone)
            {
                Console.WriteLine($"Process zone: {zone.Name}");
                generationRequest.decode_only = false;
                generationRequest.decode_method = "additive";
                var modifiedRequest = zone.CreateGenerationRequest(instance, generationRequest);
                var requestId = instance.RequestGeneration(modifiedRequest);
                var result = instance.PollForResponse(requestId, true) ?? throw new Exception();
                var fillResult = zone.ProcessGenerationResult(result);

                if (fillResult.Rejected)
                {
                    execution.Log.AppendLine($"Zone {zone.Name} was rejected");
                    // TODO: Repeat
                }
                else
                {
                    execution.Outputs[zone.Name] = zone.GetValue<string>() ?? zone.GetValue<object>();
                    if (fillResult.KeepGeneration)
                    {
                        if (fillResult.ModifiedRequest is not null)
                        {
                            generationRequest = fillResult.ModifiedRequest;
                        }
                        else
                        {
                            generationRequest.prompt += result.ToString();
                        }
                    }
                }
                execution.Results.Add(result);
            }
            else
            {
                execution.Log.AppendLine($"Unrecognized step: {part}");
            }
        }
        
        return execution;
    }
}

public class FormExecution
{
    public List<GenerationResult> Results = new();
    public Dictionary<string, object?> Outputs = new();
    public StringBuilder Log = new();
}

public abstract class FormZone
{
    public FormZone(string name, ZoneDirection direction, string zoneType, JsonElement? options)
    {
        Name = name;
        Direction = direction;
        ZoneType = zoneType;
        Options = options;
    }

    public string Name { get; set; }
    public ZoneDirection Direction { get; set; }
    public string ZoneType { get; set; }
    public JsonElement? Options { get; set; }

    public FormZone(FormZone other)
    {
        Name = other.Name;
        Direction = other.Direction;
        ZoneType = other.ZoneType;
        Options = other.Options;
    }

    public abstract GenerationRequest CreateGenerationRequest(IGptInstance instance, GenerationRequest request);
    public abstract ZoneFulfillmentResult ProcessGenerationResult(GenerationResult result);
    public abstract T? GetValue<T>() where T : class;

    public static FormZone? Parse(string name, JsonElement element)
    {
        var type = element.GetProperty("type").GetString();
        var direction = Enum.Parse<ZoneDirection>(element.GetProperty("direction").GetString(), true);
        var hasOptions = element.TryGetProperty("options", out JsonElement optionsElement);
        switch (type)
        {
            case "text":
                return new TextFormZone(name, direction, type, hasOptions ? optionsElement : null);
            default:
                Console.WriteLine($"Could not handle zone type {type}");
                return null;
        }
    }
}

public class TextFormZone : FormZone
{
    public int MaxTokens = 100;
    public bool StopAtNewline = false;
    public int[][] Choices = new int[0][];
    private string[] ChoicesString = new string[0];
    
    private List<string> StoppingSuffixes = new(); 

    private string? _contents = null;
    
    public TextFormZone(FormZone other) : base(other)
    {       
        if (Options?.TryGetProperty("maxTokens", out JsonElement maxTokensElement) ?? false)
        {
            MaxTokens = maxTokensElement.GetInt32();
        }
        if (Options?.TryGetProperty("stoppingConditions", out JsonElement stoppingConditionsElement) ?? false)
        {
            var conditionsEnumerated = stoppingConditionsElement.EnumerateArray().ToList();
            var conditionsString = conditionsEnumerated.Where(s => s.ValueKind == JsonValueKind.String)
                .Select(s => s.GetString()).ToList();
            StopAtNewline = conditionsString.Contains("newline");
            StoppingSuffixes = conditionsString.Where(s => s.StartsWith("suffix:")).Select(s => s.Substring("suffix:".Length)).ToList();
            if (StopAtNewline)
            {
                StoppingSuffixes.Add("\n");
            }
        }
    }

    public TextFormZone(string name, ZoneDirection direction, string zoneType, JsonElement? options) : base(name, direction, zoneType, options)
    {
        if (Options?.TryGetProperty("maxTokens", out JsonElement maxTokensElement) ?? false)
        {
            MaxTokens = maxTokensElement.GetInt32();
        }
        if (Options?.TryGetProperty("stoppingConditions", out JsonElement stoppingConditionsElement) ?? false)
        {
            var conditionsEnumerated = stoppingConditionsElement.EnumerateArray().ToList();
            var conditionsString = conditionsEnumerated.Where(s => s.ValueKind == JsonValueKind.String)
                .Select(s => s.GetString()).ToList();
            StopAtNewline = conditionsString.Contains("newline");
            StoppingSuffixes = conditionsString.Where(s => s.StartsWith("suffix:")).Select(s => s.Substring("suffix:".Length)).ToList();
            if (StopAtNewline)
            {
                StoppingSuffixes.Add("\n");
            }
        }
        if (Options?.TryGetProperty("choices", out JsonElement choicesElement) ?? false)
        {
            ChoicesString = choicesElement.EnumerateArray().Select(j => j.GetString()).ToArray();
        }
    }

    public override GenerationRequest CreateGenerationRequest(IGptInstance instance, GenerationRequest request)
    {
        if (ChoicesString.Length != Choices.Length)
        {
            Console.WriteLine($"Caching {ChoicesString.Length} choices...");
            Choices = new int[ChoicesString.Length][];
            int i = 0;
            var longest = 0;
            foreach (var choice in ChoicesString)
            {
                var choiceTokens = instance.Tokenize(choice);
                if (choiceTokens[0] == 1)
                {
                    choiceTokens = choiceTokens.Skip(1).ToArray();
                }
                
                longest = Math.Max(longest, choiceTokens.Length);
                Choices[i++] = choiceTokens;
            }

            for (int j = 0; j < Choices.Length; j++)
            {
                var choice = Choices[j];
                if (choice.Length != longest)
                {
                    var newArr = new int[longest];
                    Array.Copy(choice, newArr, choice.Length);
                    Choices[j] = newArr;
                }
            }
        }

        if (Choices.Length > 0)
        {
            Console.WriteLine($"Applying {ChoicesString.Length} choices...");
            request.choices = Choices;
        }
        
        request.decode_only = false;
        request.num_tokens = MaxTokens;
        request.eos_token = StopAtNewline ? GptUtil.NewlineToken : -1;
        // request.decode_method = "additive";
        return request;
    }

    public override ZoneFulfillmentResult ProcessGenerationResult(GenerationResult result)
    {
        var resultString = result.ToString();
        if (result.Probabilities is not null && result.Probabilities.Length != 0)
        {
            var probsConverted = new Dictionary<string, double>();
            for (int i = 0; i < ChoicesString.Length; i++)
            {
                var choice = ChoicesString[i];
                probsConverted[choice] = 1;
            }

            for (int i = 0; i < result.Probabilities.Length; i++)
            {
                var probsSequence = result.Probabilities[i];
                for (int j = 0; j < probsSequence.Length; j++)
                {
                    probsConverted[ChoicesString[j]] *= probsSequence[j];
                }
            }
            
            Console.WriteLine($"After multiplying, final dict looks like: {JsonSerializer.Serialize(probsConverted)}");
            // probsConverted = probsConverted.Re
            for (int i = 0; i < ChoicesString.Length; i++)
            {
                var choice = ChoicesString[i];
                if (probsConverted[choice] < 0.01)
                {
                    probsConverted.Remove(choice);
                }
            }

            var topChoices = probsConverted.OrderByDescending(p => p.Value).Select(p => (p.Key, p.Value)).ToList();
            if (topChoices.Count == 0)
            {
                _contents = "oops?";
            }
            else
            {
                _contents = string.Join(" | ", topChoices.Take(Math.Min(topChoices.Count, 3)).Select(p => $"{p.Key} ({p.Value:P})"));
            }
        }
        else
        {
            _contents = resultString;
        }
        
        // if (StopAtNewline && !resultString.EndsWith('\n'))
        var matchingSuffixes = StoppingSuffixes.Select(s => resultString.IndexOf(s)).ToList();
        var earliestMatchIndex = -1;
        for (int i = 0; i < matchingSuffixes.Count; i++)
        {
            var suffixIndex = matchingSuffixes[i];
            if (suffixIndex == -1)
            {
                continue;
            }

            if (earliestMatchIndex == -1 || suffixIndex < matchingSuffixes[earliestMatchIndex])
            {
                earliestMatchIndex = i;
            }
        }
        
        if (earliestMatchIndex != -1)
        {
            var suffix = StoppingSuffixes[earliestMatchIndex];
            Console.WriteLine($"Earliest match index: {earliestMatchIndex} \"{suffix}\" ({matchingSuffixes.Count} total), trimming to index {matchingSuffixes[earliestMatchIndex]}, will throw away \"{resultString.Substring(matchingSuffixes[earliestMatchIndex])}\"");
            resultString = resultString.Substring(0, matchingSuffixes[earliestMatchIndex]);
            _contents = resultString;
            result.Request.prompt += resultString + suffix;
            return new ZoneFulfillmentResult(rejected: false, keepGeneration: true, modifiedRequest: result.Request);
        }
        else
        {
            Console.WriteLine($"Output matched none of {StoppingSuffixes.Count} suffixes");
        }
        return new ZoneFulfillmentResult(rejected: false, keepGeneration: true);
    }
    
    public override T? GetValue<T>() where T : class
    {
        var type = typeof(T);
        if (type == typeof(string) || type == typeof(object))
        {
            return _contents as T;
        }

        throw new Exception($"Cannot cast text to {type}");
    }
}

public record ZoneFulfillmentResult
{
    public readonly bool Rejected;
    public readonly bool KeepGeneration;
    public readonly GenerationRequest? ModifiedRequest;
    public readonly string? Message;

    public ZoneFulfillmentResult(bool rejected, bool keepGeneration = true, GenerationRequest? modifiedRequest = null, string? message = null)
    {
        Rejected = rejected;
        KeepGeneration = keepGeneration;
        ModifiedRequest = modifiedRequest;
        Message = message;
    }
}

public enum ZoneDirection
{
    In, Out
}