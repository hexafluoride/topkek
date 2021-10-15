
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HeimdallBase;
using Tweetinvi;
using Tweetinvi.Core;
using Tweetinvi.Models;

namespace FarField
{
    public class TwitterUtil
    {
        TwitterCredentials Credentials { get; set; }
        Random Random { get; set; }

        public TwitterUtil()
        {
        }

        public void Init()
        {
            Credentials = new TwitterCredentials(Config.GetString("twitter.consumer.key"), Config.GetString("twitter.consumer.secret"), Config.GetString("twitter.user.token"), Config.GetString("twitter.user.secret"));
            Console.WriteLine("Setting up Twitter credentials as:");

            Console.WriteLine("\t{0}", Credentials.ConsumerKey);
            Console.WriteLine("\t{0}", Credentials.ConsumerSecret);
            Console.WriteLine("\t{0}", Credentials.AccessToken);
            Console.WriteLine("\t{0}", Credentials.AccessTokenSecret);

            Auth.SetCredentials(Credentials);
            Random = new Random();

            TweetinviConfig.CurrentThreadSettings.TweetMode = TweetMode.Extended;
            TweetinviConfig.ApplicationSettings.TweetMode = TweetMode.Extended;
        }

        public long GetTweetId(string url)
        {
            var tweetId = url.Split("/status/")[1];

            if (tweetId.Contains('?'))
                tweetId = tweetId.Substring(0, tweetId.IndexOf('?'));

            if (!long.TryParse(tweetId, out long tweetIdNumeric))
                return -1;

            return tweetIdNumeric;
        }

        public string GetTweetSummary(string url) => GetTweetSummary(GetTweetId(url));

        public string GetTweetSummary(ITweet tweet)
        {
            var handle = tweet.CreatedBy.ScreenName;

            /*if (handle.StartsWith("https://twitter.com/"))
                handle = handle.Substring("https://twitter.com/".Length);*/

            var format = $"11Tweet from {tweet.CreatedBy.Name} ({(tweet.CreatedBy.Verified?"✓":"")}@{handle}, {OsirisBase.Utilities.TimeSpanToPrettyString(DateTime.Now - tweet.CreatedAt)} ago): \"​{tweet.FullText}​\"";

            if (tweet.FavoriteCount > 0)
                format += $" | {OsirisBase.Utilities.BetterPlural(tweet.FavoriteCount, "like", bold: 1, color: 13)}";

            if (tweet.RetweetCount > 0)
                format += $" | {OsirisBase.Utilities.BetterPlural(tweet.RetweetCount, "retweet", bold: 1, color: 12)}";
            
            return format;
        }
        public string GetTweetSummary(long tweetId)
        {
            var tweet = Tweet.GetTweet(tweetId);
            return GetTweetSummary(tweet);
        }

        public string GetSearchResult(string query)
        {
            try
            {
                TweetinviConfig.CurrentThreadSettings.TweetMode = TweetMode.Extended;
                TweetinviConfig.ApplicationSettings.TweetMode = TweetMode.Extended;
                string cache_id = "twitter-search:" + query.ToLower().Trim();

                var item = LinkResolver.Cache.Get(cache_id);

                if(item != null)
                {
                    var items = item.Content.Split('\n');
                    return items[Random.Next(items.Length)];
                }

                Func<ITweet, string> transform = (t) => { return $"{GetTweetSummary(t)} | {t.Url.Replace(t.CreatedBy.ScreenName, "i")}"; };

                if (Config.GetString("twitter.output") == "classic")
                    transform = (t) => { return t.FullText.Replace("\n", " "); };

                var results = Search.SearchTweets(query).Where(t => !t.IsRetweet).Select(transform).ToList();
                LinkResolver.Cache.Add(cache_id, string.Join("\n", results), TimedCache.DefaultExpiry);
                
                return results[Random.Next(results.Count)];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            return null;
        }
    }
}