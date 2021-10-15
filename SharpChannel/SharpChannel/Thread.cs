using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.IO;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace SharpChannel
{
    public delegate void OnNewPost(Post post);
    public delegate void OnPostDeleted(Post post);
    public delegate void OnThreadDeleted(Thread thread);

    [Serializable]
    public class Thread
    {
        [field: NonSerialized]
        public event OnNewPost NewPost;
        [field: NonSerialized]
        public event OnPostDeleted PostDeleted;
        [field: NonSerialized]
        public event OnThreadDeleted ThreadDeleted;

        public int ID { get; internal set; }
        public List<Post> Posts { get; internal set; }
        public Board Parent { get; internal set; }
        public bool Alive { get; internal set; }

        public IEndpointProvider EndpointProvider = EndpointManager.DefaultProvider;

        public Post OP
        {
            get
            {
                return Posts[0];
            }
        }

        public Thread(int id, Board board)
        {
            ID = id;
            Parent = board;
            Posts = new List<Post>();
            Alive = true;

            Update();
        }

        public void Update()
        {
            Parent.updating_threads.Wait();
            string thread_url = EndpointProvider.GetThreadEndpoint(Parent.Name, ID);

            if (thread_url == "")
            {
                Parent.updating_threads.Release();
                return;
            }

            string raw = Utilities.Download(thread_url);

            if (raw == "-")
            {
                Parent.updating_threads.Release();
                Removal();
                return;
            }

            JObject root = JObject.Parse(raw);

            JArray posts = root.Value<JArray>("posts");

            JsonSerializer serializer = new JsonSerializer();
            serializer.NullValueHandling = NullValueHandling.Ignore;

            List<int> current_posts = new List<int>();

            foreach (JObject rawpost in posts)
            {
                Post post = serializer.Deserialize<Post>(new JTokenReader(rawpost));
                post.Parent = this;

                current_posts.Add(post.ID);

                if (!Posts.Any(p => p.ID == post.ID))
                {
                    Posts.Add(post);

                    if (NewPost != null)
                        NewPost(post);
                }
            }

            Func<Post, bool> dead = (p => !current_posts.Contains(p.ID));

            Posts.Where(dead).ToList().ForEach(RemovePost);

            Parent.updating_threads.Release();
        }

        internal void Removal()
        {
            if (ThreadDeleted != null)
                ThreadDeleted(this);

            Alive = false;
        }

        private void RemovePost(Post post)
        {
            if (PostDeleted != null)
                PostDeleted(post);

            post.Removal();
        }

        public class Sleep
        {
        }
    }
}
