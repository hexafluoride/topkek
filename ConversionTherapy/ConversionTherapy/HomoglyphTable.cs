using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConversionTherapy
{
    public class HomoglyphTable
    {
        public List<Homoglyph> Homoglyphs = new List<Homoglyph>();

        public HomoglyphTable() {}
        
        public HomoglyphTable(string filename)
        {
            StreamReader sr = new StreamReader(filename);

            while(!sr.EndOfStream)
            {
                string line = sr.ReadLine();

                string[] arr = line.Split('\t');

                if (!arr.Any())
                    continue;

                Homoglyph homoglyph = new Homoglyph();

                homoglyph.Original = arr[0];

                foreach (string str in arr)
                    if (!string.IsNullOrWhiteSpace(str))
                        homoglyph.Homoglyphs.Add(str);

                Homoglyphs.Add(homoglyph);
            }
        }

        public string GetOriginal(string homoglyph)
        {
            var matching = Homoglyphs.Where(h => h.Homoglyphs.Contains(homoglyph));

            if (!matching.Any())
                return homoglyph;

            return matching.First().Original;
        }

        public string Purify(string text)
        {
            StringBuilder sb = new StringBuilder();

            foreach (char c in text)
                sb.Append(GetOriginal(c.ToString()));

            return sb.ToString();
        }
    }

    public class Homoglyph
    {
        public string Original;
        public List<string> Homoglyphs = new List<string>();

        public Homoglyph()
        {

        }
    }
}
