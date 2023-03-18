using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public class SearchUtil
    { 
        public static WebClient Client = new WebClient();
        public static Random Random = new Random();

        public static string SearchTwitter(string text)
        {
            return "";
        }

        public static string SearchImages(string text, bool random, bool gif, bool yandex = false)
        {
            //Client.
            string id = GetCacheString(text, gif ? "gif" : "im");
            var item = FarField.Cache.Get(id);

            if (item != null)
            {
                var temp_list = item.Content.Split('\n');

                if (random)
                {
                    int rand = Random.Next(temp_list.Length);
                    Console.WriteLine("random pick: {0} out of {1}", rand, temp_list.Length);
                    return temp_list[Random.Next(temp_list.Length)];
                }
                else
                {
                    return temp_list[0];
                }
            }

            Console.WriteLine("Cache miss");
            var list = yandex ? _SearchImagesYandex(text, random, gif) : _SearchImagesGoogle(text, random, gif);

            string res = string.Join("\n", list);

            Console.WriteLine(list.Count);

            if (list.Count != 0)
                FarField.Cache.Add(id, res, TimedCache.DefaultExpiry);
            else
                return "-";

            if (random)
            {
                return list[Random.Next(list.Count)];
            }
            else
            {
                return list[0];
            }
        }

        static List<string> _SearchImagesGoogle(string text, bool random, bool gif)
        {
            Client.Headers["User-Agent"] = "Mozilla/5.0 (X11; Linux x86_64; rv:106.0) Gecko/20100101 Firefox/106.0";//Config.GetString("search.useragent");

            string response = Client.DownloadString(BuildQuery(text, true, gif));
            
            var callbackMarker = "initDataCallback";
            var callbackKey = "({key: 'ds:1'";
            if (!response.Contains(callbackMarker + callbackKey))
            {
                return new List<string>();
            }

            var jsonResponse =
                response.Substring(response.IndexOf(callbackMarker + callbackKey) + callbackMarker.Length + 1);

            jsonResponse = jsonResponse.Substring(0, jsonResponse.IndexOf("</script>")).Trim();

            if (jsonResponse.EndsWith(");"))
                jsonResponse = jsonResponse.Substring(0, jsonResponse.Length - 2);

            var parsedJsonResponse = JObject.Parse(jsonResponse);

            var arr = (JArray)parsedJsonResponse["data"];
            var toTraverse = new List<JToken>() { arr };
            var results = new List<string>();

            while (toTraverse.Any())
            {
                var nextToken = toTraverse[0];
                toTraverse.RemoveAt(0);

                if (nextToken is JArray nextArr)
                {
                    if (nextArr.Count > 4)
                    {
                        if (nextArr[0].Type != JTokenType.Integer)
                            goto cont;

                        if (nextArr[0].Value<int>() != 0)
                            goto cont;

                        if (nextArr[2].Type != JTokenType.Array || nextArr[3].Type != JTokenType.Array)
                            goto cont;

                        var firstArr = nextArr[2] as JArray;
                        var secondArr = nextArr[3] as JArray;

                        if (firstArr.Count != 3 || secondArr.Count != 3)
                            goto cont;

                        if (firstArr[0].Type != JTokenType.String || secondArr[0].Type != JTokenType.String)
                            goto cont;

                        if (!firstArr[0].Value<string>().Contains("gstatic.com/images?q="))
                            goto cont;

                        results.Add(secondArr[0].Value<string>());
                        continue;
                    }

                    cont:
                    foreach (var elem in nextArr)
                    {
                        if (!(elem is JArray))
                        {
                            if (elem is JObject elemObj)
                            {
                                foreach (var child in elemObj)
                                {
                                    if (child.Value?.Type == JTokenType.Array || child.Value?.Type == JTokenType.Object)
                                        toTraverse.Add(child.Value);
                                }
                            }
                            continue;
                        }

                        toTraverse.Add(elem as JArray);
                    }
                }
                else if (nextToken is JObject nextObj)
                {
                    foreach (var child in nextObj)
                    {
                        if (child.Value?.Type == JTokenType.Array || child.Value?.Type == JTokenType.Object)
                            toTraverse.Add(child.Value);
                    }
                }
            }

            return results;
            
            var matches = Regex.Matches(response, "\"ou\":\"(http.*?)\",\"ow\"");
            return matches.Cast<Match>().Where(m => m.Groups.Count >= 2 && !m.Groups[1].Value.Contains("ggpht") && !m.Groups[1].Value.Contains("fjcdn.com")).Select(m => Regex.Replace(
    m.Groups[1].ToString(),
    @"\\[Uu]([0-9A-Fa-f]{4})",
    k => char.ToString(
        (char)ushort.Parse(k.Groups[1].Value, NumberStyles.AllowHexSpecifier)))).ToList();
        }

        static List<string> _SearchImagesYandex(string text, bool random, bool gif)
        {
            Client.Headers["User-Agent"] = Config.GetString("search.useragent");

            string response = Client.DownloadString(BuildQuery(text, true, gif, true));
            //File.WriteAllText("./yandex", response);
            var matches = Regex.Matches(response, "data-bem='(?'json'.*?yandex\\.com\\%3Bimages.*?)'");

            Console.WriteLine("m: {0}", matches.Count);

            return matches.Cast<Match>().Select(m => JObject.Parse(m.Groups[1].ToString())).Where(obj => obj.ContainsKey("serp-item") && obj.ContainsKey("dups")).Select(obj => ((JArray)obj["dups"])[0].Value<string>("url")).ToList();

            return matches.Cast<Match>().Where(m => m.Groups.Count >= 2 && !m.Groups[1].Value.Contains("ggpht") && !m.Groups[1].Value.Contains("fjcdn.com")).Select(m => Regex.Replace(
    m.Groups[1].ToString(),
    @"\\[Uu]([0-9A-Fa-f]{4})",
    k => char.ToString(
        (char)ushort.Parse(k.Groups[1].Value, NumberStyles.AllowHexSpecifier)))).ToList();
        }

        public static string SearchLinks(string text, bool random)
        {
            string id = GetCacheString(text, "urls");
            var item = FarField.Cache.Get(id);

            if (item != null)
            {
                var temp_list = item.Content.Split('\n');

                if (random)
                {
                    return temp_list[Random.Next(temp_list.Length)];
                }
                else
                {
                    return temp_list[0];
                }
            }

            Console.WriteLine("Cache miss");
            Client.Headers["User-Agent"] = Config.GetString("search.useragent");

            string response = Client.DownloadString(BuildQuery(text, false, false));
            var matches = Regex.Matches(response, "href=\"(http.*?)\"");
            var list = matches.Cast<Match>().Where(m => m.Groups.Count >= 2 && !m.Groups[1].Value.Contains("webcache.googleusercontent.com") && !m.Groups[1].Value.Contains("google.com") && !m.Groups[1].Value.Contains("google.nl")).Select(m => m.Groups[1].ToString()).ToList();

            string res = string.Join("\n", list);

            Console.WriteLine(list.Count);
            if (list.Count != 0)
                FarField.Cache.Add(id, res, TimedCache.DefaultExpiry);
            else
                return "-";

            if (random)
            {
                return list[Random.Next(list.Count)];
            }
            else
            {
                return list[0];
            }
        }

        public static string SearchLinksDdg(string text, bool random)
        {
            string id = GetCacheString(text, "urls-ddg");
            var item = FarField.Cache.Get(id);

            if (item != null)
            {
                var temp_list = item.Content.Split('\n');

                if (random)
                {
                    return temp_list[Random.Next(temp_list.Length)];
                }
                else
                {
                    return temp_list[0];
                }
            }

            Console.WriteLine("Cache miss");
            Client.Headers["User-Agent"] = Config.GetString("search.useragent");

            string response = Client.DownloadString(string.Format("https://duckduckgo.com/html?q={0}", WebUtility.UrlEncode(text)));
            var matches = Regex.Matches(response, "<a rel=\"nofollow\" href=\"(http.*?)\"");
            var list = matches.Cast<Match>().Where(m => 
                m.Groups.Count >= 2 && 
                !m.Groups[1].Value.Contains("/feedback.html")).Select(m => m.Groups[1].ToString()).Distinct().ToList();

            string res = string.Join("\n", list);

            Console.WriteLine(list.Count);
            if (list.Count != 0)
                FarField.Cache.Add(id, res, TimedCache.DefaultExpiry);
            else
                return "-";

            if (random)
            {
                return list[Random.Next(list.Count)];
            }
            else
            {
                return list[0];
            }
        }

        public static string BuildQuery(string text, bool im, bool gif, bool yandex = false)
        {
            if (im)
            {
                if (yandex)
                    return string.Format("http://yandex.com/images/search?{0}text={1}", gif ? "itype=gifan&" : "", WebUtility.UrlEncode(text));
                else
                    return string.Format("http://google.com/search?hl=en&tbm=isch&q={0}{1}", WebUtility.UrlEncode(text), gif ? "&tbs=itp:animated" : "");
            }
            else
                return string.Format("http://google.com/search?hl=en&q={0}", WebUtility.UrlEncode(text));
        }

        public static string GetCacheString(string query, string type = "im")
        {
            return string.Format("?search:{0}:{1}", query, type);
        }
    }
}