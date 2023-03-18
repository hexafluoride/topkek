using System;
using System.Threading;
using System.Threading.Tasks;

namespace FarField
{
    public partial class FarField
    {
        bool SearchEnabled(string source)
        {
            var trimmedSource = source.Contains('/') ? source.Substring(0, source.IndexOf('/')) : source;
            return !GetUserDataForSourceAndNick<bool>(source, "", "search.disabled") && !GetUserDataForSourceAndNick<bool>(trimmedSource, "", "search.disabled");
        }
    
        void TwitterSearch(string args, string source, string n)
        {
            if (!SearchEnabled(source))
                return;
            
            args = args.Substring("?tw".Length).Trim();

            var tweet = TwitterUtil.GetSearchResult(args);

            SendMessage(tweet, source);
        }
        void YoutubeSearch(string args, string source, string n)
        {
            if (!SearchEnabled(source))
                return;
            
            if (args.StartsWith(".youtube"))
                args = args.Substring(9).Trim();
            else
                args = args.Substring(4).Trim();

            if (string.IsNullOrWhiteSpace(args))
                return;

            string result = YoutubeUtil.Search(args);

            if (result == "")
                return;

            SendMessage(result, source);
        }
        
        void ImageSearch(string args, string source, string n)
        {
            if (!SearchEnabled(source))
                return;
            bool random = args[2] == 'r';

            args = args.Substring(".im".Length).Trim();
            string result = SearchUtil.SearchImages(args, random, false);

            if (result != "-")
                SendMessage(string.Format("Found {0}: {1}", args, result), source);
            else
            {
                result = SearchUtil.SearchImages(args, random, false, true);
                
                if (result != "-")
                    SendMessage(string.Format("Found {0} (Yandex fallback): {1}", args, result), source);
            }
        }

        void GifSearch(string args, string source, string n)
        {
            if (!SearchEnabled(source))
                return;
            bool random = args[3] == 'r';

            args = args.Substring(".gif".Length).Trim();
            if (random)
                args = args.Substring(1).Trim();

            string result = SearchUtil.SearchImages(args, true, true);

            if (result != "-")
                SendMessage(string.Format("Found {0}: {1}", args, result), source);
        }

        void TumblrSearch(string args, string source, string n)
        {
            if (!SearchEnabled(source))
                return;
            bool random = args[3] == 'r';

            args = args.Substring(".tu".Length + (random ? 1 : 0)).Trim();
            string result = SearchUtil.SearchImages(args + " site:tumblr.com", random, false);

            if (result != "-")
                SendMessage(string.Format("Found {0}: {1}", args, result), source);
        }
        
        void DdgSearch(string args, string source, string n)
        {
            if (!SearchEnabled(source))
                return;
            try
            {
                ManualResetEvent finished = new ManualResetEvent(false);

                Task.Factory.StartNew(delegate
                {
                    bool fallback = args.StartsWith("fallback");

                    if (fallback)
                    {
                        args = args.Substring("fallback".Length).Trim();
                    }
                    else if (args.StartsWith(".g"))
                        args = args.Substring(".g".Length).Trim();
                    else
                        args = args.Substring(".ddg".Length).Trim();

                    string result = SearchUtil.SearchLinksDdg(args, false);

                    if (result != "-")
                    {
                        finished.Set();
                        var preview_result = LinkResolver.GetSummaryWithTimeout(result);

                        string preview = preview_result.Value == "" ? "link preview timed out" : preview_result.Value;
                        SendMessage($"Found {args}{(fallback ? " (duckduckgo fallback)" : "")}: {result} | {preview}", source);
                    }
                });

                if (!finished.WaitOne(3000))
                    throw new Exception();
            }
            catch
            {
            }
        }
    }
}