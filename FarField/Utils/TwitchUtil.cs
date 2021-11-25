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
    public class TwitchUtil
    {
        public string AccessToken { get; set; }
        public DateTime ExpiryTime { get; set; }

        private HttpClient Client { get; set; }

        public TwitchUtil()
        {
            Client = new HttpClient();
        }

        private DateTime noRetryUntil = DateTime.MinValue;

        public string GetStreamSummary(string url)
        {
            if (url.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
            {
                url = new Uri(url).AbsolutePath;
                url = url.TrimStart('/');
            }

            var search = Client.GetStringAsync($"/helix/streams?user_login={WebUtility.UrlEncode(url)}").Result;
            var parsed = JObject.Parse(search);

            if (!parsed.ContainsKey("data"))
                throw new Exception($"Nonsensical response");

            foreach (var stream in parsed["data"])
            {
                var login = stream.Value<string>("user_login");

                if (!string.Equals(login, url))
                    continue;

                var live = stream.Value<string>("type") == "live";

                if (!live)
                {
                    return $"{url} is not currently live.";
                }
                
                var started = stream.Value<string>("started_at");
                var started_pretty =
                    OsirisBase.Utilities.TimeSpanToPrettyString((DateTime.UtcNow - DateTime.Parse(started)));
                var viewers = stream.Value<int>("viewer_count");
                var game_name = stream.Value<string>("game_name");
                var title = stream.Value<string>("title");
                var pretty_name = stream.Value<string>("user_name");
                
                return $"{OsirisBase.Utilities.FormatWithColor("Twitch", 6)} livestream: \"{title}\" | {game_name} | {OsirisBase.Utilities.BetterPlural(viewers, "viewer", color: 6, bold: 1)} | {pretty_name} started streaming {started_pretty} ago.";
            }

            return null;
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
                Client.BaseAddress = new Uri("https://id.twitch.tv");
                var request = new HttpRequestMessage(HttpMethod.Post, $"/oauth2/token?client_id={Config.GetString("twitch.client")}&client_secret={Config.GetString("twitch.secret")}&grant_type=client_credentials&scope=");

                /*var byteArray =
                    new UTF8Encoding().GetBytes(
                        $"{Config.GetString("reddit.appname")}:{Config.GetString("reddit.secret")}");
                Client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                Client.DefaultRequestHeaders.UserAgent.TryParseAdd(Config.GetString("reddit.useragent"));*/

                /*var formData = new List<KeyValuePair<string, string>>();
                formData.Add(new KeyValuePair<string, string>("grant_type",
                    "https://oauth.reddit.com/grants/installed_client"));
                formData.Add(new KeyValuePair<string, string>("device_id", Config.GetString("reddit.deviceid")));

                request.Content = new FormUrlEncodedContent(formData);*/
                var response = Client.SendAsync(request).Result;
                var responseJson = JObject.Parse(response.Content.ReadAsStringAsync().Result);

                if (!responseJson.ContainsKey("access_token"))
                    throw new Exception();

                AccessToken = responseJson.Value<string>("access_token");
                ExpiryTime = DateTime.UtcNow.AddSeconds(responseJson.Value<int>("expires_in"));
                Client = new HttpClient();
                Client.BaseAddress = new Uri("https://api.twitch.tv");
                Client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", AccessToken);
                Client.DefaultRequestHeaders.TryAddWithoutValidation("Client-Id", Config.GetString("twitch.client"));
                Client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Config.GetString("twitch.useragent"));
                Console.WriteLine($"Obtained twitch token: {AccessToken}");
            }
            catch (Exception ex)
            {
                noRetryUntil = DateTime.UtcNow.AddHours(1);
                Console.WriteLine($"Failed to obtain twitch auth token");
                Console.WriteLine(ex);
            }
        }
    }
}