using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Backyard
{
    public class QuoteUtil
    {
        public List<Quote> Quotes { get; set; } = new();
        private Dictionary<(string, string), Query> Queries = new();

        public QuoteUtil()
        {
            
        }

        public void Purge() => Queries.Clear();

        public void LoadFromCsv(string source, string filename)
        {
            Quotes.RemoveAll(q => q.Source == source && q.Filename == filename);
            
            using (var sr = new StreamReader(filename))
            {
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();

                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(',');
                    var id = int.Parse(parts[0]);
                    var time = long.Parse(parts[1]);
                    var score = int.Parse(parts[2]);
                    var contents = string.Join(',', parts.Skip(3));

                    if (contents[0] == '"' && contents[contents.Length - 1] == '"')
                        contents = contents.Substring(1, contents.Length - 2);

                    var quote = new Quote()
                    {
                        Id = id,
                        Contents = contents,
                        Score = score,
                        Source = source,
                        Time = DateTime.UnixEpoch.AddSeconds(time),
                        Filename = filename
                    };
                    
                    Quotes.Add(quote);
                }
            }
        }

        Query GetQuery(string query, string source)
        {
            if (!Queries.ContainsKey((source, query)))
            {
                var matching = Quotes.Where(q => q.Source == source && q.Contents.Contains(query, StringComparison.InvariantCultureIgnoreCase)).ToArray();
                var q = new Query(matching)
                {
                    Term = query,
                    Source = source,
                    Created = DateTime.UtcNow
                };
                
                Queries[(source, query)] = q;
            }

            return Queries[(source, query)];
        }

        public Quote GetNextQuote(string query, string source)
        {
            return GetQuery(query, source).GetNext();
        }
    }

    public class Query
    {
        public static Random StaticRandom = new();
        
        public string Source { get; set; }
        public string Term { get; set; }
        public DateTime Created { get; set; }
        public Random Random { get; set; }
        
        private Quote[] results { get; set; }
        private int[] indices { get; set; }
        private int counter { get; set; }

        public Query(Quote[] results)
        {
            this.results = results;
            Random = new Random(StaticRandom.Next());
            indices = new int[results.Length];
            
            var input_copy = Enumerable.Range(0, results.Length).ToList();
            int i = 0;

            while(input_copy.Any())
            {
                int index = Random.Next(input_copy.Count);
                indices[i++] = input_copy[index];
                input_copy.RemoveAt(index);
            }
        }

        public Quote GetNext()
        {
            if (counter > 0 && counter % indices.Length == 0)
            {
                counter++;
                Random = new Random(StaticRandom.Next());
                return null;
            }

            return results[indices[counter++ % indices.Length]];
        }
    }

    public class Quote
    {
        public string Filename { get; set; }
        public string Source { get; set; }
        public int Id { get; set; }
        public int Score { get; set; }
        public DateTime Time { get; set; }
        public string Contents { get; set; }
    }
}