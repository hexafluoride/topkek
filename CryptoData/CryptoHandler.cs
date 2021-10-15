using Exchange;

using System;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using BasicBuffer;
using System.Diagnostics;
using OsirisBase;

namespace CryptoData
{
    class CryptoHandler
    {
        //static Dictionary<string, HashSet<string>> PairGraph = new Dictionary<string, HashSet<string>>();
        //static List<KeyValuePair<string, string>> ActualPairs = new List<KeyValuePair<string, string>>();
        //public static List<Ticker> 
        public static List<Ticker> Pairs = new List<Ticker>();
        public static HashSet<string> Tickers = new HashSet<string>();

        //public static BitfinexInstance Bitfinex = new BitfinexInstance();
        //public static BinanceInstance Binance = new BinanceInstance();

        static Logger Log = LogManager.GetCurrentClassLogger();
        static WebClient Client = new WebClient();
        static List<IExchange> PeckingOrder = new List<IExchange>();
        public static Dictionary<string, IExchange> Exchanges = new();

        public static void Init()
        {
            Exchanges["Binance"] = new BinanceInstance();
            
            //PopulatePairGraph();
            //Bitfinex.PopulatePairGraph("./bitfinex-pairs.txt");
            (Exchanges["Binance"] as BinanceInstance).PopulatePairGraph();

            foreach (var exchange in Exchanges.Values)
            {
                Task.Run(exchange.Connect);
                Pairs.AddRange(exchange.ActualPairs.Select(p => new Ticker(p.Key, p.Value, exchange)));
                //exchange.OnTickerUpdateReceived += TickerMan
            }

            /*Task.Run((Action)Bitfinex.Connect);
            Task.Run((Action)Binance.Connect);*/

            PeckingOrder = new List<IExchange>() { /*Bitfinex,*/ Exchanges["Binance"] };
            //Tickers = new HashSet<string>(Exchanges["Binance"].Currencies);
            Tickers = Exchanges.Values.SelectMany(e => e.Currencies).ToHashSet();
        }

        public static bool LooksLikeAddress(string addr)
        {
            string valid_chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            return ((addr.StartsWith("1") || addr.StartsWith("3")) &&
                addr.Length > 26 && addr.Length < 35 &&
                !addr.Any(a => !valid_chars.Contains(a)));
        }

        public static bool LooksLikeTxid(string tx)
        {
            string valid_chars =
                   "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            return (tx.Length == 64 &&
                !tx.Any(a => !valid_chars.Contains(a)));
        }

        public static string GetAddressInfo(string addr)
        {
            string valid_chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            if ((!addr.StartsWith("1") && !addr.StartsWith("3")) || 
                addr.Length > 35 ||
                addr.Any(a => !valid_chars.Contains(a)))
                return "That doesn't seem like a valid Bitcoin address.";

            string raw_response = Client.DownloadString(string.Format("https://blockchain.info/rawaddr/{0}", addr));
            var response = JObject.Parse(raw_response);

            double satoshi = 100000000d;

            double balance = response.Value<double>("final_balance") / satoshi;
            double received = response.Value<double>("total_received") / satoshi;
            double sent = response.Value<double>("total_sent") / satoshi;

            double btcusd = GetCurrentTickerData(PeckingOrder[0], new Ticker("BTC", "USDT")).LastTrade;

            double balance_usd = balance * btcusd;
            double received_usd = received * btcusd;
            double sent_usd = sent * btcusd;

            int n_tx = response.Value<int>("n_tx");

            //return string.Format("Bitcoin address {0}: {1} BTC/{2} USD balance after {3} transactions, {4} BTC/{5} USD received, {6} BTC/{7} USD sent", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
            //return string.Format("Bitcoin address {0}: 07{1:##,#0.########} BTC/03${2:##,#0.##} USD balance after {3} transactions, 07{4:##,#0.########} BTC/03${5:##,#0.##} USD received, 07{6:##,#0.########} BTC/03${7:##,#0.##} USD sent", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
            //return string.Format("Bitcoin address {0} has a balance of 07{1:##,#0.########} BTC/03${2:##,#0.##} USD after receiving 07{4:##,#0.########} BTC/03${5:##,#0.##} USD, and sending 07{6:##,#0.########} BTC/03${7:##,#0.##} USD in {3} transactions.", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
            return string.Format("Bitcoin address {0} has a balance of 07{1:##,#0.########} BTC(03${2:##,#0.##}) after receiving 07{4:##,#0.########} BTC(03${5:##,#0.##}) and sending 07{6:##,#0.########} BTC(03${7:##,#0.##}) in {3} transactions.", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
        }

        static DateTime last_block_query = DateTime.Now;
        static int last_block_height = -1;
        static string last_block_hash = "";
        static DateTime last_block_time = DateTime.Now;

        static int GetLatestBlock()
        {
            if (last_block_height > 0 && (DateTime.Now - last_block_query).TotalMinutes > 5)
                return last_block_height;

            string raw_response = Client.DownloadString("https://blockchain.info/latestblock");
            var response = JObject.Parse(raw_response);

            last_block_height = response.Value<int>("height");
            last_block_hash = response.Value<string>("hash");
            last_block_time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(response.Value<long>("time"));
            last_block_query = DateTime.Now;

            return last_block_height;
        }

        public static Ticker TickerFromString(string ticker)
        {
            var exchange = ticker.Substring(ticker.IndexOf('@') + 1);
            var rest = ticker.Substring(0, ticker.Length - (exchange.Length + 1));

            var left = rest.Split(':')[0];
            var right = rest.Split(':')[1];
            return new Ticker(left, right, exchange);
            //return PeckingOrder[0].TickerFromString(ticker);
        }

        public static string GetTransactionInfo(string tx)
        {
            double satoshi = 100000000d;
            string valid_chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            if (tx.Length != 64 ||
                tx.Any(a => !valid_chars.Contains(a)))
                return "That doesn't seem like a valid Bitcoin transaction ID.";

            string raw_response = Client.DownloadString(string.Format("https://blockchain.info/rawtx/{0}", tx));
            var response = JObject.Parse(raw_response);

            int block_height = GetLatestBlock();

            string confirm_status = "";

            if (!response.TryGetValue("block_height", out JToken tx_block))
                confirm_status = "4Unconfirmed transaction";
            else
            {
                int confirmations = block_height - (int)tx_block;
                if (confirmations < 6)
                {
                    confirm_status = string.Format("Transaction with 08{0} confirmations", confirmations);
                }
                else
                {
                    confirm_status = string.Format("Transaction with 03{0} confirmations", confirmations);
                }
            }


            string input_addr = response["inputs"][0]["prev_out"].Value<string>("addr");
            int out_count = response["out"].Count();

            double total_transacted = response["inputs"].Sum(i => i["prev_out"].Value<long>("value") / satoshi);
            double total_transacted_usd = total_transacted * GetCurrentTickerData(PeckingOrder[0], new Ticker("BTC", "USDT")).LastTrade;

            string estimated_output = "";
            double estimated_btc = 0;
            double estimated_usd = 0;

            try
            {
                estimated_output = response["out"].First(o => o.Value<int>("n") == 1).Value<string>("addr");
                estimated_btc = response["out"].First(o => o.Value<int>("n") == 1).Value<long>("value") / satoshi;

                estimated_usd = estimated_btc * GetCurrentTickerData(PeckingOrder[0], new Ticker("BTC", "USDT")).LastTrade;
            }
            catch
            {

            }

            string transacted_info = string.Format(", total {0:##,#0.########} BTC/{1:##,#0.##} USD transacted", total_transacted, total_transacted_usd);

            return string.Format("{0} from input {1} to {2} outputs{3}{4}",
                confirm_status,
                input_addr,
                out_count,
                estimated_output != "" ? string.Format(", estimated path: {0} --- {1:##,#0.########} BTC/{2:##,#0.##} USD ---> {3}", input_addr, estimated_btc, estimated_usd, estimated_output)
                : "",
                transacted_info);
        }

        //public static bool TickerExists(string ticker)
        //{
        //    if (ticker.Length < 6 || ticker.Length > 7)
        //        return false;

        //    ticker = ticker.Trim().TrimStart('t');
        //    ticker = ticker.ToUpper();

        //    string first3 = ticker.Substring(0, 3);
        //    string last3 = ticker.Substring(3, 3);

        //    return (Tickers.Any(t => t.StartsWith(first3)) && Tickers.Any(t => t.StartsWith(last3)));
        //}

        public static string GetLastBlockInfo()
        {
            GetLatestBlock();
            return string.Format("Last block has height {0}, hash {1}, mined {2} ago", last_block_height, last_block_hash, Utilities.TimeSpanToPrettyString(DateTime.UtcNow - last_block_time));
        }

        public static string Convert(string first, string second, double value)
        {
            Stopwatch sw = Stopwatch.StartNew();

            first = first.ToUpper();
            second = second.ToUpper();

            var path = FindPath(first, second, out string graph, out IExchange exchange);

            if (path == null)
                return string.Format("Couldn't find path from ticker {0} to {1}.", first, second);

            double temp_value = value;

            if (path.Count == 1)
            {
                var pair = path[0];
                double age = 0;
                TickerData ticker_data = null;

                if (exchange.ActualPairs.Any(p => p.Key == pair.Key && p.Value == pair.Value))
                {
                    var ticker = new Ticker(pair.Key, pair.Value);

                    ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value *= price;

                    age = (DateTime.Now - exchange.TickerAge[ticker]).TotalSeconds;

                    Log.Debug("Price of t{0}{1} is {2}", pair.Key, pair.Value, price);
                }
                else if (exchange.ActualPairs.Any(p => p.Key == pair.Value && p.Value == pair.Key))
                {
                    var ticker = new Ticker(pair.Value, pair.Key);

                    ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value /= price;

                    age = (DateTime.Now - exchange.TickerAge[ticker]).TotalSeconds;

                    Log.Debug("Price of t{0}{1} is {2}", pair.Value, pair.Key, price);
                }

                if (age > 30)
                    exchange.Reconnect();

                return string.Format("{0} {1} = {2:##,#0.########} {3} // 24h stats: high 03{6:##,#0.########}, low 04{7:##,#0.########}, volume {8:##,#}, change {9} (direct pair, ticker data is {4}, {5}s old)", value, first, temp_value, second, 
                    age < 15 ? "3fresh" :
                    age < 30 ? "8stale" :
                               "4expired, attempting to reconnect", (int)age,
                    ticker_data.DailyHigh,
                    ticker_data.DailyLow,
                    ticker_data.DailyVolume,
                    string.Format(ticker_data.DailyChangePercentage > 0 ? "03↑{0:0.##}%" : "04↓{0:0.##}%", ticker_data.DailyChangePercentage * 100));
            }

            //Dictionary<string, double> ages = new Dictionary<string, double>();
            List<KeyValuePair<Ticker, double>> ages = new List<KeyValuePair<Ticker, double>>();
            
            foreach (var pair in path)
            {
                if (exchange.ActualPairs.Any(p => p.Key == pair.Key && p.Value == pair.Value))
                {
                    var ticker = new Ticker(pair.Key, pair.Value, exchange);

                    var ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value *= price;

                    ages.Add(new KeyValuePair<Ticker, double>(ticker, (DateTime.Now - exchange.TickerAge[ticker]).TotalSeconds));

                    Log.Debug("Price of t{0}{1} is {2}", pair.Key, pair.Value, price);
                }
                else if (exchange.ActualPairs.Any(p => p.Key == pair.Value && p.Value == pair.Key))
                {
                    var ticker = new Ticker(pair.Value, pair.Key, exchange);

                    var ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value /= price;

                    ages.Add(new KeyValuePair<Ticker, double>(ticker, (DateTime.Now - exchange.TickerAge[ticker]).TotalSeconds));

                    Log.Debug("Price of t{0}{1} is {2}", pair.Value, pair.Key, price);
                }
                else
                {
                    Log.Warn("Invalid pair {0}:{1}", pair.Key, pair.Value);
                    throw new Exception("Invalid pair");
                }
            }

            var pairs_with_buffer = new Dictionary<Ticker, List<BufferElement>>();

            int longest_buffer = 0;
            int shortest_buffer = int.MaxValue;

            float first_value = -1;
            float low_value = float.MaxValue;
            float high_value = 0;

            foreach (var pair in ages)
            {
                bool reverse_s = !exchange.ActualPairs.Any(p => p.Key == pair.Key.First && p.Value == pair.Key.Second);

                var ticker = reverse_s ? new Ticker(pair.Key.Second, pair.Key.First) { Exchange = pair.Key.Exchange } : pair.Key;
                var buffer = TickerDataManager.GetRangeForTicker(ticker, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

                if(buffer == null)
                {
                    goto calc_end;
                }

                longest_buffer = Math.Max(longest_buffer, buffer.Count);
                shortest_buffer = Math.Min(shortest_buffer, buffer.Count);

                pairs_with_buffer.Add(pair.Key, buffer);
            }

            var low_combination = new List<KeyValuePair<Ticker, BufferElement>>();
            var high_combination = new List<KeyValuePair<Ticker, BufferElement>>();

            Ticker[] tickers = ages.Select(p => p.Key).ToArray();
            Dictionary<Ticker, double> current_values = new Dictionary<Ticker, double>();
            int[] indices = new int[tickers.Length];
            bool[] reverse = new bool[tickers.Length];
            int total_length = pairs_with_buffer.Sum(p => p.Value.Count);

            for (int j = 0; j < tickers.Length; j++)
            {
                var ticker = tickers[j];

                current_values[ticker] = pairs_with_buffer[ticker][0].Data;
                reverse[j] = !exchange.ActualPairs.Any(p => p.Key == ticker.First && p.Value == ticker.Second);
            }

            for(int i = 0; i < total_length; i++)
            {
                int earliest_ticker_index = -1;
                Ticker earliest_ticker = new Ticker("", "");
                BufferElement earliest_data = new BufferElement(int.MaxValue, 0);

                for(int j = 0; j < tickers.Length; j++)
                {
                    var ticker = tickers[j];

                    if (indices[j] >= pairs_with_buffer[ticker].Count)
                        continue;

                    var data = pairs_with_buffer[ticker][indices[j]];

                    if(data.Timestamp < earliest_data.Timestamp)
                    {
                        earliest_data = data;
                        earliest_ticker = ticker;
                        earliest_ticker_index = j;
                    }
                }

                current_values[earliest_ticker] = earliest_data.Data;

                double temp_val = value;
                for (int j = 0; j < tickers.Length; j++)
                {
                    var ticker = tickers[j];
                    //var data_point = pairs_with_buffer[ticker][indices[j]];
                    var data_point = current_values[ticker];
                    
                    if (reverse[j])
                        temp_val /= data_point;
                    else
                        temp_val *= data_point;
                }

                if (first_value == -1)
                    first_value = (float)temp_val;

                low_value = (float)Math.Min(temp_val, low_value);
                high_value = (float)Math.Max(temp_val, high_value);

                indices[earliest_ticker_index]++;
            }

            //for (int i = 0; i < longest_buffer; i++)
            //{
            //    double temp_val = value;
            //    int ref_timestamp = 0;

            //    for (int j = 0; j < tickers.Length; j++)
            //    {
            //        var ticker = tickers[j];
            //        bool reverse = !exchange.ActualPairs.Any(p => p.Key == ticker.First && p.Value == ticker.Second);
            //        var data_point = pairs_with_buffer[ticker][indices[j]];

            //        if(ref_timestamp == 0)
            //        {
            //            ref_timestamp = (int)data_point.Timestamp;
            //        }
            //        else
            //        {
            //            int current_diff = (int)Math.Abs(data_point.Timestamp - ref_timestamp);

            //            data_point = pairs_with_buffer[ticker][indices[j]];

            //        }

            //        if (reverse)
            //            temp_val /= pairs_with_buffer[ticker][indices[j]].Data;
            //        else
            //            temp_val *= pairs_with_buffer[ticker][indices[j]].Data;
            //    }

            //    if (first_value == -1)
            //        first_value = (float)temp_val;

            //    low_value = (float)Math.Min(temp_val, low_value);
            //    high_value = (float)Math.Max(temp_val, high_value);
            //}

            calc_end:

            float change_percentage = (float)(temp_value / first_value) - 1;
            
            var oldest_pair = ages.Aggregate((l, r) => l.Value < r.Value ? l : r);

            if (oldest_pair.Value > 30)
                exchange.Reconnect();

            sw.Stop();

            if (low_value != float.MaxValue)
            {
                return string.Format("{0} {1} = {2:##,#0.########} {3} // 24h stats: high 03{8:##,#0.########}, low 04{9:##,#0.########}, change {11} (virtual pair({7}), ticker data is {4}, oldest ticker is {5}, {6}s old, {10:0.00}s)", value, first, temp_value, second,
                    oldest_pair.Value < 15 ? "3fresh" :
                    oldest_pair.Value < 30 ? "8stale" :
                               "4expired, attempting to reconnect", oldest_pair.Key, (int)oldest_pair.Value, graph, high_value, low_value, sw.ElapsedMilliseconds / 1000d,
                    string.Format(change_percentage > 0 ? "03↑{0:0.##}%" : "04↓{0:0.##}%", change_percentage * 100));
            }
            else
            {

                return string.Format("{0} {1} = {2:##,#0.########} {3} // (virtual pair({7}), ticker data is {4}, oldest ticker is {5}, {6}s old, {8:0.00}s)", value, first, temp_value, second,
                    oldest_pair.Value < 15 ? "3fresh" :
                    oldest_pair.Value < 30 ? "8stale" :
                               "4expired, attempting to reconnect", oldest_pair.Key, (int)oldest_pair.Value, graph, sw.ElapsedMilliseconds / 1000d);
            }
            //string.Join(" -> ", ages.Select(p => string.Format("({0}, {1}s)", p.Key, (int)p.Value)))
            //Console.WriteLine("{0} {1} = {2} {3}", value, first, temp_value, second);
        }

        static string GetTickerName(string first, string second)
        {
            return string.Format("t{0}{1}", first.ToUpper().Substring(0, 3), second.ToUpper().Substring(0, 3));
        }

        public static TickerData GetCurrentTickerData(IExchange exchange, Ticker pair)
        {
            if ((DateTime.Now - exchange.LastMessage).TotalSeconds > 10)
            {
                exchange.Reconnect();
                Thread.Sleep(1000);
            }

            if (exchange.TickerData.ContainsKey(pair))
                return exchange.TickerData[pair];

            exchange.SubscribeToTicker(pair);

            int waited = 0;

            while (!exchange.TickerData.ContainsKey(pair) && ++waited < 20)
                Thread.Sleep(100);

            if (waited == 20)
            {
                exchange.Reconnect();
                Thread.Sleep(1000);
                exchange.SubscribeToTicker(pair);
            }

            while (!exchange.TickerData.ContainsKey(pair) && ++waited < 100)
                Thread.Sleep(100);

            if(waited == 100)
                throw new Exception(string.Format("Couldn't get price for pair {0}, retry in a couple of seconds", pair));

            return exchange.TickerData[pair];
        }

        static int GetWeight(string ticker)
        {
            if (ticker == "USD" || ticker == "EUR" || ticker == "BNB")
                return 3;
            else if (ticker == "ETH")
                return 2;

            return 1;
        }

        static List<KeyValuePair<string, string>> FindPath(string start_ticker, string end_ticker, out string graph, out IExchange final_exchange)
        {
            foreach (var exchange in PeckingOrder)
            {
                Log.Debug("Trying {0}", exchange.ExchangeName);
                var path = FindPath(exchange, start_ticker, end_ticker, out graph);

                if (path != null)
                {
                    final_exchange = exchange;
                    return path;
                }
            }

            throw new Exception("Couldn't find path");
        }

        static List<KeyValuePair<string, string>> FindPath(IExchange exchange, string start_ticker, string end_ticker, out string graph)
        {
            graph = "";

            if (!exchange.PairGraph.ContainsKey(start_ticker))
            {
                Log.Debug("Unknown ticker {0}", start_ticker);
                return null;
            }

            if (!exchange.PairGraph.ContainsKey(end_ticker))
            {
                Log.Debug("Unknown ticker {0}", end_ticker);
                return null;
            }

            if (exchange.PairGraph[start_ticker].Contains(end_ticker))
            {
                Log.Debug("Direct pair exists: {0}:{1}", start_ticker, end_ticker);
                return new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>(start_ticker, end_ticker) };
            }

            Dictionary<string, int> distance = new Dictionary<string, int>();
            Dictionary<string, bool> visited = new Dictionary<string, bool>();
            Dictionary<string, string> previous = new Dictionary<string, string>();

            HashSet<string> unvisited = new HashSet<string>(exchange.PairGraph.Keys);
            unvisited.Remove(start_ticker);

            foreach (var key in exchange.PairGraph.Keys)
            {
                distance[key] = int.MaxValue;
                visited[key] = false;
            }

            distance[start_ticker] = 0;

            string current_node = start_ticker;

            while (!visited[end_ticker] && unvisited.Any())
            {
                current_node = distance.Where(p => !visited[p.Key]).Aggregate((l, r) => l.Value < r.Value ? l : r).Key;
                var neighbors = exchange.PairGraph[current_node];

                foreach (var neighbor in neighbors)
                {
                    if (distance[neighbor] > distance[current_node] + GetWeight(current_node))
                    {
                        distance[neighbor] = distance[current_node] + GetWeight(current_node);
                        previous[neighbor] = current_node;
                    }
                }

                visited[current_node] = true;
                unvisited.Remove(current_node);
            }

            Queue<string> best_path_q = new Queue<string>();

            current_node = end_ticker;

            while (current_node != start_ticker)
            {
                best_path_q.Enqueue(current_node);
                current_node = previous[current_node];
            }

            best_path_q.Enqueue(current_node);

            var best_path = best_path_q.Reverse().ToList();
            graph = string.Join(" -> ", best_path);

            Log.Debug("Best path from {0} to {1} is via {2}", start_ticker, end_ticker, string.Join(" -> ", best_path));

            var best_path_pairified = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < best_path.Count - 1; i++)
            {
                best_path_pairified.Add(new KeyValuePair<string, string>(best_path[i], best_path[i + 1]));
            }

            return best_path_pairified;
        }
    }
}
