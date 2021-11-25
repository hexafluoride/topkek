using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HeimdallBase;

namespace FarField
{
    public class LinkResolver
    {
        public static TimedCache Cache = new TimedCache();
        public static List<IResolver> Resolvers = new List<IResolver>(); // ordered by priority

        
        
        public static void AddResolver(IResolver resolver)
        {
            Resolvers.Add(resolver);
            Console.WriteLine("Loaded resolver {0}", resolver.Name);
        }

        public static string GetTitle(string url)
        {
            int multiplier = 128;

            string result = string.Empty;
            HttpWebRequest request;
            int bytesToGet = 1024 * multiplier;
            request = WebRequest.Create(url) as HttpWebRequest;

            request.UserAgent = Config.GetString("title.useragent");

            //get first 1000 bytes
            request.AddRange(0, bytesToGet - 1);

            // the following code is alternative, you may implement the function after your needs
            StringBuilder sb = new StringBuilder();

            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    for (int i = 0; i < multiplier; i++)
                    {
                        byte[] buffer = new byte[1024];
                        int read = stream.Read(buffer, 0, 1024);
                        Array.Resize(ref buffer, read);
                        sb.Append(Encoding.UTF8.GetString(buffer));

                        string title = GetTitleFromContent(sb.ToString());

                        if (title != "-")
                            return title;
                    }
                }
            }
            
            //File.WriteAllText("./last-preview", sb.ToString());

            return "-";
        }

        public static string GetTitleFromContent(string content)
        {
            var matches = Regex.Matches(content, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase);

            if (matches.Count == 0)
                return "-";

            return string.Format("11Title: {0}", Sanitize(matches[0].Groups["Title"].Value));
        }

        public static string Sanitize(string input)
        {
            input = HttpUtility.HtmlDecode(input);
            input = input.Replace("\n", "");
            input = input.Replace("\r", "");

            input = input.Replace("http://", "");
            input = input.Replace("https://", "");

            if (input.Length > 500) // on the safe side
                input = input.Substring(0, 500) + "...";

            return input;
        }

        public static KeyValuePair<string, string> GetSummaryWithTimeout(string url, int timeout = 3000)
        {
            KeyValuePair<string, string> result = new KeyValuePair<string, string>();

            try
            {
                ManualResetEvent reset = new ManualResetEvent(false);

                Task.Factory.StartNew(
                    delegate
                    {
                        result = GetSummary(url).Result;
                        reset.Set();
                    });

                reset.WaitOne(timeout);
            }
            catch
            {
            }

            return result;
        }

        public static async Task<KeyValuePair<string, string>> GetSummary(string url, bool debug = false)
        {
            try
            {
                string id = url;

                IResolver resolver = null;
                bool valid_to_cache = true;

                for(int i = 0; i < Resolvers.Count; i++)
                {
                    resolver = Resolvers[i];

                    try
                    {
                        if (resolver == null || !resolver.Matches(url))
                        {
                            continue;
                        }

                        if (!resolver.Ready(url))
                        {
                            Console.WriteLine("Resolver {0} isn't ready yet, not caching", resolver.Name);
                            valid_to_cache = false;
                            continue;
                        }
                        
                        id = resolver.GetCacheID(url);

                        if (Cache.Get(id) != null)
                            return new KeyValuePair<string, string>(url, Cache.Get(id).Content + "(cache hit)");
                        
                        var resolver_response = resolver.GetSummary(url);

                        if (resolver_response != null && resolver_response.Summary != "-")
                        {
                            if(valid_to_cache && resolver_response.ShouldCache)
                                Cache.Add(id, resolver_response.Summary, TimedCache.DefaultExpiry);
                            return new KeyValuePair<string, string>(id, resolver_response.Summary);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                }

                if (Cache.Get(url) != null)
                    return new KeyValuePair<string, string>(url, Cache.Get(id).Content + "(cache hit)");

                var handler = new SocketsHttpHandler();
                
                if (IPAddress.TryParse(Config.GetString("title.ip"), out IPAddress bindAddr))
                {
                    handler.ConnectCallback = async (context, token) =>
                    {
                        Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                        socket.Bind(new IPEndPoint(bindAddr, 0));

                        socket.NoDelay = true;

                        try
                        {
                            await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);

                            return new NetworkStream(socket, true);
                        }
                        catch
                        {
                            socket.Dispose();

                            throw;
                        }
                    };
                }

                using (var httpClient = new HttpClient(handler))
                {
                    httpClient.MaxResponseContentBufferSize = Config.GetInt("title.max_length");

                    using (HttpRequestMessage request =
                        new HttpRequestMessage(HttpMethod.Head,
                            new Uri(url)))
                    {
                        request.Headers.Add("User-Agent", Config.GetString("title.useragent"));

                        using (HttpResponseMessage response =
                            await httpClient.SendAsync(request))
                        {
                            int length = 0;

                            if (response.Content.Headers.Contains("Content-Length"))
                            {
                                length = int.Parse(response.Content.Headers.GetValues("Content-Length").First());

                                if (length > Config.GetInt("title.max_length"))
                                    return new KeyValuePair<string, string>("", "File too big!");
                            }

                            string ext = "";

                            if (url.Contains("."))
                                ext = "." + url.Split('.').Last();

                            if (ext.Length > 5)
                                ext = "";

                            string mime = GetMimeType(response);
                            var type = GetType(mime);
                            var ext_type = GetTypeByExt(ext);

                            if ((type == LinkType.Generic || type != ext_type) && ext_type != LinkType.Generic)
                                type = ext_type;

                            switch (type)
                            {
                                case LinkType.Html:
                                    string title = "";

                                    title = GetTitle(url);

                                    if (valid_to_cache)
                                        Cache.Add(url, title, TimedCache.DefaultExpiry);

                                    return new KeyValuePair<string, string>(url, title);
                                case LinkType.Image:
                                {
                                    string msg = "";

                                    using (var resp = await httpClient.GetAsync(url))
                                    {
                                        byte[] data = await resp.Content.ReadAsByteArrayAsync();
                                        MemoryStream ms = new MemoryStream(data);

                                        Bitmap bmp = new Bitmap(ms);

                                        string hash = GetHash(data);
                                        //string imgtype = mime.Split('/')[1].ToUpper();
                                        string imgtype = bmp.RawFormat.ToString().ToUpperInvariant();

                                        if (length != 0)
                                            msg = string.Format("11{0} image({1}, {2}x{3})", imgtype,
                                                GetBoldLength(length),
                                                bmp.Width, bmp.Height);
                                        else
                                            msg = string.Format("11{0} image({1}x{2})", imgtype, bmp.Width,
                                                bmp.Height);

                                        if (valid_to_cache)
                                            Cache.Add(url, msg, TimedCache.DefaultExpiry);
                                        return new KeyValuePair<string, string>(url, msg);
                                    }
                                }
                                case LinkType.Video:
                                case LinkType.Audio:
                                //{
                                //    var resp = await httpClient.GetAsync(url);
                                //    string temp = Path.GetTempFileName();
                                //    byte[] data = await resp.Content.ReadAsByteArrayAsync();
                                //    File.WriteAllBytes(temp, data);
                                //    string ret = string.Format(MediaInfo.GetMediaInfo(temp), GetBoldLength(length));
                                //    File.Delete(temp);

                                //    Cache.Add(url, ret, TimedCache.DefaultExpiry);
                                //    return new KeyValuePair<string, string>(url, ret);
                                //}
                                case LinkType.Generic:
                                    if (length != 0)
                                    {
                                        string ret = "";

                                        ret = string.Format("11{0}, {1}", mime, GetBoldLength(length));

                                        if (valid_to_cache)
                                            Cache.Add(url, ret, TimedCache.DefaultExpiry);

                                        return new KeyValuePair<string, string>(url, ret);
                                    }

                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                }

                return new KeyValuePair<string, string>("", "-");
            }
            catch
            {
                throw;
            }
        }

        public static string GetHash(byte[] data)
        {
            System.Security.Cryptography.SHA1CryptoServiceProvider sha = new System.Security.Cryptography.SHA1CryptoServiceProvider();
            return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLower();
        }

        public static string GetHash(string path)
        {
            return GetHash(File.ReadAllBytes(path));
        }

        static Random rnd = new Random();

        public static string GetProperLength(long length)
        {
            double K = 1024;
            double M = K * K;
            double G = K * M;

            if (length > G)
                return (length / G).ToString("0.00") + " GB";

            if (length > M)
                return (length / M).ToString("0.00") + " MB";

            if (length > K)
                return (length / K).ToString("0.00") + " KB";

            return length + " B";
        }

        public static string GetLength(long length)
        {
            Dictionary<string, double> Units = new Dictionary<string, double>()
            {
                { " inches of uncompressed DDS-2 tape({0})", 846667d },
                { " seconds of Bell 202 audio({0})", 150d },
                { " square millimeters of CD({0})", 174000d },
                { " times jquery-3.1.0.min.js({0})", 86351d },
                { " copies of Carl Sagan on Wikipedia({0})", 425079d },
                { " tweets holding Base64-encoded data({0})", 105d }
            };

            var pair = Units.ElementAt(rnd.Next(Units.Count));

            return string.Format((length / pair.Value).ToString("0.00") + pair.Key, "" + GetProperLength(length) + "");
        }

        public static string GetBoldLength(long length)
        {
            string str_length = GetLength(length);
            string num = str_length.Split(' ')[0];
            string unit = string.Join(" ", str_length.Split(' ').Skip(1).ToArray());

            return string.Format("{0} {1}", num, unit);
        }

        public static string GetMimeType(HttpResponseMessage response)
        {
            if (response.Content.Headers.Contains("Content-Type"))
            {
                string ret = response.Content.Headers.GetValues("Content-Type").ToList()[0].ToLower();
                if (ret.Contains(';'))
                    ret = ret.Split(';')[0];

                return ret;
            }

            return "-";
        }

        public static LinkType GetType(string mime)
        {
            switch (mime)
            {
                case "image/tiff":
                case "image/png":
                case "image/gif":
                case "image/jpeg":
                case "image/jpg":
                case "image/bmp":
                    return LinkType.Image;
                case "text/html":
                case "application/xhtml+xml":
                    return LinkType.Html;
                case "audio/aac":
                case "audio/mp4":
                case "audio/mpeg":
                case "audio/ogg":
                case "audio/wav":
                case "audio/webm":
                case "audio/flac":
                    return LinkType.Audio;
                case "video/mp4":
                case "video/ogg":
                case "video/webm":
                    return LinkType.Video;
                default:
                    Console.WriteLine("Unrecognized mime type: {0}", mime);
                    return LinkType.Generic;
            }
        }

        public static LinkType GetTypeByExt(string extension)
        {
            switch (extension)
            {
                case ".tiff":
                case ".png":
                case ".gif":
                case ".jpeg":
                case ".jpg":
                case ".bmp":
                    return LinkType.Image;
                case ".html":
                case ".htm":
                case ".xhtml":
                case ".xhtm":
                    return LinkType.Html;
                case ".aac":
                case ".m4a":
                case ".mpeg":
                case ".ogg":
                case ".wav":
                case ".flac":
                    return LinkType.Audio;
                case ".webm":
                case ".mp4":
                    return LinkType.Video;
                default:
                    return LinkType.Generic;
            }
        }
    }

    public class ResolverResponse
    {
        public bool ShouldCache { get; set; }
        public string Summary { get; set; }

        public ResolverResponse(string summary, bool cache = true)
        {
            Summary = summary;
            ShouldCache = cache;
        }
    }

    public interface IResolver
    {
        string Name { get; }

        bool Matches(string URL);
        bool Ready(string URL);
        ResolverResponse GetSummary(string URL);
        string GetCacheID(string URL);
    }

    public enum LinkType
    {
        Html, Image, Video, Audio, Generic
    }
}