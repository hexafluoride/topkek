using System;

namespace FarField
{
    public class RedditResolver : IResolver
    {
        public string Name => "reddit";
        public RedditUtil RedditUtil { get; set; }
    
        public RedditResolver(RedditUtil util)
        {
            RedditUtil = util;
            util.ObtainKey();
        }
    
        public bool Matches(string URL) => (URL.Contains("reddit.com") && URL.Contains("/comments/")) || URL.Contains("i.redd.it") || URL.Contains("v.redd.it");
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
            var cacheId = GetPermaName(URL);

            if (cacheId.StartsWith("t3_"))
                return new ResolverResponse(RedditUtil.GetPostSummary(cacheId), false);
            else if (cacheId.StartsWith("t1_"))
                return new ResolverResponse(RedditUtil.GetCommentSummary(cacheId), false);
            else
            {
                return new ResolverResponse(RedditUtil.GetPostSummary(RedditUtil.GetPostForMediaLink(URL), showShortLinkInsteadOfMediaLink: true), false);
            }

            return null;
        }

        public bool Ready(string URL)
        {
            RedditUtil.ObtainKey();
            return RedditUtil.ExpiryTime > DateTime.UtcNow;   
        }
    }
}