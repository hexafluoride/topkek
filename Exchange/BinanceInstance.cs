using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exchange.WebSockets;
using Newtonsoft.Json.Linq;
using NLog;

namespace Exchange
{
    public class BinanceInstance : IExchange
    {
        public Dictionary<string, HashSet<string>> PairGraph { get; set; }
        public List<KeyValuePair<string, string>> ActualPairs { get; set; }
        public HashSet<string> Currencies { get; set; }

        Logger Log = LogManager.GetCurrentClassLogger();
		
        public event TickerUpdate OnTickerUpdateReceived;
        public event ConnectEvent OnConnect;

        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public string Endpoint { get; set; }

        public string ExchangeName { get { return "Binance"; } }

        public DateTime LastMessage { get; set; }

        public Dictionary<Ticker, TickerData> TickerData { get; set; }
        public Dictionary<Ticker, DateTime> TickerAge { get; set; }
        private ClientConnection client;

        private bool _reconnecting = false;

        public BinanceInstance()
        {
            PairGraph = new Dictionary<string, HashSet<string>>();
            ActualPairs = new List<KeyValuePair<string, string>>();
            Currencies = new HashSet<string>();

            Endpoint = "wss://stream.binance.com:9443/ws/!ticker@arr";
        }

        public void Connect()
        {
            lock (Currencies)
            {
                return;
                
                try
                {
                    TickerData = new Dictionary<Ticker, TickerData>();
                    TickerAge = new Dictionary<Ticker, DateTime>();

                    Uri endpoint_uri = new Uri(Endpoint);

                    bool tls = endpoint_uri.Scheme == "wss";
                    int port = endpoint_uri.Port;

                    if (port == -1)
                        port = tls ? 443 : 80;

                    TcpClient tcp = new TcpClient(endpoint_uri.Host, port);
                    client = new ClientConnection(tcp, tls, endpoint_uri.Host);

                    Log.Info("Connecting to {0}:{1}...", endpoint_uri.Host,
                        ((IPEndPoint) tcp.Client.RemoteEndPoint).Port);

                    client.OnDataReceived += (sender, msg, payload) =>
                    {
                        LastMessage = DateTime.Now;

                        string payload_str = Encoding.UTF8.GetString(payload);

                        try
                        {
                            var obj = JToken.Parse(payload_str);

                            if (obj.Type == JTokenType.Array) // ticker data
                            {
                                var arr = (JArray) obj;
                                ParseTickers(arr);
                            }
                            else
                            {
                                Log.Warn("Unrecognized JToken type {0}", obj.Type);
                            }
                        }
                        catch (Exception ex)
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
                catch (Exception ex)
                {
                    Log.Error(ex, "while reconnecting");
                }
                finally {
                    _reconnecting = false;}
            }
        }

        public void ParseTickers(JArray tickers)
        {
            foreach(var raw_ticker in tickers)
            {
                try
                {
                    string symbol = raw_ticker.Value<string>("s");

                    if (symbol == null)
                    {
                        Log.Info($"No symbol provided, ticker: {raw_ticker.ToString()}");
                        continue;
                    }
                    
                    var ticker = TickerFromSymbol(symbol);

                    if (ticker == null)
                    {
                        Log.Info($"Symbol {symbol} did not resolve to a ticker");
                        continue;
                    }

                    if (symbol.Contains("USDP"))
                    {
                        //Log.Debug($"{symbol} {ticker}");
                    }
                    
                    ticker.Exchange = ExchangeName;

                    TickerData data = new TickerData();

                    data.Ticker = ticker;
                    data.LastTrade = double.Parse(raw_ticker.Value<string>("c"));
                    data.DailyChangePercentage = double.Parse(raw_ticker.Value<string>("P")) / 100d;
                    data.DailyHigh = double.Parse(raw_ticker.Value<string>("h"));
                    data.DailyLow = double.Parse(raw_ticker.Value<string>("l"));
                    data.DailyVolume = double.Parse(raw_ticker.Value<string>("v"));
                    data.Retrieved = DateTime.Now;
                    data.Timestamp = DateTime.Now;

                    TickerData[ticker] = data;
                    TickerAge[ticker] = DateTime.Now;

                    OnTickerUpdateReceived?.Invoke(this, data);
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
        }

		public void Reconnect()
        {
            lock (Currencies)
            {
                if (_reconnecting)
                    return;
                _reconnecting = true;
            }
            
            Log.Info("Reconnecting...");
            new Thread(new ThreadStart(delegate
            {
                try {client.Close();} catch{}
                Thread.Sleep(500);
                Connect();
            })).Start();
        }

		public void SubscribeToTicker(Ticker ticker)
        {

        }

        public TickerData? GetCurrentTickerData(Ticker ticker)
        {
            return TickerData.ContainsKey(ticker) ? TickerData[ticker] : null;
        }

        public List<Ticker> Tickers { get; set; } = new();

        public void PopulatePairGraph()
        {
            var raw_info = new WebClient().DownloadString("https://api.binance.com/api/v3/exchangeInfo");
            var json = JObject.Parse(raw_info);
            var symbols = new List<string>();

            foreach (var symbol in json["symbols"])
            {
                var baseAsset = symbol.Value<string>("baseAsset");
                var quoteAsset = symbol.Value<string>("quoteAsset");
                symbols.Add($"{baseAsset}:{quoteAsset}");
                Tickers.Add(new Ticker(baseAsset, quoteAsset, this));
            }

            PopulatePairGraph(symbols);
        }

        public void PopulatePairGraph(List<string> pairs)
        {
            var pairs_parsed = pairs.Select(p => new KeyValuePair<string, string>(p.Split(':')[0], p.Split(':')[1]));

            ActualPairs = pairs_parsed.ToList();

            pairs_parsed = pairs_parsed.Concat(pairs_parsed.Select(p => new KeyValuePair<string, string>(p.Value, p.Key)));

            foreach (var pair in pairs_parsed)
            {
                string right = pair.Value;

                if (!PairGraph.ContainsKey(pair.Key))
                    PairGraph.Add(pair.Key, new HashSet<string>());

                if (!PairGraph.ContainsKey(right))
                    PairGraph.Add(right, new HashSet<string>());

                PairGraph[pair.Key].Add(right);
                PairGraph[right].Add(pair.Key);

                Currencies.Add(pair.Key);
                Currencies.Add(right);
            }
        }

        private Dictionary<string, Ticker> TickerCache = new();

        public Ticker TickerFromSymbol(string str)
        {
            if (TickerCache.ContainsKey(str))
                return TickerCache[str];
            
            var possible_first_matches = Currencies.Where(c => str.StartsWith(c));

            foreach (var match in possible_first_matches)
            {
                var cropped = str.Substring(match.Length);
                var second_matches = Currencies.Where(c => cropped.StartsWith(c));

                if (second_matches.Any())
                    return TickerCache[str] = new Ticker(match, second_matches.FirstOrDefault()) {Exchange = ExchangeName};
            }

            return null;

            var first_match = Currencies.FirstOrDefault(c => str.StartsWith(c));

            if (first_match == default)
                return null;
            
            str = str.Substring(first_match.Length);
            var second_match = Currencies.FirstOrDefault(c => str.StartsWith(c));

            if (second_match == default)
                return null;

            //if (second_match == "USDT")
            //    second_match = "USD";

            return new Ticker(first_match, second_match) { Exchange = ExchangeName };
        }
    }
}
