using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exchange.WebSockets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Exchange
{
    public class KucoinInstance : IExchange
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

        public string ExchangeName { get { return "Kucoin"; } }

        public DateTime LastMessage { get; set; }

        public Dictionary<Ticker, TickerData> TickerData { get; set; }
        public Dictionary<Ticker, DateTime> TickerAge { get; set; }
        private ClientConnection client;

        private bool _reconnecting = false;

        public KucoinInstance()
        {
            PairGraph = new Dictionary<string, HashSet<string>>();
            ActualPairs = new List<KeyValuePair<string, string>>();
            Currencies = new HashSet<string>();

            //Endpoint = "wss://stream.binance.com:9443/ws/!ticker@arr";
        }

        public void Connect()
        {
            //PopulatePairGraph();
            lock (Currencies)
            {
                try
                {
                    var postclient = new HttpClient();

                    var resp = postclient.PostAsync("https://api.kucoin.com/api/v1/bullet-public", new StringContent("")).Result.Content
                        .ReadAsStringAsync().Result;

                    var resp_parsed = JObject.Parse(resp);
                    var token = resp_parsed["data"].Value<string>("token");
                    var instances = resp_parsed["data"]["instanceServers"];

                    var first_valid = instances.First(i => i.Value<string>("protocol") == "websocket");
                    Endpoint = first_valid.Value<string>("endpoint") + $"?token={token}";
                    
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

                            if (obj is JObject jobj)
                            {
                                if (jobj.ContainsKey("type"))
                                {
                                    var type = jobj.Value<string>("type");

                                    switch (type)
                                    {
                                        case "ping":
                                            var resp = new { id = jobj.Value<string>("id"), type = "pong" };
                                            Console.Write(JsonConvert.SerializeObject(resp));
                                            client.SendText(JsonConvert.SerializeObject(resp));
                                            break;
                                        case "message":
                                            HandleTicker(jobj);
                                            break;
                                    }
                                }
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
                    
                    /*
                     * {
    "id": 1545910660739,                          //The id should be an unique value
    "type": "subscribe",
    "topic": "/market/ticker:BTC-USDT,ETH-USDT",  //Topic needs to be subscribed. Some topics support to divisional subscribe the informations of multiple trading pairs through ",".
    "privateChannel": false,                      //Adopted the private channel or not. Set as false by default.
    "response": true                              //Whether the server needs to return the receipt information of this subscription or not. Set as false by default.
}
                     */

                    int start = 10050;

                    foreach (var market in Markets)
                    {
                        var subscribe = new
                        {
                            id = start++,
                            type = "subscribe",
                            topic = $"/market/snapshot:{market}",
                            privateChannel = false,
                            response = false
                        };
                        
                        //client.SendText(JsonConvert.SerializeObject(subscribe));
                        Log.Info($"Subscribed to market {market}");
                    }
                    
                    var subscribe_tick = new
                    {
                        id = start++,
                        type = "subscribe",
                        topic = $"/market/ticker:all",
                        privateChannel = false,
                        response = false
                    };
                    client.SendText(JsonConvert.SerializeObject(subscribe_tick));

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

        public void HandleTicker(JObject message)
        {
            if (message.Value<string>("topic") == "/market/ticker:all")
            {
                try
                    {
                        string symbol = message.Value<string>("subject");

                        if (symbol == null)
                        {
                            Log.Info($"No symbol provided, ticker: {message.ToString()}");
                            return;
                        }

                        var ticker = TickerFromSymbol(symbol);

                        if (ticker == null)
                        {
                            //Log.Info($"Symbol {symbol} did not resolve to a ticker");
                            return;
                        }

                        //var time = DateTime.UnixEpoch.AddMilliseconds(raw_ticker.Value<double>("datetime")).ToLocalTime();
                        var time = DateTime.Now;

                        if (TickerAge.ContainsKey(ticker) && (TickerAge[ticker] == time || (DateTime.Now - TickerAge[ticker]).TotalSeconds < 2))
                        {
                            return;
                        }

                        var raw_ticker = message["data"];
                        
                        ticker.Exchange = ExchangeName;

                        TickerData data = new TickerData();

                        data.Ticker = ticker;
                        data.LastTrade = double.Parse(raw_ticker.Value<string>("price"));
                        /*data.DailyChangePercentage = double.Parse(raw_ticker.Value<string>("changeRate"));
                        data.DailyHigh = double.Parse(raw_ticker.Value<string>("high"));
                        data.DailyLow = double.Parse(raw_ticker.Value<string>("low"));
                        data.DailyVolume = double.Parse(raw_ticker.Value<string>("vol"));*/
                        data.Retrieved = time;
                        data.Timestamp = time;

                        TickerData[ticker] = data;
                        TickerAge[ticker] = DateTime.Now;

                        OnTickerUpdateReceived?.Invoke(this, data);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex);
                    }
            }
            else if (message.Value<string>("subject") == "trade.snapshot")
            {
                var raw_ticker = message["data"]["data"];
                //foreach(var raw_ticker in message["data"])
                {
                    try
                    {
                        string symbol = raw_ticker.Value<string>("symbol");

                        if (symbol == null)
                        {
                            Log.Info($"No symbol provided, ticker: {raw_ticker.ToString()}");
                            return;
                        }

                        var ticker = TickerFromSymbol(symbol);

                        if (ticker == null)
                        {
                            //Log.Info($"Symbol {symbol} did not resolve to a ticker");
                            return;
                        }

                        var time = DateTime.UnixEpoch.AddMilliseconds(raw_ticker.Value<double>("datetime")).ToLocalTime();

                        if (TickerAge.ContainsKey(ticker) && (TickerAge[ticker] == time || (DateTime.Now - TickerAge[ticker]).TotalSeconds < 2))
                        {
                            return;
                        }
                        
                        ticker.Exchange = ExchangeName;

                        TickerData data = new TickerData();

                        data.Ticker = ticker;
                        data.LastTrade = double.Parse(raw_ticker.Value<string>("lastTradedPrice"));
                        data.DailyChangePercentage = double.Parse(raw_ticker.Value<string>("changeRate"));
                        data.DailyHigh = double.Parse(raw_ticker.Value<string>("high"));
                        data.DailyLow = double.Parse(raw_ticker.Value<string>("low"));
                        data.DailyVolume = double.Parse(raw_ticker.Value<string>("vol"));
                        data.Retrieved = time;
                        data.Timestamp = time;

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
            else
            {
                Log.Warn($"Unknown message subject {message.Value<string>("subject")}");
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

        private List<string> Markets = new List<string>();

        public void PopulatePairGraph()
        {
            if (ActualPairs.Any())
                return;
            
            var raw_info = new WebClient().DownloadString("https://api.kucoin.com/api/v1/symbols");
            var json = JObject.Parse(raw_info);
            var symbols = new List<string>();

            foreach (var symbol in json["data"])
            {
                symbols.Add($"{symbol.Value<string>("baseCurrency")}:{symbol.Value<string>("quoteCurrency")}");
                
                if (!Markets.Contains(symbol.Value<string>("market")))
                    Markets.Add(symbol.Value<string>("market"));
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

            var split = str.Split('-');

            if (!Currencies.Contains(split[0]) || !Currencies.Contains(split[1]))
            {
                //Log.Warn($"Could not resolve symbol {str}");
                return null;
            }
            
            return TickerCache[str] = new Ticker(split[0], split[1]) {Exchange = ExchangeName};
            
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
