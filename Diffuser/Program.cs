using System.Text;
using OsirisBase;
using UserLedger;

namespace Diffuser
{
    class Program
    {
        static void Main(string[] args)
        {
            new Diffuser().Start(args);
        }
    }    
    
    public partial class Diffuser : LedgerOsirisModule
    {
        private DiffuseUtil DiffuseUtil { get; set; }
        private GptUtil GptUtil { get; set; }
        private EspeakUtil EspeakUtil { get; set; }
        
        public void Start(string[] args)
        {
            Name = "Diffuser";
            GptUtil = new GptUtil(this);
            EspeakUtil = new EspeakUtil(this);
            
            Commands = new Dictionary<string, MessageHandler>()
            {
                {".diffuse", Diffuse},
                {".hypno", Diffuse},
                {".commit", CommitImage},
                // {"$gpt ", HandleGpt},
                // {".chat", ChatOneShot},
                {"", HandleTtsBroken},
                {".tts ", HandleTts},
                {".tiktok ", HandleTts},
                {".sing ", HandleTts},
                {".aus ", HandleTts},
                {".uk ", HandleTts},
                {".jp ", HandleTts},
                {".negative", SetNegative}
                // {".gptwa ", GptWolfram}
            };

            new Thread((ThreadStart)delegate
            {
                while (true)
                {
                    lock (startPatch)
                    {
                        for (int i = 0; i < pendingTts.Count; i++)
                        {
                            try
                            {
                                var pending = pendingTts[i];
                                (var nick, var source) = pending;

                                if (startPatch.ContainsKey(nick))
                                {
                                    var age = DateTime.Now - startPatch[nick];

                                    if (age.TotalMilliseconds > 200)
                                    {
                                        pendingTts.RemoveAt(i);
                                        i--;
                                        FinishTts(nick, source);
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                //throw;
                            }
                        }
                    }
                    
                    Thread.Sleep(50);
                }
            }).Start();
            
            Init(args, DiffuserMain);
        }
        
        int currentChatRequests = 0;

        private Dictionary<string, List<string>> patchBuffer = new();
        private Dictionary<string, DateTime> startPatch = new();

        private List<(string, string)> pendingTts = new();

        public void FinishTts(string n, string source)
        {
            lock (startPatch)
            {
                string type = "normal";
                string prompt = "";
                var args = string.Join(' ', patchBuffer[n]);
                startPatch.Remove(n);
                patchBuffer.Remove(n);

                if (args.StartsWith(".tts"))
                    prompt = args.Substring(".tts".Length).Trim();
                else if (args.StartsWith(".tiktok"))
                {
                    prompt = args.Substring(".tiktok".Length).Trim();
                    type = "tiktok";

                    var parts = prompt.Split(' ');
                    if (parts.Length > 1)
                    {
                        var possibleVoice = parts[0];

                        if (possibleVoice.Length > 3 && possibleVoice[2] == '_')
                        {
                            type = possibleVoice;
                            prompt = prompt.Substring(possibleVoice.Length).Trim();
                        }
                    }
                }
                else if (args.StartsWith(".sing"))
                {
                    var singVoices = new[]
                    {
                        "en_female_f08_salut_damour",
                        "en_male_m03_lobby",
                        "en_male_m03_sunshine_soon",
                        "en_female_f08_warmy_breeze",
                        "en_female_ht_f08_glorious",
                        "en_male_sing_funny_it_goes_up",
                        "en_male_m2_xhxs_m03_silly",
                        "en_female_ht_f08_wonderful_world"
                    };
                    prompt = args.Substring(".sing".Length).Trim();
                    type = singVoices[Random.Shared.Next(singVoices.Length)];
                }
                else if (args.StartsWith(".aus"))
                {
                    prompt = args.Substring(".aus".Length).Trim();
                    type = "en_au_001";
                }
                else if (args.StartsWith(".uk"))
                {
                    prompt = args.Substring(".uk".Length).Trim();
                    type = "en_uk_001";
                }
                else if (args.StartsWith(".jp"))
                {
                    prompt = args.Substring(".jp".Length).Trim();
                    type = "jp_001";
                }

                try
                {
                    var returned = EspeakUtil.DoTts(prompt, type);

                    if (returned is null)
                    {
                        SendMessage($"{n}: Could not do TTS", source);
                    }
                    else
                    {
                        SendMessage($"{n}: {returned}", source);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    SendMessage($"{n}: Oops! {e.ToString()}", source);
                }
            }
        }
        
        public void HandleTtsBroken(string args, string source, string n)
        {
            // GptUtil.RecordLine(args, source, n);
            
            lock (startPatch)
            {
                if (!startPatch.ContainsKey(n))
                    return;

                if (patchBuffer[n].Count > 10)
                    return;
                
                if (args.StartsWith(".tiktok") || args.StartsWith(".tts") || args.StartsWith(".uk") || args.StartsWith(".aus") || args.StartsWith(".sing") || args.StartsWith(".jp"))
                    return;

                startPatch[n] = DateTime.Now;
                patchBuffer[n].Add(args);
            }
        }
        
        public void HandleTts(string args, string source, string n)
        {
            var prompt = args;

            lock (startPatch)
            {
                patchBuffer[n] = new();
                patchBuffer[n].Add(args);
                startPatch[n] = DateTime.Now;
                pendingTts.Add((n, source));
            }
        }

        public void GptWolfram(string args, string source, string n)
        {
            
        }
        
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

                ChatLine? tempLine = null;

                var chatLong = false;
                if (args.StartsWith(".chatlong"))
                {
                    chatLong = true;
                    args = ".chat" + args.Substring(".chatlong".Length);
                }
                
                if (args.StartsWith(".chat") && args.Length > ".chat".Length + 1)
                {
                    var tempLineAdd = args.Substring(".chat".Length).Trim();
                    tempLine = new ChatLine()
                    {
                        Message = tempLineAdd,
                        Nick = n,
                        Source = source,
                        Time = DateTime.UtcNow
                    };
                }

                var results = GptUtil.GetPromptResponseForSource(source, tempLine, chatLong).Split('\n');

                Console.WriteLine($"{results.Length} results");

                string CleanLine(string result)
                {
                    var resultWriter = new StringBuilder();

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
                            resultWriter.Append("\x02\x02");
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
                    var result = results[0];
                    bool preferredFound = false;

                    for (int i = 0; i < results.Length; i++)
                    {
                        result = results[i];

                        if (result.Contains("coinman") || result.Contains("╠╗"))
                            continue;

                        var wordCount = result.Split(' ',
                            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length;
                        if (wordCount > 6)
                        {
                            preferredFound = true;
                            break;
                        }
                    }

                    if (!preferredFound)
                        result = results[0];

                    try
                    {
                        result = result.Trim();

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
                        //result = {lineContents}";
                        result = lineContents.Trim();

                        //GptUtil.RecordLine(lineContents, source, "diffuser");
                    }
                    catch
                    {

                    }

                    var trimmedResult = result.Substring(0, Math.Min(512, result.Length));
                    var resultLine = new ChatLine()
                        {Message = trimmedResult, Nick = "diffuser", Source = source, Time = DateTime.UtcNow};
                    GptUtil.CandidateLines[source] = resultLine;

                    var resultStr = CleanLine(result);
                    SendMessage(resultStr, source);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
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

        void CommitImage(string args, string source, string n)
        {
            // Commits the last generated images.
            var targetNick = n;
            var targetTuple = (source, targetNick);

            if (!DiffuseUtil.LastRequests.ContainsKey(targetTuple))
            {
                SendMessage($"{targetNick} hasn't requested a diffusion here yet.", source);
                return;
            }

            var request = DiffuseUtil.LastRequests[targetTuple];

            if (!DiffuseUtil.Results.ContainsKey(request.Id))
            {
                SendMessage("I don't have results from that diffusion.", source);
                return;
            }

            var responses = DiffuseUtil.Results[request.Id];
            var commitIds = DiffuseUtil.CommitResults(responses);
            
            SendMessage($"{n}: your diffusions have been committed with ID(s) {string.Join(", ", commitIds)}", source);
        }

        void Diffuse(string args, string source, string n)
        {
            if (args.StartsWith(".hypno"))
            {
                args = args.Substring(".hypno".Length);
                args = ".diffuse" + args;
            }
            
            if (args.Trim() == ".diffuse" || args.Trim() == ".diffuse help")
            {
                SendMessage(
                    ".diffuse - Invoke a prompt-to-image/image-to-image Stable Diffusion image generation. Basic usage is .diffuse \"prompt\".", source);
                SendMessage("You can set parameters (width/height=[128-1024], cfg=[1.0-20.0], steps=[5-at least 100, max depends on res], gan, seed=[integer], copies=[1-4], img=[url], pick=[1-copies], overwrite=[0.0-1.0]) like so: .diffuse \"example prompt\" seed=17843 width=256 steps=100 copies=2 gan. Quote your prompt with \" when supplying parameters!", source);
                SendMessage("gan performs a 2x GAN upscaling (cheap, improves faces), cfg= is \"Classifier-Free Guidance\". Use img= for img2img, and overwrite= to specify how much the input image should be overwritten. pick= indexes into a batch size as specified by copies=.", source);
                SendMessage("Use .diffuse last to repeat your last invocation with altered parameters, like .diffuse last steps=100. You can also specify a nick to reuse their last invocation from the channel you're in, like .diffuse last=kate steps=100", source);
                return;
            }
            
            args = args.Substring(".diffuse ".Length).Trim();
            var request = new TextToImageRequest();

            void SetParams(string chunk)
            {
                string? tag = null;
                string? value = null;
                StringBuilder current = new();
                bool inQuote = false;
                bool quoted = false;
                bool escaping = false;

                void FinishCurrent()
                {
                    if (tag is null)
                    {
                        tag = current.ToString();
                    }
                    else
                    {
                        value = current.ToString();
                    }
                    request.Parameters[tag] = value ?? "";
                    tag = null;
                    value = null;
                    inQuote = false;
                    escaping = false;
                    quoted = false;
                    current.Clear();
                }
                
                for (int i = 0; i < chunk.Length; i++)
                {
                    char currentChar = chunk[i];
                    if (!inQuote && currentChar == ' ')
                    {
                        // Commit current
                        if (tag == null && quoted)
                        {
                            tag = "prompt";
                        }
                        FinishCurrent();
                    }
                    else if (!inQuote && currentChar == '"')
                    {
                        inQuote = true;
                        quoted = true;
                    }
                    else if (inQuote && !escaping && currentChar == '"')
                    {
                        inQuote = false;
                    }
                    else if (!inQuote && currentChar == '=')
                    {
                        tag = current.ToString();
                        current.Clear();
                    }
                    else
                    {
                        current.Append(currentChar);
                    }
                }

                if (current.Length > 0)
                {
                    // Commit current
                    if (tag == null && quoted)
                    {
                        tag = "prompt";
                    }
                    FinishCurrent();
                }
                
                // foreach (var arg in chunk.Split(' ',
                //              StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                // {
                //     var parts = arg.TrimStart('-').Split('=');
                //
                //     if (parts.Length == 1)
                //     {
                //         request.Parameters[parts[0].ToLowerInvariant()] = "";
                //     }
                //     else
                //     {
                //         request.Parameters[parts[0].ToLowerInvariant()] = parts[1];
                //     }
                // }
            }

            if (args.Count(c => c == '"') < 2)
            {
                var argsWords = args.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (argsWords[0] == "last" || argsWords.Any(p => p == "last" || p.StartsWith("last=")))
                {
                    SetParams(args);
                }
                else
                {
                    request.Prompt = args;
                }
            }
            else
            {
                // var firstQuote = args.IndexOf('"');
                // var lastQuote = args.LastIndexOf('"');
                //
                // var prompt = args.Substring(firstQuote, (lastQuote - firstQuote) + 1).Trim('"').Trim();
                // request.Prompt = prompt;
                // var argsWithoutPrompt = args.Remove(firstQuote, (lastQuote - firstQuote) + 1);

                SetParams(args);
                if (request.Parameters.ContainsKey("prompt"))
                    request.Prompt = request.Parameters["prompt"];
            }
            
            if (request.Parameters.ContainsKey("last"))
            {
                var targetNick = request.Parameters["last"];
                if (string.IsNullOrWhiteSpace(targetNick))
                    targetNick = n;

                var targetTuple = (source, targetNick);

                if (!DiffuseUtil.LastRequests.ContainsKey(targetTuple))
                {
                    SendMessage($"{targetNick} has not yet invoked a successful diffusion in this channel.", source);
                    return;
                }

                var modelRequest = DiffuseUtil.LastRequests[targetTuple].Clone();
                foreach (var param in request.Parameters)
                {
                    modelRequest.Parameters[param.Key] = param.Value;
                }

                if (!string.IsNullOrWhiteSpace(request.Prompt))
                {
                    modelRequest.Prompt = request.Prompt;
                }

                request = modelRequest;
                request.Id = Guid.NewGuid();
            }

            if (!request.Parameters.ContainsKey("negative"))
            {
                string? defaultNegative = GetUserDataForSourceAndNick<string>(source, n, "diffuser.negative");
                if (!string.IsNullOrWhiteSpace(defaultNegative))
                {
                    request.Parameters["negative"] = defaultNegative;
                }
            }

            if (request.Parameters.ContainsKey("dump"))
            {
                foreach ((string key, string val) in request.Parameters)
                {
                    SendMessage($"\"{key}\": \"{val}\"", source);
                }
            }

            request.Source = source;
            request.Nick = n;

            var queueResult = DiffuseUtil.EnqueueRequest(request);

            if (queueResult != null)
            {
                SendMessage($"{n}: {queueResult}", source);
            }
        }

        void SetNegative(string args, string source, string n)
        {
            string negativePrompt = args.Substring(".negative".Length).Trim();
            SetUserDataForSourceAndNick(source, n, "diffuser.negative", negativePrompt);
            SendMessage($"Set your default negative prompt to \"{negativePrompt}\"", source);
        }

        void DiffuserMain()
        {
            DiffuseUtil = new DiffuseUtil(this);
            //GptUtil = new GptUtil(this);

            // new Thread(DiffuseUtil.ProcessorThread).Start();
            new Thread((ThreadStart)delegate { DiffuseUtil.ProcessorThread().Wait(); }).Start();
        }
    }
}