using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Exchange;
using HeimdallBase;
using OsirisBase;
using UserLedger;

namespace CryptoData
{
    public class Program
    {
        public static void Main(string[] args) => new CryptoData().Start(args);
    }
    
    public class CryptoData : LedgerOsirisModule
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public void Start(string[] args)
        {
            var strs = new List<(string, string)>();
            for (int i = 0; i < 9; i++)
            {
                for (int j = 20; j <= 40; j++)
                {
                    strs.Add(CryptoHandler.GetBar(j - 15, j - 5, 2 * j, 0));
                }
                for (int j = 40; j > 20; j--)
                {
                    strs.Add(CryptoHandler.GetBar(j - 5, j - 15, 2 * j, 0));
                }

                break;
            }

            CryptoHandler.PrintCandlesticks(strs);
            
            strs.Clear();
            
            for (int i = 0; i < 9; i++)
            {
                for (int j = 20; j <= 40; j++)
                {
                    strs.Add(CryptoHandler.GetBar(j - 10, j - 7, j, 0));
                }
                for (int j = 40; j > 20; j--)
                {
                    strs.Add(CryptoHandler.GetBar(j - 9, j - 10, j, 0));
                }

                break;
            }

            CryptoHandler.PrintCandlesticks(strs);

            //return;

            Name = "crypto";
            //SmartAlertManager.Load();

            Commands = new Dictionary<string, MessageHandler>()
            {
                {"", Wildcard },
                {".exc", GetExchange },
                {".statage ", GetOldestStat},
                {".stat ", GetNearestStat },
                {".graph ", PrintGraphToConsole},
                {"$flush", (a, s, n) => { SendMessage(string.Format("Saved {0} entries.", TickerDataManager.Save()), s); SendMessage(string.Format("Total cache entries: {0}", TickerDataManager.Buffers.Buffers.Sum(b => b.Value.Cached)), s); GC.Collect(); } }
            };

            Init(args, delegate
            {
                CryptoHandler.Init(!args.Contains("--create-buffers"));
                TickerDataManager.Init(args.Contains("--create-buffers"));

                while (true)
                {
                    Thread.Sleep(15000);

                    foreach (var exc in CryptoHandler.Exchanges)
                    {
                        if ((DateTime.Now - exc.Value.LastMessage).TotalSeconds > 25)
                        {
                            try
                            {
                                Log.Debug($"Reconnecting to {exc.Key}...");
                                exc.Value.Reconnect();
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            });
        }

        void PrintGraphToConsole(string args, string source, string n)
        {
            try
            {
                args = args.Substring(".graph".Length).Trim();

                if (string.IsNullOrWhiteSpace(args) || args == "help")
                {
                    SendMessage("Syntax: .graph <currency>[:<other currency>] [candle width]", source);
                    SendMessage("By default, other currency is USDT and candle width is 2m.", source);
                    return;
                }

                var parts = args.Split(' ');

                Ticker ticker;
                parts[0] = parts[0].ToUpper();

                if (parts[0].Contains(':'))
                {
                    if (parts[0].Contains('@'))
                        ticker = CryptoHandler.TickerFromString(parts[0]);
                    else
                        ticker = CryptoHandler.TickerFromString(parts[0] + "@Binance");
                }
                else
                {
                    ticker = CryptoHandler.TickerFromString(parts[0] + ":USDT@Binance");
                }

                //if (!CryptoHandler.Exchanges[].Contains(ticker.ToString()))
                if (!CryptoHandler.Exchanges[ticker.Exchange].TickerData.ContainsKey(ticker))
                {
                    ticker = new Ticker(ticker.Second, ticker.First, ticker.Exchange);
                    
                    
                    if (!CryptoHandler.Exchanges[ticker.Exchange].TickerData.ContainsKey(ticker))
                        ticker = new Ticker(ticker.First, ticker.Second, "Kucoin");
                    if (!CryptoHandler.Exchanges[ticker.Exchange].TickerData.ContainsKey(ticker))
                        ticker = new Ticker(ticker.Second, ticker.First, "Kucoin");
                }

                if (!CryptoHandler.Exchanges[ticker.Exchange].TickerData.ContainsKey(ticker))
                {
                    SendMessage($"I couldn't find the ticker you asked for.", source);
                    return;
                }

                int candle_w_sec = 120;
                int candle_count = 70;

                if (parts.Length > 1)
                {
                    if (char.IsDigit(parts[1][0]))
                    {
                        var len = int.Parse(new string(parts[1].TakeWhile(char.IsDigit).ToArray()));

                        if (parts[1].EndsWith("m"))
                        {
                            candle_w_sec = len * 60;
                        }
                        else if (parts[1].EndsWith("s"))
                        {
                            candle_w_sec = len;
                        }
                        else if (parts[1].EndsWith("h"))
                        {
                            candle_w_sec = len * 3600;
                        }
                    }
                }

                if (candle_count * candle_w_sec > 10000)
                {
                    SendMessage("Note: you are viewing a bit too far into the past. Consider using narrower candles.",
                        source);
                }

                if (ticker == null)
                {
                    SendMessage("Couldn't find that ticker.", source);
                    return;
                }

                //var ticker = CryptoHandler.TickerFromString(args.Split(' ')[0].Trim());


                var start = DateTime.UtcNow.Subtract(TimeSpan.FromSeconds(candle_w_sec * candle_count));
                start = start.AddSeconds(-start.Second);

                (var candlesticks, var top, var scale) = CryptoHandler.RenderCandlesticks(
                    CryptoHandler.GrabCandlestickData(ticker,
                        TimeSpan.FromSeconds(candle_w_sec), candle_count, start));

                var latest = CryptoHandler.GetCurrentTickerData(CryptoHandler.Exchanges[ticker.Exchange], ticker);

                CryptoHandler.PrintCandlesticks(candlesticks);
                var lines = CryptoHandler.IrcPrintCandlesticks(candlesticks, top, scale, candle_w_sec, latest.LastTrade);

                foreach (var line in lines)
                {
                    SendMessage(line, source);
                    Thread.Sleep(15);
                }
                
                GetExchange($".exc 1 {ticker.First} to {ticker.Second}", source, n);
            }
            catch (Exception ex)
            {
                SendMessage($"Something went wrong: {ex.Message}", source);
            }
        }

        void GetNearestStat(string args, string source, string n)
        {
            args = args.Substring(".stat".Length).Trim();
            var ticker = CryptoHandler.TickerFromString(args.Split(' ')[0].Trim());
            var time_str = string.Join(" ", args.Split(' ').Skip(1));
            var time = TimeUtils.Get(time_str, true);
            var data = TickerDataManager.GetTickerDataForTime(ticker, time);
            
            SendMessage(string.Format("Ticker data for {0}: Requested age: {4}, {1:##,#0.########}, {2} old, {3}, delta {5}", ticker, data.LastTrade, Utilities.TimeSpanToPrettyString(DateTime.Now - data.Timestamp), data.Timestamp, time, time - data.Timestamp), source);
        }

        void GetOldestStat(string args, string source, string n)
        {
            args = args.Substring(".statage".Length).Trim();
            var ticker = CryptoHandler.TickerFromString(args);
            Log.Info("Ticker for .statage: {0}", ticker);
            var data = TickerDataManager.GetOldestTickerData(ticker, out int rawtime, out int index);
            Log.Info("Found data for {0}", ticker);

            SendMessage(
                $"Oldest ticker data for {ticker}: {data.LastTrade:##,#0.########}, {Utilities.TimeSpanToPrettyString(DateTime.Now - data.Timestamp)} old, {data.Timestamp}, {rawtime}, index {index}", source);
        }

        void Wildcard(string args, string source, string n)
        {
            var prefix = ".";
            
            if (Config.Contains("crypto.disabled", source))
                return;

            if (Config.Contains("crypto.alter", source))
                prefix = "!";
            
            if (args.StartsWith(prefix))
            {
                var ticker = args.Substring(1);
                var first = ticker.Split(' ')[0];

                if (CryptoHandler.Tickers.Contains(first.ToUpper()))
                {
                    var rest = ticker.Split(' ').Skip(1).Select(t => t.Trim()).ToArray();

                    //Console.WriteLine("\"{0}\"", rest[0]);

                    if (rest.Any() && CryptoHandler.LooksLikeAddress(rest[0]))
                    {
                        try
                        {
                            SendMessage(CryptoHandler.GetAddressInfo(rest[0]), source);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }

                    if (rest.Any() && CryptoHandler.LooksLikeTxid(rest[0]))
                    {
                        try
                        {
                            SendMessage(CryptoHandler.GetTransactionInfo(rest[0]), source);
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }

                    if (rest.Any() && (rest[0] == "block" || rest[0] == "latest" || rest[0] == "latestblock" || rest[0] == "last" || rest[0] == "lastblock"))
                    {
                        try
                        {
                            SendMessage(CryptoHandler.GetLastBlockInfo(), source);
                            return;
                        }
                        catch
                        {

                        }
                    }

                    if (rest.Any() && (CryptoHandler.Tickers.Contains(rest[0].ToUpper())))
                    {
                        GetExchange(string.Format(".exc 1 {0} to {1}", first, rest[0]), source, n);
                        return;
                    }

                    double amount = 1;
                    bool to_usd = true;
                    bool success = false;

                    for (int i = 0; i < rest.Length; i++)
                    {
                        if (rest[i].StartsWith("$") && (success = double.TryParse(rest[i].Substring(1), out amount)))
                        {
                            to_usd = false;
                            break;
                        }
                        else if ((success = double.TryParse(rest[i], out amount)))
                            break;
                    }

                    if (amount == 0)
                        amount = 1;

                    if (to_usd)
                        GetExchange(string.Format(".exc {0} {1} to USDT", amount, first), source, n);
                    else
                        GetExchange(string.Format(".exc {0} USDT to {1}", amount, first), source, n);
                }
            }
        }

        public void GetExchange(string args, string source, string n)
        {
            args = args.Substring(".exc".Length).Trim();
            var parts = args.Split(' ');

            if (!parts.Any(p => double.TryParse(p, out double tmp)))
            {
                SendMessage("Sorry, I couldn't understand that. Try something like .exc 100 usd to btc. Known currencies are: " + string.Join(", ", CryptoHandler.Tickers), source);
                return;
            }

            var amount = double.Parse(parts.First(p => double.TryParse(p, out double tmp)));

            var eligible = parts.Where(p => CryptoHandler.Tickers.Contains(p.ToUpper())).ToList();

            if (eligible.Count < 2)
            {
                SendMessage("Sorry, I couldn't understand that. Try something like .exc 100 usd to btc. Known currencies are: " + string.Join(", ", CryptoHandler.Tickers), source);
                return;
            }

            try
            {
                SendMessage(CryptoHandler.Convert(eligible[0], eligible[1], amount), source);
            }
            catch (Exception ex)
            {
                SendMessage(string.Format("Exception occurred: {0}", ex.Message), source);
            }
        }
    }
}
