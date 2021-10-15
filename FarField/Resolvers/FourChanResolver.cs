using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OsirisNext;
using SharpChannel;

namespace FarField
{
    public class FourChanResolver : IResolver
    {
        public Dictionary<string, Board> Boards = new Dictionary<string, Board>();

        public string Name => "4chan";

        public FourChanResolver()
        {
            EndpointManager.DefaultProvider = new FourEndpointProvider();
        }

        private Random Random = new Random();
        private List<string> LastOutputs = new List<string>();
        private int OutputCount = 100;
        
        public Board GetBoard(string board_id, bool delay = true)
        {
            board_id = board_id.ToLower();

            if (Boards.ContainsKey(board_id))
                return Boards[board_id];

            Board board = new Board(board_id, false);
            Boards.Add(board_id, board);

            return board;
        }

        public bool Ready(string url)
        {
            return true;
            //Uri uri = new Uri(url);
            //var parts = uri.AbsolutePath.Split('/');
            //var board_id = parts[1];

            //Console.Write("Testing readiness for board {0}: ", board_id);

            //var ret = !GetBoard(board_id, true).Updating;
            //Console.WriteLine(ret);
            //return ret;
        }

        public bool Matches(string url)
        {
            url = url.ToLower();

            Uri uri = new Uri(url);
            var parts = uri.AbsolutePath.Split('/');

            if (parts.Length < 4)
                return false;

            if (!int.TryParse(parts[3], out int id))
                return false;

            return url.StartsWith("http://boards.4chan.org") ||
                url.StartsWith("https://boards.4chan.org") ||
                url.StartsWith("http://boards.4channel.org") ||
                url.StartsWith("https://boards.4channel.org");
        }

        public string GetCacheID(string url)
        {
            url = url.ToLower();

            Uri uri = new Uri(url);
            return "4chan:" + uri.AbsolutePath + uri.Fragment;
        }

        static string Layout = "034chan thread: {0} posted on 07/{1}/ | {2} | {3} | {4} | Posted {5} ago{6}";
        string post_layout = "034chan post: {0} posted on thread {1} on 07/{2}/ | {3} | Posted {4} ago{5}";

        public ResolverResponse GetSummary(string url)
        {
            url = url.ToLower();
            var uri = new Uri(url);
            string path = uri.AbsolutePath;
            var post_str = uri.Fragment;
            Console.WriteLine(url);
            Console.WriteLine(uri.Fragment);

            int post_id = -1;
            if(post_str.Length > 0)
                int.TryParse(post_str.Substring(2), out post_id);

            var parts = path.Split('/');
            string board_id = parts[1];
            string id_str = parts[3];

            if (!int.TryParse(id_str, out int id))
                return null;

            var board = GetBoard(board_id);

            if(board.Threads.Count == 0 || board.Threads.All(p => p.ID != id))
            {
                var thread = new Thread(id, board);
                thread.Update();

                board.Threads.Add(thread);
            }

            for(int i = 0; i < board.Threads.Count; i++)
            {
                var thread = board.Threads[i];

                if (thread.ID == id)
                {
                    if(post_id > -1)
                    {
                        try
                        {
                            var post = thread.Posts.First(p => p.ID == post_id);
                            int replies = thread.Posts.Where(p => p.Comment.Contains(">>" + post.ID)).Count();

                            return new ResolverResponse(string.Format(post_layout,
                                OsirisBase.Utilities.Bold(post.SmartSubject) ?? "(no subject)",
                                OsirisBase.Utilities.Bold(thread.OP.SmartSubject) ?? "(no subject)",
                                board_id,
                                OsirisBase.Utilities.BetterPlural(replies, "reply", 0, 8),
                                OsirisBase.Utilities.TimeSpanToPrettyString(DateTime.UtcNow - post.PostTime),
                                post.Filename > 0 ? string.Format(" | File: {0}", post.FileUrl) : ""));
                        }
                        catch
                        {

                        }
                    }

                    return new ResolverResponse(string.Format(Layout, 
                        OsirisBase.Utilities.Bold(thread.OP.SmartSubject) ?? "(no subject)", 
                        board_id,
                        OsirisBase.Utilities.BetterPlural(thread.OP.Replies, "reply", thread.OP.BumpLimit, 8),
                        OsirisBase.Utilities.BetterPlural(thread.OP.Images, "image", thread.OP.ImageLimit, 3),
                        OsirisBase.Utilities.BetterPlural(thread.OP.Posters, "poster", 0, 11),
                        OsirisBase.Utilities.TimeSpanToPrettyString(DateTime.UtcNow - thread.OP.PostTime),
                                thread.OP.Filename > 0 ? string.Format(" | File: {0}", thread.OP.FileUrl) : ""));
                }
            }

            throw new Exception("nope");
        }
    }
}