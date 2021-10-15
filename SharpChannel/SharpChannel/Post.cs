using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SharpChannel
{
    [Serializable]
    public class Post
    {
        [JsonProperty("no")]
        public int ID { get; set; }

        [JsonProperty("resto")]
        public int ResponseTo { get; set; }

        [JsonProperty("com")]
        public string RawComment { get; set; }

        public string Comment
        {
            get
            {
                if (string.IsNullOrWhiteSpace(RawComment))
                    return "";

                if (_comment != null)
                    return _comment;

                _comment = Regex.Replace(HttpUtility.HtmlDecode(RawComment.Replace("<br>", "\n")), "<.*?>", String.Empty);
                return _comment;
            }
        }

        private string _comment = null;

        public string SmartSubject
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Subject))
                    return Subject;

                if (!string.IsNullOrWhiteSpace(Comment))
                    return Comment.Split('\n')[0];

                return null;
            }
        }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("trip")]
        public string Tripcode { get; set; }

        [JsonProperty("tim")]
        public ulong Filename { get; set; }

        [JsonProperty("filename")]
        public string OriginalFilename { get; set; }

        [JsonProperty("ext")]
        public string Extension { get; set; }

        [JsonProperty("sub")]
        public string Subject { get; set; }

        [JsonProperty("archived")]
        private int _archived { get; set; }

        [JsonProperty("images")]
        public int Images { get; set; }

        [JsonProperty("replies")]
        public int Replies { get; set; }

        [JsonProperty("time")]
        public ulong Time { get; set; }

        [JsonProperty("unique_ips")]
        public int Posters { get; set; }

        [JsonProperty("bumplimit")]
        public int BumpLimit { get; set; }

        [JsonProperty("imagelimit")]
        public int ImageLimit { get; set; }

        [JsonProperty("last_modified")]
        public ulong ModificationTime { get; set; }

        public bool Archived
        {
            get
            {
                return _archived == 1;
            }
        }

        public string FileUrl
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Extension))
                    return null;

                return string.Format("https://i.4cdn.org{0}", FilePath);
            }
        }

        public string FilePath
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Extension))
                    return null;

                return string.Format("/{0}/{1}{2}", Parent.Parent.Name, Filename, Extension);
            }
        }

        public DateTime PostTime
        {
            get
            {
                return new DateTime(1970, 1, 1).AddSeconds(Time);
            }
        }

        public Thread Parent { get; set; }
        public bool Alive { get; internal set; }
        [field: NonSerialized]
        public OnPostDeleted Deleted;

        public Post()
        {

        }

        internal void Removal()
        {
            if (Deleted != null)
                Deleted(this);

            Alive = false;
        }
    }
}
