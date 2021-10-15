using Pluralize.NET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public static class Utilities
    {
        static Pluralizer Pluralizer = new Pluralizer();

        public static string MakePlural(this string str)
        {
            return Pluralizer.Pluralize(str); 
        }

        private static Dictionary<char, char> superscript_replacements = new Dictionary<char, char>()
        {
            { '0', '⁰' },
            { '1', '¹' },
            { '2', '²' },
            { '3', '³' },
            { '4', '⁴' },
            { '5', '⁵' },
            { '6', '⁶' },
            { '7', '⁷' },
            { '8', '⁸' },
            { '9', '⁹' },
            { '-', '⁻' },
        };

        public static string Superscript(this int i)
        {
            string str = i.ToString();

            foreach (var pair in superscript_replacements)
                str = str.Replace(pair.Key, pair.Value);

            return str;
        }
    }
}
