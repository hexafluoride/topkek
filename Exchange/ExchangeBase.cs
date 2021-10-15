using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Exchange
{
    public delegate void TickerUpdate(IExchange instance, TickerData data);
    public delegate void ConnectEvent(IExchange instance);

    public interface IExchange
    {
        Dictionary<string, HashSet<string>> PairGraph { get; set; }
        List<KeyValuePair<string, string>> ActualPairs { get; set; }
        HashSet<string> Currencies { get; set; }

        event TickerUpdate OnTickerUpdateReceived;
        event ConnectEvent OnConnect;

        string ApiKey { get; set; }
        string ApiSecret { get; set; }
        string Endpoint { get; set; }

        string ExchangeName { get; }

        DateTime LastMessage { get; set; }

        Dictionary<Ticker, TickerData> TickerData { get; set; }
        Dictionary<Ticker, DateTime> TickerAge { get; set; }

        void Connect();
        void Reconnect();
        void SubscribeToTicker(Ticker ticker);
    }
}
