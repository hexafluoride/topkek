using Exchange;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoData
{/*
    public class SmartAlertManager
    {
        public static List<Ticker> WatchedTickers = new List<Ticker>();
        public static string Filename = "./watched-tickers.txt";

        public static double NotificationStartThreshold = 0.01; // percentage
        public static Logger Log = LoggerManager.GetCurrentClassLogger();
        public static Dictionary<Ticker, DateTime> LastNotificationSent = new Dictionary<Ticker, DateTime>();
        public static int TimeBack = 150000;

        public static void HandleTickerUpdate(IExchange exchange, TickerData data)
        {
            var ticker = data.Ticker;

            if (!WatchedTickers.Contains(data.Ticker))
                return;

            var lookback = DateTime.Now.AddMilliseconds(-TimeBack);

            if (LastNotificationSent.ContainsKey(ticker) && lookback < LastNotificationSent[ticker])
                return;

            var old_data = TickerDataManager.GetTickerDataForTime(ticker, lookback);
            var delta = old_data.LastTrade - data.LastTrade;

            //Log.Trace("TickerDelta for {0}: {1:0.00000000}, NotificationStartThreshold = {2:0.00000000}", ticker, delta / old_data.LastTrade, NotificationStartThreshold);

            if(Math.Abs(delta / old_data.LastTrade) > NotificationStartThreshold)
            {
                if (CryptoHandler.NotificationReleaser.Notifications.ContainsKey(ticker))
                {
                    var notification = CryptoHandler.NotificationReleaser.Notifications[ticker];
                    double current_absolute_change = notification.AbsoluteChange;
                    double old_price = notification.CurrentPrice;

                    notification.CurrentPrice = data.LastTrade;

                    if (notification.AbsoluteChange > current_absolute_change)
                    {
                        Log.Info("Updated notification for ticker {0}, old absolute change: {1:0.00000000}, new absolute change: {2:0.00000000}", ticker, current_absolute_change, notification.AbsoluteChange);
                        CryptoHandler.NotificationReleaser.AddNotification(ticker, notification);
                    }
                    else
                    {
                        Log.Info("Not updating for ticker {0}, old absolute change: {1:0.00000000}, new absolute change: {2:0.00000000}", ticker, current_absolute_change, notification.AbsoluteChange);
                        notification.CurrentPrice = old_price;
                    }
                    //else
                    //    notification.CurrentPrice = 
                }
                else
                {
                    Log.Debug("Created notification for ticker {0}", ticker);

                    var notification = new Notification(ticker, lookback, old_data.LastTrade, data.LastTrade);
                    CryptoHandler.NotificationReleaser.AddNotification(ticker, notification);
                }
            }
        }

        public static void AddTicker(Ticker ticker)
        {
            if (!WatchedTickers.Contains(ticker))
            {
                WatchedTickers.Add(ticker);
                Save();
            }
        }

        public static void Save()
        {
            Config.Save();
            //File.WriteAllLines(Filename, WatchedTickers.Select(GetTickerId));
        }

        public static void Load()
        {
            WatchedTickers = Config.GetArray<string>("crypto.smartwatch").Select(GetTicker).ToList();
            Log.Debug("Loaded {0} tickers:", WatchedTickers.Count);

            foreach (var ticker in WatchedTickers)
                Log.Debug("\t{0}", GetTickerId(ticker));

            CryptoHandler.NotificationReleaser.NotificationTimeout = Config.GetInt("crypto.notification.time");
            NotificationStartThreshold = Config.GetDouble("crypto.notification.threshold");
            TimeBack = Config.GetInt("crypto.smartwatch.timeback");
            //if(!File.Exists(Filename))

            //WatchedTickers = File.ReadAllLines(Filename).Where(t => !string.IsNullOrWhiteSpace(t)).Select(GetTicker).ToList();
        }

        public static string GetTickerId(Ticker ticker)
        {
            return string.Join("_", ticker.First, ticker.Second, ticker.Exchange);
        }

        public static Ticker GetTicker(string id)
        {
            var parts = id.Split('_');
            return new Ticker(parts[0], parts[1], parts[2]);
        }
    }

    public class Notification
    {
        public Ticker Ticker { get; set; }
        public DateTime AnalysisStart { get; set; }
        public double StartPrice { get; set; }

        public double Change { get { return CurrentPrice - StartPrice; } }
        public double ChangePercentage { get { return Change / StartPrice; } }

        public double AbsoluteChange { get { return Math.Abs(Change); } }
        public double AbsoluteChangePercentage { get { return Math.Abs(ChangePercentage); } }

        public double CurrentPrice { get; set; }

        public Timer Timer { get; set; }

        public Notification(Ticker ticker, DateTime start, double start_price, double current_price)
        {
            Ticker = ticker;
            AnalysisStart = start;
            StartPrice = start_price;
            CurrentPrice = current_price;
        }
    }

    public enum NotificationType
    {
        RisingPrice,
        FallingPrice
    }*/
}
