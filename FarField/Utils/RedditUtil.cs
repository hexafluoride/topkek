using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public class RedditUtil
    {
        public string AccessToken { get; set; }
        public DateTime ExpiryTime { get; set; }

        private HttpClient Client { get; set; }

        public RedditUtil()
        {
            Client = new HttpClient();
        }

        private DateTime noRetryUntil = DateTime.MinValue;

        private JObject GetPost(string postId)
        {
            ObtainKey();
            if (!postId.StartsWith("t3_"))
                postId = "t3_" + postId;
            
            var cached = LinkResolver.Cache.Get($"reddit:{postId}");

            if (cached != null)
                return JObject.Parse(cached.Content);

            try
            {
                var postInfo = Client.GetStringAsync($"/api/info?id={postId}&raw_json=true").Result;
                var parsedResponse = JObject.Parse(postInfo);
                var child = (JObject) parsedResponse["data"]["children"][0]["data"];
                LinkResolver.Cache.Add($"reddit:{postId}", child.ToString(), TimeSpan.FromHours(1));
                return child;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        private JObject GetComment(string commentId)
        {
            ObtainKey();
            if (!commentId.StartsWith("t1_"))
                commentId = "t1_" + commentId;

            var cached = LinkResolver.Cache.Get($"reddit:{commentId}");

            if (cached != null)
                return JObject.Parse(cached.Content);
            
            try
            {
                var resp = Client.GetAsync($"/api/info?id={commentId}&raw_json=true").Result;
                var postInfo = resp.Content.ReadAsStringAsync().Result;
                var parsedResponse = JObject.Parse(postInfo);
                var child = (JObject) parsedResponse["data"]["children"][0]["data"];
                LinkResolver.Cache.Add($"reddit:{commentId}", child.ToString(), TimeSpan.FromHours(1));
                return child;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        public string GetPostForMediaLink(string url)
        {
            ObtainKey();
            var cached = LinkResolver.Cache.Get($"reddit:{url}");
            if (cached != null)
                return cached.Content;
            
            try
            {
                var resp = Client.GetAsync($"/api/info?url={WebUtility.UrlEncode(url)}&raw_json=true").Result;
                var postInfo = resp.Content.ReadAsStringAsync().Result;
                var parsedResponse = JObject.Parse(postInfo);
                var child = (JObject) parsedResponse["data"]["children"][0]["data"];
                var postId = child.Value<string>("name");

                if (!postId.StartsWith("t3_"))
                    throw new Exception($"Found unexpected name {postId}");
                
                LinkResolver.Cache.Add($"reddit:{url}", postId, TimeSpan.FromHours(1));
                LinkResolver.Cache.Add($"reddit:postId", child.ToString(), TimeSpan.FromHours(1));
                return postId;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }

        public string GetPostSummary(string postId, bool showShortLinkInsteadOfMediaLink = false)
        {
            var budget = 250;
            
            try
            {
                var child = GetPost(postId);

                if (child == null)
                    return "-";

                budget -= child.Value<string>("title").Length;

                if (budget <= 0)
                    budget += 50;
                
                var juice = child.Value<string>("selftext");
                if (string.IsNullOrWhiteSpace(juice))
                {
                    if (showShortLinkInsteadOfMediaLink)
                    {
                        var shortPostId = postId;
                        if (shortPostId[2] == '_')
                            shortPostId = postId.Substring(3);
                        juice = $"https://redd.it/{shortPostId}";
                    }
                    else
                    {
                        juice = child.Value<string>("url");   
                    }
                }
                else
                {
                    if (juice.Length > budget)
                        juice = juice.Substring(0, budget) + "...";

                    juice = $"\"{juice}\"";
                }

                var createdSecondsAgo = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds -
                                        child.Value<double>("created_utc");

                string format =
                    $"\"{child.Value<string>("title")}\" on {child.Value<string>("subreddit_name_prefixed")} by u/{child.Value<string>("author")}: {juice}";

                format += $" | {OsirisBase.Utilities.BetterPlural(child.Value<int>("score"), "point", color: 7, bold: 1)} ({child.Value<double>("upvote_ratio"):P0} upvoted)";

                if (child.Value<int>("num_comments") > 0)
                {
                    format +=
                        $" | {OsirisBase.Utilities.BetterPlural(child.Value<int>("num_comments"), "comment", color: 72, bold: 1)}";
                }

                format +=
                    $" | {OsirisBase.Utilities.TimeSpanToPrettyString(TimeSpan.FromSeconds(createdSecondsAgo))} ago";

                return format;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return "-";
            }
        }

        public string GetCommentSummary(string commentId)
        {
            var budget = 300;
            
            try 
            {
                var child = GetComment(commentId);
                if (child == null)
                    return "-";
                
                var post = GetPost(child.Value<string>("link_id"));
                var postTitle = post.Value<string>("title");
                
                var juice = child.Value<string>("body").Replace("\n", "");

                budget -= postTitle.Length;
                if (budget <= 0)
                    budget += 50;

                if (juice.Length > budget)
                    juice = juice.Substring(0, budget) + "...";

                var createdSecondsAgo = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds -
                                        child.Value<double>("created_utc");

                string format = $"Comment on {post.Value<string>("subreddit_name_prefixed")} post \"{postTitle}\" by u/{child.Value<string>("author")}: \"{juice}\"";

                format += $" | {OsirisBase.Utilities.BetterPlural(child.Value<int>("score"), "point", color: 7, bold: 1)}";
                format +=
                    $" | {OsirisBase.Utilities.TimeSpanToPrettyString(TimeSpan.FromSeconds(createdSecondsAgo))} ago";

                return format;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return "-";
            }
        }

        public void ObtainKey(bool checkNeeded = true)
        {
            if (DateTime.UtcNow < noRetryUntil)
                return;

            if (checkNeeded && DateTime.UtcNow < ExpiryTime)
                return;
            
            try
            {
                Client = new HttpClient();
                Client.BaseAddress = new Uri("https://www.reddit.com");
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/access_token");

                var byteArray =
                    new UTF8Encoding().GetBytes(
                        $"{Config.GetString("reddit.appname")}:{Config.GetString("reddit.secret")}");
                Client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                Client.DefaultRequestHeaders.UserAgent.TryParseAdd(Config.GetString("reddit.useragent"));

                var formData = new List<KeyValuePair<string, string>>();
                formData.Add(new KeyValuePair<string, string>("grant_type",
                    "https://oauth.reddit.com/grants/installed_client"));
                formData.Add(new KeyValuePair<string, string>("device_id", Config.GetString("reddit.deviceid")));

                request.Content = new FormUrlEncodedContent(formData);
                var response = Client.SendAsync(request).Result;
                var responseJson = JObject.Parse(response.Content.ReadAsStringAsync().Result);

                if (!responseJson.ContainsKey("access_token"))
                    throw new Exception();

                AccessToken = responseJson.Value<string>("access_token");
                ExpiryTime = DateTime.UtcNow.AddSeconds(responseJson.Value<int>("expires_in"));
                Client = new HttpClient();
                Client.BaseAddress = new Uri("https://oauth.reddit.com");
                Client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("bearer", AccessToken);
                Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Config.GetString("reddit.useragent"));
                Console.WriteLine($"Obtained reddit token: {AccessToken}");
            }
            catch (Exception ex)
            {
                noRetryUntil = DateTime.UtcNow.AddHours(1);
                Console.WriteLine($"Failed to obtain reddit auth token");
                Console.WriteLine(ex);
            }
        }
    }
}