using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Net;

namespace SharpChannel
{
    class Utilities
    {
        public static string Download(string url)
        {
            try
            {
                WebClient web = new WebClient();
                return web.DownloadString(url);
            }
            catch
            {
                return "-";
            }
        }
    }
}
