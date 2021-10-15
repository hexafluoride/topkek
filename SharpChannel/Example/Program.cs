using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SharpChannel;

namespace Example
{
    class Program
    {
        static void Main(string[] args)
        {
            EndpointManager.DefaultProvider = new FourEndpointProvider(); // this will make us contact the actual 4chan API endpoint
            Board board = new Board("b");

            board.NewThread += (t) => {
                Console.WriteLine("New thread on {0}: {1}, {2}", t.Parent.Name, t.ID, board.updating_threads.CurrentCount);
            };

            board.ThreadDeleted += (t) =>
            {
                Console.WriteLine("Thread {0} on {1} deleted", t.ID, t.Parent.Name);
            };

            board.StartAutoUpdate();

            Console.ReadKey();
        }
    }
}
