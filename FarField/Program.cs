using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HeimdallBase;
using OsirisBase;
using UserLedger;

namespace FarField
{
    class Program
    {
        static void Main(string[] args)
        {
            // SearchUtil.SearchImages("this was a triumph", false, false, false);
            new FarField().Start(args);
        }
    }    
    
    public partial class FarField : LedgerOsirisModule
    {
        public static TimedCache Cache { get; set; } = new TimedCache();
        public YoutubeUtil YoutubeUtil { get; set; }
        public GeocodeUtil GeocodeUtil { get; set; }
        public WeatherUtil WeatherUtil { get; set; }
        public TwitterUtil TwitterUtil { get; set; }
        private RedditUtil RedditUtil { get; set; }
        private TwitchUtil TwitchUtil { get; set; }
        private DiffuseUtil DiffuseUtil { get; set; }
        
        public void Start(string[] args)
        {
            Name = "farfield";
            Commands = new Dictionary<string, MessageHandler>()
            {
                {".im ", ImageSearch},
                {".ir ", ImageSearch},
                {".gif ", GifSearch},
                {".ddg ", DdgSearch},
                {".g ", DdgSearch},
                {".tu ", TumblrSearch},
                {".yt ", YoutubeSearch},
                {".tw ", TwitterSearch},
                {".weather", GetWeather},
                {".wa ", Wolfram},
                {".plot ", WolframPlot},
                {".fm", LastFm},
                {".lastfm", LastFm},
                {".resolve", ResolveLink},
                {".horoscope", GetHoroscope},
                {"http", MaybeResolveLink},
                {"$uncache ", Uncache},
                {".top", GetTopN},
                // {".hypno", Hypno},
                {".wiki ", GetWikipedia},
                {".dram", RamDelta},
                {".gc", RamDelta}
                // {".diffuse", Diffuse},
                // {".commit", CommitImage}
            };
            
            Init(args, FarFieldMain);
        }

        private long LastRam = 0;
        private DateTime LastRamChecked = DateTime.UtcNow;
        void RamDelta(string args, string source, string n)
        {
            if (args.StartsWith(".gc"))
            {
                GC.Collect();
            }
            
            var currentRam = GC.GetTotalMemory(false);
            var diff = currentRam - LastRam;
            var diffTime = DateTime.UtcNow - LastRamChecked;
            LastRamChecked = DateTime.UtcNow;
            LastRam = currentRam;
            
            SendMessage($"Current managed allocation: {currentRam / 1048576d:0.00}MB, {(diff > 0 ? "+" : "")}{diff} bytes since last check {((long)diffTime.TotalSeconds)}s ago", source);
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
                foreach (var arg in chunk.Split(' ',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = arg.TrimStart('-').Split('=');

                    if (parts.Length == 1)
                    {
                        request.Parameters[parts[0].ToLowerInvariant()] = "";
                    }
                    else
                    {
                        request.Parameters[parts[0].ToLowerInvariant()] = parts[1];
                    }
                }
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
                var firstQuote = args.IndexOf('"');
                var lastQuote = args.LastIndexOf('"');

                var prompt = args.Substring(firstQuote, (lastQuote - firstQuote) + 1).Trim('"').Trim();
                request.Prompt = prompt;
                var argsWithoutPrompt = args.Remove(firstQuote, (lastQuote - firstQuote) + 1);

                SetParams(argsWithoutPrompt);
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

                var modelRequest = DiffuseUtil.LastRequests[targetTuple];
                foreach (var param in request.Parameters)
                {
                    modelRequest.Parameters[param.Key] = param.Value;
                }

                if (!string.IsNullOrWhiteSpace(request.Prompt))
                {
                    modelRequest.Prompt = request.Prompt;
                }

                request = modelRequest;
            }

            request.Source = source;
            request.Nick = n;

            var queueResult = DiffuseUtil.EnqueueRequest(request);

            if (queueResult != null)
            {
                SendMessage($"{n}: {queueResult}", source);
            }
        }

        void GetWikipedia(string args, string source, string n)
        {
            var query = string.Join(' ', args.Split(' ').Skip(1));

            var result = FetchWikipediaSummaryFromPageId(query);

            if (!string.IsNullOrWhiteSpace(result))
            {
                SendMessage(result, source);
            }
            else
            {
                SendMessage("oops", source);
            }
        }
        
        void Uncache(string args, string source, string n)
        {
            args = args.Substring("$uncache".Length).Trim();

            if (!string.IsNullOrWhiteSpace(Cache.LastHit))
            {
                SendMessage($"Last hit: {Cache.LastHit}", source);
            }
            
            var cacheItem = Cache.Get(args);

            if (cacheItem != null)
            {
                if (Cache.Remove(args))
                    SendMessage($"Successfully removed cache item {cacheItem.ID}: {cacheItem.Content}", source);
                else
                    SendMessage($"Failed to remove cache item {cacheItem.ID}: {cacheItem.Content}", source);
            }
            else
            {
                SendMessage($"Could not find cache item with ID {args}", source);
            }
        }
        
        Regex linkRegex = new Regex(@"(?<Protocol>\w+):\/\/(?<Domain>[\w@][\w-.:@]+)\/?[\w\.~?=%&=\-@+:!\/$,]*");


        void MaybeResolveLink(string args, string source, string n)
        {
            try
            {
                //string channel = source.Substring(0, 16);
                if (Config.Contains("links.disabled", source))
                    return;

                //Console.WriteLine(args);

                if (args.StartsWith("Reporting in!"))
                    return;

                if (args.StartsWith(".diffuse"))
                    return;

                if (args.Contains("diffuse.kate.land"))
                    return;

                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

                //Regex regex = new Regex(@"(?<Protocol>\w+):\/\/(?<Domain>[\w@][\w-.:@]+)\/?[\w\.~?=%&\+\!=\-@/$,]*");
                if (!linkRegex.IsMatch(args))
                    return;

                var match = linkRegex.Matches(args)[0];
                int fragment_index = match.Index + match.Length;
                string fragment = "";

                if (args.Length > fragment_index)
                {
                    if (args[fragment_index] == '#')
                    {
                        while (args.Length > fragment_index && args[fragment_index] != ' ')
                            fragment += args[fragment_index++];
                    }
                }

                string url = match.Value + fragment;

                var result = LinkResolver.GetSummary(url).Result;
                string summary = result.Value;

                if (summary == "-")
                    return;

                summary = HttpUtility.HtmlDecode(summary);

                bool cache_hit = false;
                if (cache_hit = summary.EndsWith("(cache hit)"))
                {
                    summary = summary.Substring(0, summary.Length - "(cache hit)".Length);
                }

                sw.Stop();
                SendMessage(string.Format("{0} ({1}s{2})", summary, sw.Elapsed.TotalSeconds.ToString("0.00"), cache_hit ? "-cache" : ""), source);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        void ResolveLink(string args, string source, string n)
        {
            var link = args.Substring(".resolve".Length).Trim();
            SendMessage(LinkResolver.GetSummary(link, true).Result.Value, source);
        }

        void FarFieldMain()
        {
            YoutubeUtil = new YoutubeUtil();
            YoutubeUtil.LoadKeys();

            GeocodeUtil = new GeocodeUtil();
            WeatherUtil = new WeatherUtil();
            TwitterUtil = new TwitterUtil();
            TwitterUtil.Init();

            RedditUtil = new RedditUtil();
            RedditUtil.ObtainKey();

            TwitchUtil = new TwitchUtil();
            TwitchUtil.ObtainKey();

            HoroscopeUtil = new HoroscopeUtil();

            DiffuseUtil = new DiffuseUtil(this);

            new Thread(DiffuseUtil.ProcessorThread).Start();
            
            LinkResolver.AddResolver(new YoutubeLinkResolver(YoutubeUtil));
            LinkResolver.AddResolver(new TwitterResolver(TwitterUtil));
            LinkResolver.AddResolver(new SoundCloudResolver());
            LinkResolver.AddResolver(new RedditResolver(RedditUtil));
            LinkResolver.AddResolver(new TwitchResolver(TwitchUtil));
            LinkResolver.AddResolver(new FourChanResolver());
        }
    }
}