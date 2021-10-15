using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
                {".fm", LastFm},
                {".lastfm", LastFm},
                {".resolve", ResolveLink},
                {".horoscope", GetHoroscope},
                {"http", MaybeResolveLink},
                {"$uncache ", Uncache},
                {".top", GetTopN}
            };
            
            Init(args, FarFieldMain);
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

                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

                //Regex regex = new Regex(@"(?<Protocol>\w+):\/\/(?<Domain>[\w@][\w-.:@]+)\/?[\w\.~?=%&\+\!=\-@/$,]*");
                Regex regex = new Regex(@"(?<Protocol>\w+):\/\/(?<Domain>[\w@][\w-.:@]+)\/?[\w\.~?=%&=\-@+:!\/$,]*");

                if (!regex.IsMatch(args))
                    return;

                var match = regex.Matches(args)[0];
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

            HoroscopeUtil = new HoroscopeUtil();
            
            LinkResolver.AddResolver(new YoutubeLinkResolver(YoutubeUtil));
            LinkResolver.AddResolver(new TwitterResolver(TwitterUtil));
            LinkResolver.AddResolver(new SoundCloudResolver());
            LinkResolver.AddResolver(new RedditResolver(RedditUtil));
            LinkResolver.AddResolver(new FourChanResolver());
        }
    }
}