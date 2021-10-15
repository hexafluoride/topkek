using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpChannel
{
    public interface IEndpointProvider
    {
        string GetThreadEndpoint(string board, int id);
        string GetBoardEndpoint(string board);
    }

    public class EndpointManager
    {
        public static IEndpointProvider DefaultProvider { get; set; }
    }

    public class FourEndpointProvider : IEndpointProvider
    {
        public FourEndpointProvider()
        {

        }

        public string GetThreadEndpoint(string board, int id)
        {
            return string.Format("http://a.4cdn.org/{0}/thread/{1}.json", board, id);
        }

        public string GetBoardEndpoint(string board)
        {
            return string.Format("http://a.4cdn.org/{0}/threads.json", board);
        }
    }

    public class FuukaEndpointProvider : IEndpointProvider
    {
        public string URL = "";

        public FuukaEndpointProvider(string url)
        {
            URL = url;
        }

        public string GetThreadEndpoint(string board, int id)
        {
            return string.Format("http://{0}/_/api/chan/thread/?board={1}&num={2}", URL, board, id);
        }

        public string GetBoardEndpoint(string board)
        {
            return "";
        }
    }
}
