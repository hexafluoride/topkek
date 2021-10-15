using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange
{
    public class Ticker
    {
        public string First { get; set; }
        public string Second { get; set; }
        public string Exchange { get; set; }

        public Ticker(string first, string second)
        {
            First = string.Intern(first);
            Second = string.Intern(second);
        }

        public Ticker(string first, string second, string exchange) :
            this(first, second)
        {
            Exchange = exchange;
        }

        public Ticker(string first, string second, IExchange exchange) :
            this(first, second, exchange.ExchangeName)
        {

        }

        public override string ToString()
        {
            return First + Second;
        }

        private uint DjbHash(string str)
        {
            uint hash = 5381;
            
            for(int i = 0; i < str.Length; i++)
            {
                hash = ((hash << 5) + hash) + (uint)str[i];
            }

            return hash;
        }

        public override int GetHashCode()
        {
            return (int)DjbHash(First + Second);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Ticker))
                return false;

            var ticker = (Ticker)obj;

            return ticker.First == First && ticker.Second == Second;
        }
    }

    [Serializable]
    public class TickerData
    {
        public Ticker Ticker { get; set; }
        public double LastTrade { get; set; }
        public double DailyHigh { get; set; }
        public double DailyLow { get; set; }
        public double DailyVolume { get; set; }
        public double DailyChangePercentage { get; set; }
        public DateTime Timestamp { get; set; }

        public DateTime Retrieved = DateTime.Now;

        public TickerData()
        {
        }

        public TickerData(Ticker ticker, JArray obj)
        {
            Ticker = ticker;

            DailyChangePercentage = obj[1][5].Value<double>();
            LastTrade = obj[1][6].Value<double>();
            DailyVolume = obj[1][7].Value<double>();
            DailyHigh = obj[1][8].Value<double>();
            DailyLow = obj[1][9].Value<double>();

            Timestamp = DateTime.Now;
        }

        public override string ToString()
        {
            return string.Format("{0} price: {1}, low: {2}, high: {3}, volume: {4}, change: {5}%", Ticker, LastTrade, DailyLow, DailyHigh, DailyVolume, DailyChangePercentage * 100d);
        }
    }
}
