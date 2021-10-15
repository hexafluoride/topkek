using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using HeimdallBase;

namespace FarField
{
    public class YoutubeLinkResolver : IResolver
    {
        public string Name => "youtube";
        public YoutubeUtil YoutubeUtil { get; set; }
        
        public YoutubeLinkResolver(YoutubeUtil util)
        {
            YoutubeUtil = util;
            util.LoadKeys();
        }
        
        public bool Matches(string URL) => YoutubeUtil.IsYouTubeLink(URL);
        public string GetCacheID(string URL) => "youtube:" + YoutubeUtil.GetVideoID(URL);
        public ResolverResponse GetSummary(string URL) => YoutubeUtil.GetSummary(URL);
        public bool Ready(string URL) => YoutubeUtil.Service != null;
    }
    
    public class YoutubeUtil
    {
        public YouTubeService Service;
        
        public bool IsYouTubeLink(string url)
        {
            return
                url.StartsWith("http://youtube.com") ||
                url.StartsWith("https://youtube.com") ||
                url.StartsWith("http://www.youtube.com") ||
                url.StartsWith("https://www.youtube.com") ||
                url.StartsWith("http://youtu.be") ||
                url.StartsWith("https://youtu.be") ||
                url.StartsWith("http://hooktube.com") ||
                url.StartsWith("https://hooktube.com");
        }

        public string GetVideoID(string url)
        {
            if (url.Contains("youtu.be") && !url.Contains("feature"))
            {
                string[] parts = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 3)
                    return "-";

                if (parts[2].Contains('?'))
                    return parts[2].Split('?')[0];

                return parts[2];
            }
            else
            {
                string[] parts = url.Split(new[] { "v=" }, StringSplitOptions.RemoveEmptyEntries);

                if (!parts.Any())
                    return "-";

                if (parts[1].Contains("&"))
                    return parts[1].Split('&')[0];

                return parts[1];
            }
        }

        public void LoadKeys()
        {
            Service = new YouTubeService(new BaseClientService.Initializer()
            {
                ApiKey = Config.GetString("youtube.key"),
                ApplicationName = Config.GetString("youtube.appname")
            });
        }

        public string Search(string search, bool link_only = false)
        {
            if (Service == null)
                LoadKeys();

            var req = Service.Search.List("snippet");
            req.Q = search;

            var response = req.Execute();

            string id = "";

            foreach (var item in response.Items)
            {
                if (item.Kind == "youtube#searchResult" && item.Id.Kind == "youtube#video")
                {
                    id = item.Id.VideoId;
                    break;
                }
            }

            if (id != "")
            {
                if (link_only)
                    return $"https://youtu.be/{id}";
                return GetSummary(id)?.Summary?.Replace("You4Tube livestream:", "You4Tube video:")?.Replace("You4Tube video:", string.Format("https://youtu.be/{0} |", id)) ?? "-";
            }

            return "-";
        }

        public ResolverResponse GetSummary(string ID)
        {
            if (ID.StartsWith("http"))
                ID = GetVideoID(ID);

            if (Service == null)
                LoadKeys();

            try
            {
                var req = Service.Videos.List("snippet,statistics,contentDetails,liveStreamingDetails");
                req.Id = ID;

                var response = req.Execute();

                string duration = "";
                string name = "";
                string uploader = "";
                string likes = "";
                string dislikes = "";
                string views = "";
                string uploaded = "";

                bool live = false;
                bool success = false;

                foreach (var item in response.Items)
                {
                    if (item.Kind == "youtube#video")
                    {
                        try
                        {
                            duration = XmlConvert.ToTimeSpan(item.ContentDetails.Duration.Trim()).ToString("hh\\:mm\\:ss");
                            name = item.Snippet.Title;
                            uploader = item.Snippet.ChannelTitle;
                            likes = ((ulong)item.Statistics.LikeCount).ToString("N0");
                            dislikes = ((ulong)item.Statistics.DislikeCount).ToString("N0");
                            views = ((ulong)item.Statistics.ViewCount).ToString("N0");
                            uploaded = ((DateTime)item.Snippet.PublishedAt).ToShortDateString();
                            if (item.Snippet.LiveBroadcastContent != "none")
                            {
                                live = true;
                                views = ((ulong)item.LiveStreamingDetails.ConcurrentViewers).ToString("N0");
                                uploaded = NiceString(DateTime.Now - (DateTime)item.LiveStreamingDetails.ActualStartTime);
                            }
                        }
                        catch
                        {

                        }

                        success = true;
                        break;
                    }
                }

                if (!success)
                    return null;

                if (live)
                    return new ResolverResponse(string.Format(
                    "You4Tube livestream: \"{0}\" | Started by 11{1} {2} ago | {3} watching | 3{4} likes/4{5} dislikes",
                    name, uploader, uploaded, views, likes, dislikes), false);

                return new ResolverResponse(string.Format(
                    "You4Tube video: \"{0}\" | Uploaded by 11{1} on {2} | {3} long | {4} views | 3{5} likes/4{6} dislikes",
                    name, uploader, uploaded, duration, views, likes, dislikes));
            }
            catch
            {
                return null;
            }
        }
        
        public static string ConditionalPlural(double val, string noun)
        {
            int c = (int)val;

            if (c == 1)
                return c.ToString() + " " + noun;

            return c.ToString() + " " + noun + "s";
        }

        public static string NiceString(TimeSpan span)
        {
            if (span.TotalDays > 1)
                return ConditionalPlural(span.TotalDays, "day");

            if (span.TotalHours > 1)
                return ConditionalPlural(span.TotalHours, "hour");

            if (span.TotalMinutes > 1)
                return ConditionalPlural(span.TotalMinutes, "minute");

            if (span.TotalSeconds > 1)
                return ConditionalPlural(span.TotalSeconds, "second");

            return span.ToString();
        }

        public static TimeSpan FuckingRetardedStandards(string iso)
        {
            // "PT3M44S"

            TimeSpan ret = new TimeSpan();

            iso = iso.Replace("PT", "");

            var parts = iso.Split(new[] { 'H', 'M', 'S' }, StringSplitOptions.RemoveEmptyEntries);

            parts = parts.Reverse().ToArray();

            return new TimeSpan((parts.Length > 2 ? int.Parse(parts[2]) : 0), (parts.Length > 1 ? int.Parse(parts[1]) : 0), int.Parse(parts[0]));
        }
    }
}