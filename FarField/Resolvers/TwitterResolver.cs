using System.Linq;
using Tweetinvi;

namespace FarField
{
    public class TwitterResolver : IResolver
    {
        public string Name => "twitter";
        public TwitterUtil TwitterUtil { get; set; }
    
        public TwitterResolver(TwitterUtil util)
        {
            TwitterUtil = util;
            util.Init();
        }
    
        public bool Matches(string URL) => URL.Contains("twitter.com") && URL.Contains("/status/");
        public string GetCacheID(string URL) => "twitter:" + TwitterUtil.GetTweetId(URL);
        public ResolverResponse GetSummary(string URL) => new ResolverResponse(TwitterUtil.GetTweetSummary(URL), true);
        public bool Ready(string URL) => Auth.Credentials != default;
    }
}