using System;
using System.Web;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public partial class FarField
    {
        void LastFm(string args, string source, string n)
        {
            try
            {
                if (args.StartsWith(".fm"))
                    args = args.Substring(".fm".Length).Trim();
                else
                    args = args.Substring(".lastfm".Length).Trim();

                string username = "";
                string trimmedSource = source.Substring(0, source.IndexOf('/'));

                Console.WriteLine("{0} {1}", args, n);

                if (!string.IsNullOrWhiteSpace(args))
                {
                    if (HasUser(source, args) &&
                        GetUserDataForSourceAndNick<string>(trimmedSource, args, "lastfm") != default)
                    {
                        username = GetUserDataForSourceAndNick<string>(trimmedSource, args, "lastfm");
                    }
                    else
                    {
                        username = args;
                        SetUserDataForSourceAndNick(trimmedSource, n, "lastfm", username);
                    }
                }
                else
                {
                    if (GetUserDataForSourceAndNick<string>(trimmedSource, n, "lastfm") != default)
                    {
                        username = GetUserDataForSourceAndNick<string>(trimmedSource, n, "lastfm");
                    }
                    else
                    {
                        SendMessage("You need to specify a username with .fm <username> first.", source);
                        return;
                    }
                }

                // magic happens here

                Console.WriteLine("before last.fm clal");
                string response = wa_client.DownloadString(string.Format("http://ws.audioscrobbler.com/2.0/?method=user.getrecenttracks&user={0}&format=json&api_key={1}", HttpUtility.UrlEncode(username), Config.GetString("lastfm.key")));
                Console.WriteLine("last.fm call complete");
                var resp_json = JObject.Parse(response);
                Console.WriteLine(1);
                var tracks = resp_json["recenttracks"];
                Console.WriteLine(2);
                var track_obj = tracks["track"];
                Console.WriteLine(3);
                var track = track_obj[0];
                Console.WriteLine(4);

                Console.WriteLine(track);

                string artist = track["artist"].Value<string>("#text");
                Console.WriteLine(5);
                string track_name = track.Value<string>("name");
                Console.WriteLine(6);

                //var span = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(track["date"].Value<ulong>("uts"));

                var youtube_link = YoutubeUtil.Search($"{artist} {track_name}", true);

                string album = "";

                try
                {
                    album = track["album"].Value<string>("#text");
                }
                catch
                {

                }

                SendMessage(string.Format("{0} is currently listening to {1} - {2}{3}{4}", string.IsNullOrWhiteSpace(args) ? n : username, artist, track_name, album != "" ? string.Format(" on album {0}", album) : "", youtube_link != "-" ? $" | {youtube_link}" : ""), source);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Thigns are wrong");
                Console.WriteLine(ex);
            }
        }
    }
}