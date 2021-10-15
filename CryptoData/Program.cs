using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            Name = "crypto";
            //SmartAlertManager.Load();

            Commands = new Dictionary<string, MessageHandler>()
            {
                {"", Wildcard },
                {".exc", GetExchange },
                {".statage ", GetOldestStat},
                {".stat ", GetNearestStat },
                {"$flush", (a, s, n) => { SendMessage(string.Format("Saved {0} entries.", TickerDataManager.Save()), s); SendMessage(string.Format("Total cache entries: {0}", TickerDataManager.Buffers.Buffers.Sum(b => b.Value.Cached)), s); GC.Collect(); } }
            };

            Init(args, delegate
            {
                CryptoHandler.Init();
                TickerDataManager.Init();
            });
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

            SendMessage(string.Format("Oldest ticker data for {0}: {1:##,#0.########}, {2} old, {3}, {4}, index {5}", ticker, data.LastTrade, Utilities.TimeSpanToPrettyString(DateTime.Now - data.Timestamp), data.Timestamp, rawtime, index), source);
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
