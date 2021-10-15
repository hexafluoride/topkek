using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Backyard
{
    public partial class Backyard
    {
        private int insult_index;
        private int compliment_index;

        private List<string> Insults = new List<string>();
        private List<string> Compliments = new List<string>();

        private Random Random = new Random();
        
        List<string> Shuffle(IEnumerable<string> input)
        {
            var input_copy = input.ToList();
            List<string> ret = new List<string>();

            while(input_copy.Any())
            {
                int index = Random.Next(input_copy.Count);
                ret.Add(input_copy[index]);
                input_copy.RemoveAt(index);
            }

            return ret;
        }

        public void StringLength(string args, string source, string n)
        {
            if (args.StartsWith(".len"))
                args = args.Substring(".len".Length).TrimStart();
            else if (args.StartsWith(".strlen"))
                args = args.Substring(".strlen".Length).TrimStart();

            SendMessage(n + ": " + args.Length.ToString(), source);
        }

        public void Insult(string args, string source, string n)
        {
            args = args.Substring(".insult".Length).Trim();

            if (args.Any() && HasUser(source, args))
                n = args;

            if (!Insults.Any())
            {
                if (!File.Exists("./insults.txt"))
                    return;

                var insults_tmp = File.ReadAllLines("./insults.txt").Where(i => i.Trim().Any() && i.Contains("{0}"));
                insults_tmp = insults_tmp.Distinct();

                if (!insults_tmp.Any())
                    return;

                Insults = Shuffle(insults_tmp);
                insult_index = 0;
            }
            else
                insult_index = (insult_index + 1) % Insults.Count;

            SendMessage(string.Format(Insults[insult_index], n), source);
        }

        public void Compliment(string args, string source, string n)
        {
            args = args.Substring(".compliment".Length).Trim();

            if (args.Any() && HasUser(source, args))
                n = args;

            if (!Compliments.Any())
            {
                if (!File.Exists("./compliments.txt"))
                    return;

                var compliments_tmp = File.ReadAllLines("./compliments.txt").Where(i => i.Trim().Any() && i.Contains("{0}"));
                compliments_tmp = compliments_tmp.Distinct();

                if (!compliments_tmp.Any())
                    return;

                Compliments = Shuffle(compliments_tmp);
                compliment_index = 0;
            }
            else
                compliment_index = (compliment_index + 1) % Compliments.Count;

            SendMessage(string.Format(Compliments[compliment_index], n), source);
        }
    }
}