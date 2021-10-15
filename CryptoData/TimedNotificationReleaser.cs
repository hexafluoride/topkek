using Exchange;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoData
{/*
    public delegate void NotificationReleased(TimedNotificationReleaser releaser, Ticker ticker, Notification notification);

    public class TimedNotificationReleaser
    {
        public Dictionary<Ticker, Notification> Notifications = new Dictionary<Ticker, Notification>();
        public event NotificationReleased OnNotificationReleased;

        public Logger Log = LoggerManager.GetCurrentClassLogger();

        public int NotificationTimeout = 50000;

        public void AddNotification(Ticker ticker, Notification notification)
        {
            if(Notifications.ContainsKey(ticker))
            {
                var old_notification = Notifications[ticker];

                //if(notification.AbsoluteChange > old_notification.AbsoluteChange)
                    UpdateNotification(ticker, notification.CurrentPrice);
                return;
            }
            
            notification.Timer = new Timer(TimeoutHandler, ticker, NotificationTimeout, Timeout.Infinite);
            Notifications[ticker] = notification;
        }

        public void UpdateNotification(Ticker ticker, double new_price)
        {
            Log.Info("Updating notification for {0}", ticker);
            Notifications[ticker].CurrentPrice = new_price;
            Notifications[ticker].Timer.Change(NotificationTimeout, Timeout.Infinite);
        }

        public void TimeoutHandler(object ticker_obj)
        {
            var ticker = (Ticker)ticker_obj;

            if (!Notifications.ContainsKey(ticker))
                return;

            var notification = Notifications[ticker];

            notification.Timer.Change(Timeout.Infinite, Timeout.Infinite);
            notification.Timer.Dispose();
            Notifications.Remove(ticker);
            OnNotificationReleased?.Invoke(this, ticker, notification);
        }
    }*/
}
