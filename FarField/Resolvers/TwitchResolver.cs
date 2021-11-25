using System;

namespace FarField
{
    public class TwitchResolver : IResolver
    {
        public string Name => "twitch";
        public TwitchUtil TwitchUtil { get; set; }
    
        public TwitchResolver(TwitchUtil util)
        {
            TwitchUtil = util;
            util.ObtainKey();
        }
    
        //public bool Matches(string URL) => (URL.Contains("reddit.com") && URL.Contains("/comments/")) || URL.Contains("i.redd.it") || URL.Contains("v.redd.it");
        public bool Matches(string URL) => Uri.TryCreate(URL, UriKind.Absolute, out Uri uri) &&
            (uri.Host.Equals("twitch.tv", StringComparison.InvariantCultureIgnoreCase) ||
            uri.Host.Equals("www.twitch.tv", StringComparison.InvariantCultureIgnoreCase));
        //public string GetCacheID(string URL) => "twitter:" + TwitterUtil.GetTweetId(URL);
        public string GetCacheID(string url)
        {
            return "";
        }

        public string GetPermaName(string url)
        {
            if (!url.Contains("/comments/"))
                return url;
            
            var parts = url.Split("/comments/");
            var second = parts[1];
            var secondParts = second.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (secondParts.Length < 3)
                return "t3_" + secondParts[0];
            else
                return "t1_" + secondParts[2];
            /*else
            {
                return "";
            }*/
        }

        public ResolverResponse GetSummary(string URL)
        {
            var resp = TwitchUtil.GetStreamSummary(URL);

            if (resp == null)
                return null;

            return new ResolverResponse(resp, false);
            /*var cacheId = GetPermaName(URL);

            if (cacheId.StartsWith("t3_"))
                return new ResolverResponse(RedditUtil.GetPostSummary(cacheId), false);
            else if (cacheId.StartsWith("t1_"))
                return new ResolverResponse(RedditUtil.GetCommentSummary(cacheId), false);
            else
            {
                return new ResolverResponse(RedditUtil.GetPostSummary(RedditUtil.GetPostForMediaLink(URL), showShortLinkInsteadOfMediaLink: true), false);
            }

            return null;*/
        }

        public bool Ready(string URL)
        {
            TwitchUtil.ObtainKey();
            return TwitchUtil.ExpiryTime > DateTime.UtcNow;   
        }
    }
}