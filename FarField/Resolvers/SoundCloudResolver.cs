using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HeimdallBase;

namespace FarField
{
    public class SoundCloudResolver : IResolver
    {
        public string Name
        {
            get
            {
                return "soundcloud";
            }
        }

        public WebClient Client = new WebClient();

        public SoundCloudResolver()
        {
            Client.Headers["User-Agent"] = Config.GetString("title.useragent");
        }

        public bool Matches(string url)
        {
            return url.StartsWith("http://soundcloud.com") ||
                url.StartsWith("https://soundcloud.com");
        }

        public string GetCacheID(string url)
        {
            Uri uri = new Uri(url);
            return "soundcloud:" + uri.AbsolutePath;
        }

        public string GetTitleFromContent(string content)
        {
            string layout = "07SoundCloud track: {0} by {1} | 11{2} long | 3{3} likes | 07{4} comments";

            var match = Regex.Match(content, "<a itemprop=\"url\".*?>(.*?)<\\/a>");

            if (!match.Success || match.Groups.Count < 2)
                return "-";

            string name = match.Groups[1].Value;

            match = Regex.Match(content, "<div itemscope itemprop=\"byArtist\" itemtype=\"http:\\/\\/schema.org\\/MusicGroup\"><meta itemprop=\"name\" content=\"(.*?)\" \\/><meta itemprop=\"url\" content=\"(.*?)\" \\/><\\/div>");

            if (!match.Success || match.Groups.Count < 2)
                return "-";

            string author = match.Groups[1].Value;

            match = Regex.Match(content, "<meta itemprop=\"interactionCount\" content=\"UserLikes:(.*?)\" \\/>");

            if (!match.Success || match.Groups.Count < 2)
                return "-";

            string likes = match.Groups[1].Value;

            match = Regex.Match(content, "<meta itemprop=\"interactionCount\" content=\"UserComments:(.*?)\" \\/>");

            if (!match.Success || match.Groups.Count < 2)
                return "-";

            string comments = match.Groups[1].Value;

            match = Regex.Match(content, "<meta itemprop=\"duration\" content=\"(.*?)\" \\/>");

            if (!match.Success || match.Groups.Count < 2)
                return "-";

            var duration_dt = XmlConvert.ToTimeSpan(match.Groups[1].Value);
            string duration = ((int)duration_dt.TotalMinutes).ToString() + ":" + duration_dt.Seconds.ToString("00");

            return string.Format(layout, name, author, duration, likes, comments);
        }

        public ResolverResponse GetSummary(string url)
        {
            int multiplier = 16;

            string result = string.Empty;
            HttpWebRequest request;
            int bytesToGet = 32768 * multiplier;
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
                        byte[] buffer = new byte[32768];
                        int read = stream.Read(buffer, 0, 32768);
                        Array.Resize(ref buffer, read);
                        sb.Append(Encoding.UTF8.GetString(buffer));

                        string title = GetTitleFromContent(sb.ToString());

                        if (title != "-")
                            return new ResolverResponse(title);
                    }
                }
            }

            throw new Exception("Couldn't retrieve title");
        }

        public bool Ready(string URL)
        {
            return true;
        }
    }
}