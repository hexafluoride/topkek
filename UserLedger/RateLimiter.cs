using System;
using System.Collections.Generic;

namespace UserLedger
{
    public class RateLimiter
    {
        public Dictionary<(string, string, string), DateTime> ActionLastPerformed = new();

        public RateLimiter()
        {
            
        }

        public bool CanAct((string, string, string) identifier, TimeSpan minInterval)
        {
            return !ActionLastPerformed.ContainsKey(identifier) ||
                   (DateTime.UtcNow - ActionLastPerformed[identifier]) > minInterval;
        }

        public void RecordAct((string, string, string) identifier) => ActionLastPerformed[identifier] = DateTime.UtcNow;
    }
}