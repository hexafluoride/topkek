using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ConversionTherapy;

namespace Example
{
    class Program
    {
        static void Main(string[] args)
        {
            HomoglyphTable table = new HomoglyphTable("./glyphs.txt");

            PrintString("maymаy");
            PrintString(table.Purify("maymаy"));

            Console.ReadKey();
        }

        static void PrintString(string str)
        {
            Console.WriteLine(str);
            Console.WriteLine(string.Join(", ", str.Select(c => ((int)c).ToString("X"))));
        }
    }
}
