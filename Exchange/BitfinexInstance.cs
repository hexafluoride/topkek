using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Net.Sockets;

using Exchange.WebSockets;

using NLog;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Threading;
using System.Net;
using System.IO;

namespace Exchange
{
    public delegate void BookHook(JArray data);

    public class BitfinexInstance : IExchange
    {
        public Dictionary<string, HashSet<string>> PairGraph { get; set; }
        public List<KeyValuePair<string, string>> ActualPairs { get; set; }
        public HashSet<string> Currencies { get; set; }
        public Dictionary<string, string> TickerTranslations { get; set; }

        Logger Log = LogManager.GetCurrentClassLogger();

        public event BookHook OnBookDataReceived;
        public event TickerUpdate OnTickerUpdateReceived;
        public event ConnectEvent OnConnect;

        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string Endpoint { get; set; }

        public string ExchangeName { get { return "Bitfinex"; } }

        public Dictionary<string, double> ExchangeBalances = new Dictionary<string, double>();
        public AutoResetEvent BookHandle = new AutoResetEvent(false);

        public Dictionary<Ticker, TickerData> TickerData { get; set; }
        public Dictionary<Ticker, DateTime> TickerAge { get; set; }

        public DateTime LastMessage { get; set; }

        private SortedDictionary<double, BookEntry> Bids = new SortedDictionary<double, BookEntry>();
        private SortedDictionary<double, BookEntry> Asks = new SortedDictionary<double, BookEntry>();

        private string book_lock = "";

        private Stopwatch last_book_update = new Stopwatch();

        private Dictionary<int, string> channel_ids = new Dictionary<int, string>();
        private ClientConnection client;
        
        public BitfinexInstance(string key = "", string secret = "", string endpoint = "wss://api.bitfinex.com/ws/2")
        {
            PairGraph = new Dictionary<string, HashSet<string>>();
            ActualPairs = new List<KeyValuePair<string, string>>();
            Currencies = new HashSet<string>();
            TickerTranslations = new Dictionary<string, string>();

            ApiKey = key;
            ApiSecret = secret;
            Endpoint = endpoint;
        }
        
        public void Connect()
        {
            ExchangeBalances = new Dictionary<string, double>();

            Bids = new SortedDictionary<double, BookEntry>();
            Asks = new SortedDictionary<double, BookEntry>();

            TickerData = new Dictionary<Ticker, TickerData>();
            TickerAge = new Dictionary<Ticker, DateTime>();

            channel_ids = new Dictionary<int, string>();

            Uri endpoint_uri = new Uri(Endpoint);

            bool tls = endpoint_uri.Scheme == "wss";

            TcpClient tcp = new TcpClient(endpoint_uri.Host, tls ? 443 : 80);
            client = new ClientConnection(tcp, tls, endpoint_uri.Host);

            Log.Info("Connecting to {0}:{1}...", endpoint_uri.Host, ((IPEndPoint)tcp.Client.RemoteEndPoint).Port);

            client.OnDataReceived += (sender, msg, payload) =>
            {
                LastMessage = DateTime.Now;

                string payload_str = Encoding.UTF8.GetString(payload);

                try
                {
                    var obj = JToken.Parse(payload_str);

                    if(obj.Type == JTokenType.Array) // channel data
                    {
                        var arr = (JArray)obj; HandleChannelMessage(arr);
                        //Log.Debug("New message from channel {0}/{1}", arr[0].Value<int>(), arr[1].Value<string>());
                    }
                    else if(obj.Type == JTokenType.Object) // other data
                    {
                        HandleInfoMessage((JObject)obj);
                    }
                    else
                    {
                        Log.Warn("Unrecognized JToken type {0}", obj.Type);
                    }
                }
                catch(Exception ex)
                {
                    Log.Warn(ex);
                }
            };

            client.PerformHandshake(endpoint_uri.Host, endpoint_uri.PathAndQuery, "");
            client.StartThreads();

            LastMessage = DateTime.Now;

            Log.Info("Connected");
            OnConnect?.Invoke(this);
        }

        public List<BookEntry> GetEntries(int count, BookEntryType type)
        {
            lock (book_lock)
            {
                if (type == BookEntryType.Ask)
                {
                    return Asks.Take(count).Select(p => p.Value).ToList();
                }
                else if (type == BookEntryType.Bid)
                {
                    return Bids.Skip(Bids.Count - count).Take(count).Select(p => p.Value).ToList();
                }
            }

            return new List<BookEntry>();
        }

        public double GetDepth(int count, BookEntryType type)
        {
            var entries = GetEntries(count, type);
            return entries.Sum(p => p.Amount);
        }

        public double GetBestPrice(BookEntryType type)
        {
            lock (book_lock)
            {
                if (type == BookEntryType.Ask)
                    return Asks.First().Key;
                else if (type == BookEntryType.Bid)
                    return Bids.Last().Key;
            }

            return -1;
        }

        public void Authenticate()
        {
            HMACSHA384 hmac = new HMACSHA384(Encoding.UTF8.GetBytes(ApiSecret));

            ulong nonce = (ulong)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds * 1000;
            string payload = string.Format("AUTH{0}", nonce);
            string sig = BitConverter.ToString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLower().Replace("-", "");

            var obj = JObject.FromObject(new
            {
                apiKey = ApiKey,
                authSig = sig,
                authNonce = nonce,
                authPayload = payload
            });

            obj["event"] = "auth";

            client.SendText(obj.ToString());
        }

        public void SubscribeToTicker(Ticker ticker)
        {
            var obj = JObject.FromObject(new
            {
                channel = "ticker",
                symbol = "t" + ticker.First.Substring(0, 3) + ticker.Second.Substring(0, 3)
            });

            obj["event"] = "subscribe";

            client.SendText(obj.ToString());
        }

        private void SubscribeToBook(Ticker ticker)
        {
            var obj = JObject.FromObject(new
            {
                channel = "book",
                symbol = "t" + ticker.First.Substring(0, 3) + ticker.Second.Substring(0, 3)
            });

            obj["event"] = "subscribe";

            client.SendText(obj.ToString());
            last_book_update.Start();
        }

        private void Recalculate(int id, string recalc)
        {
            JArray arr = new JArray();

            arr.Add(id);
            arr.Add("calc");
            arr.Add(null);
            arr.Add(new JArray((object)new JArray(recalc)));

            client.SendText(arr.ToString());
        }

        public void HandleBook(JArray book)
        {
            int chan_id = book[0].Value<int>();

            string channel_type = channel_ids.ContainsKey(chan_id) ? channel_ids[chan_id] : book[1].Value<string>();

            {
                if (last_book_update.ElapsedMilliseconds > 300)
                    PrintBookStatus(channel_type.Split('_')[1]);

                last_book_update.Restart();

                if (OnBookDataReceived != null)
                {
                    JArray new_array = new JArray
                    (
                        0,
                        channel_type,
                        book[1]
                    );

                    OnBookDataReceived(new_array);
                }

                var book_structure = book[0].Value<int>() == 0 ? book[2] : book[1];

                if (book_structure[0].Type == JTokenType.Array) // snapshot
                {
                    foreach (var raw_entry in book_structure)
                    {
                        BookEntry entry = new BookEntry(raw_entry[0].Value<double>(), raw_entry[1].Value<int>(), raw_entry[2].Value<double>());

                        lock (book_lock)
                        {
                            if (entry.Type == BookEntryType.Ask)
                                Asks[entry.Price] = entry;
                            else if (entry.Type == BookEntryType.Bid)
                                Bids[entry.Price] = entry;
                            else
                                Log.Warn("Empty book entry: {0}", raw_entry);
                        }
                    }
                }
                else // update
                {
                    BookEntry entry = new BookEntry(book_structure[0].Value<double>(), book_structure[1].Value<int>(), book_structure[2].Value<double>());

                    if (entry.Count == 0)
                    {
                        lock (book_lock)
                        {
                            if (entry.Type == BookEntryType.Ask)
                                Asks.Remove(entry.Price);
                            else if (entry.Type == BookEntryType.Bid)
                                Bids.Remove(entry.Price);
                            else
                                Log.Warn("Empty book removal update: {0}", book_structure);
                        }
                    }
                    else if (entry.Count > 0)
                    {
                        lock (book_lock)
                        {
                            if (entry.Type == BookEntryType.Ask)
                                Asks[entry.Price] = entry;
                            else if (entry.Type == BookEntryType.Bid)
                                Bids[entry.Price] = entry;
                            else
                                Log.Warn("Empty book update: {0}", book_structure);
                        }
                    }
                }

                CalculateStance();
                BookHandle.Set();
            }
        }

        public void HandleChannelMessage(JArray update)
        {
            int chan_id = update[0].Value<int>();

            string channel_type = channel_ids.ContainsKey(chan_id) ? channel_ids[chan_id] : update[1].Value<string>();
            string identifier = channel_type.Split('_')[0];

            if (update[1].Type == JTokenType.String && update[1].Value<string>() == "hb")
            {
                //Log.Info("{0} heartbeat", chan_id);
                return;
            }

            switch(identifier)
            {
                case "ws":
                    {
                        foreach (var wallet in update[2])
                        {
                            if (wallet[4].Type == JTokenType.Null)
                            {
                                if (wallet[2].Value<float>() > 0)
                                {
                                    Recalculate(update[0].Value<int>(), string.Format("wallet_{0}_{1}", wallet[0], wallet[1]));
                                    continue;
                                }
                                else
                                {
                                    wallet[4] = 0;
                                }
                            }

                            Log.Debug("{0} wallet of currency {1} has {2} balance available", wallet[0], wallet[1], wallet[4]);
                            ExchangeBalances[wallet[1].Value<string>()] = wallet[4].Value<double>();
                        }
                    }
                    break;
                case "wu":
                    {
                        var wallet = update[2];
                        Log.Debug("{0} wallet of currency {1} has {2} balance available", wallet[0], wallet[1], wallet[2]);
                        ExchangeBalances[wallet[1].Value<string>()] = wallet[4].Value<double>();
                    }
                    break;
                case "n":
                    Log.Debug(update);
                    break;
                case "book":
                    HandleBook(update);
                    break;
                case "ticker":
                    string ticker_name = channel_type.Split('_')[1];
                    Ticker ticker = TickerFromSymbol(ticker_name);

                    //Log.Info("Ticker update on {0}: ask={1:0.0000000}/{2:0.00}, bid={3:0.0000000}/{4:0.00}, last_price={5:0.0000000}", channel_type.Split('_')[1], update[1][0], GetDepth(10, BookEntryType.Ask), update[1][2], GetDepth(10, BookEntryType.Bid), update[1][6]);
                    Log.Info("Ticker update on {0}: ask={1:0.0000000}, bid={2:0.0000000}, last_price={3:0.0000000}", ticker_name, update[1][0], update[1][2], update[1][6]);
                    //TickerData[channel_type.Split('_')[1]] = update[1][6].Value<double>();
                    TickerAge[ticker] = DateTime.Now;
                    TickerData[ticker] = new TickerData(ticker, update);

                    OnTickerUpdateReceived?.Invoke(this, TickerData[ticker]);
                    break;
                case "miu":
                case "fiu":
                    break;
                default:
                    Log.Warn("Unknown channel name \"{0}\"", channel_type);
                    break;
            }
        }

        public void Reconnect()
        {
            Console.WriteLine("Reconnecting...");
            new Thread(new ThreadStart(delegate
            {
                client?.Close();
                Thread.Sleep(500);
                Connect();
            })).Start();
        }

        private List<double> moving_window = new List<double>();
        public int moving_window_length = 70;
        private double current_stance = 0;

        public bool EstablishedStance()
        {
            // lock (moving_window)
            {
                return moving_window.Count >= moving_window_length;
            }
        }

        private void CalculateStance()
        {
            try
            {
                double immediate_ask_wall = 0;
                double immediate_bid_wall = 0;

                // lock (book_lock)
                {
                    if (!Asks.Any() || !Bids.Any())
                        return;

                    //double immediate_ask_wall = Asks.First().Value.Amount;
                    //double immediate_bid_wall = Bids.Last().Value.Amount;

                    immediate_ask_wall = GetDepth(1, BookEntryType.Ask);
                    immediate_bid_wall = GetDepth(1, BookEntryType.Bid);
                }

                double smaller = Math.Min(immediate_ask_wall, immediate_bid_wall);
                double bigger = Math.Max(immediate_ask_wall, immediate_bid_wall);

                double delta = bigger - smaller;

                double factor = (delta / smaller);

                if (immediate_ask_wall > immediate_bid_wall)
                    factor *= -1;

                // lock (moving_window)
                {
                    moving_window.Add(factor);

                    if (moving_window.Count > moving_window_length)
                        moving_window.RemoveAt(0);

                    current_stance = moving_window.Average();
                }
            }
            catch
            {
            }
        }

        public double GetStance()
        {
            return current_stance;
        }

        public void PrintBookStatus(string book)
        {
            if (!Asks.Any() || !Bids.Any())
                return;

            double factor = GetStance();

            Log.Info("{6:0.0000} | {0} gap={1:0.000000}, bid={4:0.000000}/{5:0.000} ask={2:0.000000}/{3:0.000}",
                    book,
                    GetBestPrice(BookEntryType.Ask) - GetBestPrice(BookEntryType.Bid),
                    GetBestPrice(BookEntryType.Ask),
                    GetDepth(10, BookEntryType.Ask),
                    GetBestPrice(BookEntryType.Bid),
                    GetDepth(10, BookEntryType.Bid), factor);
        }

        public void HandleInfoMessage(JObject msg)
        {
            string event_id = msg.Value<string>("event");

            switch(event_id)
            {
                case "info":
                    if (msg.TryGetValue("code", out JToken code_token))
                    {
                        int code = code_token.Value<int>();

                        switch ((BitfinexInfoCode)code)
                        {
                            case BitfinexInfoCode.BITFINEX_RECONNECT:
                                Thread.Sleep(10000);
                                client.Close();
                                Thread.Sleep(10000);
                                Connect();
                                break;
                            case BitfinexInfoCode.BITFINEX_MAINTENANCE_START:
                                break;
                            case BitfinexInfoCode.BITFINEX_MAINTENANCE_END:
                                Thread.Sleep(10000);
                                client.Close();
                                Connect();
                                break;
                        }

                        Log.Warn("Info message with code {0} and human-readable message \"{1}\"", code, msg.Value<string>("msg"));
                    }
                    else if(msg.TryGetValue("version", out JToken version_token))
                    {
                        int version = version_token.Value<int>();

                        if(version != 2)
                        {
                            Log.Error("Unrecognized API version {0}, closing connection", version);
                            client.Close();
                            return;
                        }

                        if(ApiKey != "" && ApiSecret != "")
                            Authenticate();
                    }
                    else
                    {
                        Log.Warn("Unrecognized info message!");
                    }
                    break;
                case "subscribed":
                    string chan_name = msg.Value<string>("channel");
                    int chan_id = msg.Value<int>("chanId");

                    if (chan_name == "ticker" || chan_name == "book")
                        chan_name += "_" + msg.Value<string>("symbol");

                    Log.Info("Subscribed to channel {0} at id {1}", chan_name, chan_id);
                    channel_ids[chan_id] = chan_name;
                    break;
                case "error":
                    Log.Warn("Error code: {0}, msg: \"{1}\"", msg["code"], msg["msg"]);
                    break;
                case "auth":
                    if(msg.Value<string>("status") == "OK")
                    {
                        Log.Info("Authenticated successfully!");
                    }
                    break;
                default:
                    Log.Warn("Unrecognized event type \"{0}\"", event_id);
                    break;
            }
        }

        public void PopulatePairGraph(string filename = "./pairs.txt")
        {
            PopulatePairGraph(File.ReadAllLines(filename).Where(p => p.Contains(':')).Distinct().ToList());
        }

        public void PopulatePairGraph(List<string> pairs)
        {
            var pairs_parsed = pairs.Select(p => new KeyValuePair<string, string>(p.Split(':')[0], p.Split(':')[1]));

            ActualPairs = pairs_parsed.ToList();

            pairs_parsed = pairs_parsed.Concat(pairs_parsed.Select(p => new KeyValuePair<string, string>(p.Value, p.Key)));

            foreach (var pair in pairs_parsed)
            {
                string left = pair.Key;
                string right = pair.Value;

                if (left.Length > 3)
                {
                    if (!TickerTranslations.ContainsKey(left.Substring(0, 3)))
                        TickerTranslations.Add(left.Substring(0, 3), left);
                    left = left.Substring(0, 3);
                }

                if (right.Length > 3)
                {
                    if(!TickerTranslations.ContainsKey(right.Substring(0, 3)))
                        TickerTranslations.Add(right.Substring(0, 3), right);
                    right = right.Substring(0, 3);
                }

                if (!PairGraph.ContainsKey(left))
                    PairGraph.Add(left, new HashSet<string>());

                if (!PairGraph.ContainsKey(right))
                    PairGraph.Add(right, new HashSet<string>());

                PairGraph[left].Add(right);
                PairGraph[right].Add(left);

                Currencies.Add(left);
                Currencies.Add(right);
            }
        }

        public string ConsumeCurrency(string str, out string currency)
        {
            if (TickerTranslations.Any(t => str.StartsWith(t.Key)))
            {
                var translation = TickerTranslations.First(t => str.StartsWith(t.Key));
                currency = translation.Value;

                str = str.Substring(translation.Key.Length);
            }
            else
            {
                currency = Currencies.First(c => str.StartsWith(c));
                str = str.Substring(currency.Length);
            }

            return str;
        }

        public Ticker TickerFromSymbol(string str)
        {
            if (str.StartsWith("t"))
                str = str.Substring(1);

            str = ConsumeCurrency(str, out string first_match);
            str = ConsumeCurrency(str, out string second_match);

            return new Ticker(first_match, second_match) { Exchange = ExchangeName };
        }
    }

    public class BookEntry
    {
        public double Price { get; set; }
        public int Count { get; set; }
        public double Amount { get; set; }
        public BookEntryType Type { get; set; }

        public BookEntry(double price, int count, double amount)
        {
            Price = price;
            Count = count;
            Amount = Math.Abs(amount);

            if (amount > 0)
                Type = BookEntryType.Bid;
            else if (amount < 0)
                Type = BookEntryType.Ask;
        }
    }

    public enum BookEntryType
    {
        None,
        Bid,
        Ask
    }

    public enum BitfinexInfoCode
    {
        BITFINEX_RECONNECT = 20051,
        BITFINEX_MAINTENANCE_START = 20060,
        BITFINEX_MAINTENANCE_END = 20061
    }
}
