using System;
using System.Collections.Generic;
using System.IO;
using Heimdall;
using OsirisBase;

namespace UserLedger
{
    public class LedgerOsirisModule : OsirisModule
    {
        private RateLimiter RateLimiter { get; set; } = new RateLimiter();
        private Dictionary<(string, string), TimeSpan> RateLimits = new();

        public override void ConnectionEstablished()
        {
            base.ConnectionEstablished();
            Connection.AddHandler("rehash", (c, m) => { RateLimits.Clear(); });
        }
        
        private TimeSpan GetRateLimit(string source, string action)
        {
            if (RateLimits.ContainsKey((source, action)))
                return RateLimits[(source, action)];

            var limit = GetUserDataForSourceAndNick<string>(source, "", $"ratelimit.{action}");
            if (string.IsNullOrWhiteSpace(limit) || !double.TryParse(limit, out double _))
                limit = "0";
            
            return RateLimits[(source, action)] = TimeSpan.FromMilliseconds(double.Parse(limit));
        }

        protected bool CanAct(string action, string source, string nick) =>
            RateLimiter.CanAct((action, source, nick), GetRateLimit(source, action));

        protected void MarkAct(string action, string source, string nick) =>
            RateLimiter.RecordAct((action, source, nick));

        public T GetUserDataForNick<T>(string nick, string key) => GetUserDataForSourceAndNick<T>("", nick, key);
        
        public T GetUserDataForSourceAndNick<T>(string source, string nick, string key)
        {
            using (var ms = new MemoryStream())
            {   
                ms.WriteString(source);
                ms.WriteString(nick);
                ms.WriteString(key);

                var resp = Connection.WaitFor(ms.ToArray(), "get_user_data", "ledger", "user_data");

                using (var respMs = new MemoryStream(resp))
                {
                    var result = respMs.ReadString();

                    if (string.IsNullOrWhiteSpace(result))
                        return default;

                    return (T)(Convert.ChangeType(result, typeof(T)) ?? default);
                }
            }
        }
        
        public void SetUserDataForNick(string nick, string key, object value) => SetUserDataForSourceAndNick("", nick, key, value);

        public void SetUserDataForSourceAndNick(string source, string nick, string key, object value)
        {
            using (var ms = new MemoryStream())
            {   
                ms.WriteString(source);
                ms.WriteString(nick);
                ms.WriteString(key);
                ms.WriteString(value.ToString());

                var resp = Connection.WaitFor(ms.ToArray(), "set_user_data", "ledger", "save_user_data");
                if (resp.Length == 0 || resp[0] != '+')
                {
                    Console.WriteLine($"Failed to set user data for {source}/{nick}:{key}");
                }
            }
        }
    }
}