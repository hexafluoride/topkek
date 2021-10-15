using System;
using System.Linq;
using System.Collections.Generic;
using System.Net.Http;
using Newtonsoft.Json;

namespace FarField
{
    public partial class FarField
    {
        public HoroscopeUtil HoroscopeUtil { get; set; }
        
        public void GetHoroscope(string args, string source, string n)
        {
            var trimmedSource = source.Substring(0, source.IndexOf('/'));
            args = args.Substring(".horoscope".Length).Trim();
            var sign = args;

            lock (HoroscopeUtil)
            {
                if (string.IsNullOrWhiteSpace(sign))
                    sign = GetUserDataForSourceAndNick<string>(trimmedSource, n, "horoscope.sign");
                else if (HoroscopeUtil.GetCacheId(sign) != null)
                    SetUserDataForSourceAndNick(trimmedSource, n, "horoscope.sign", sign);

                var cacheId = HoroscopeUtil.GetCacheId(sign);
                if (cacheId == null)
                {
                    SendMessage("Oops! You gave me an invalid star sign.", source);
                    return;
                }

                HoroscopeResult horoscope;
                var cached = LinkResolver.Cache.Get(cacheId);
                if (cached == null)
                {
                    horoscope = HoroscopeUtil.GetHoroscope(sign);

                    if (horoscope != null)
                        LinkResolver.Cache.Add(cacheId, JsonConvert.SerializeObject(horoscope), TimeSpan.FromDays(1));
                }
                else
                    horoscope = JsonConvert.DeserializeObject<HoroscopeResult>(cached.Content);

                if (horoscope == null)
                {
                    SendMessage("Oops! You gave me an invalid star sign.", source);
                    return;
                }

                var trimmedDate = horoscope.Date.Substring(0, horoscope.Date.IndexOf(','));
                var colorId = OsirisBase.Utilities.GetColor(horoscope.Color);
                SendMessage(
                    $"{trimmedDate} horoscope for {OsirisBase.Utilities.FormatWithColor(sign, colorId)}: {horoscope.Description} | " +
                    $"Color: {OsirisBase.Utilities.FormatWithColor(horoscope.Color, colorId)} | " +
                    $"Lucky number: 07{horoscope.LuckyNumber} | Lucky time: 11{horoscope.LuckyTime} | Mood: {OsirisBase.Utilities.FormatWithColor(horoscope.Mood, colorId)}",
                    source);
            }
        }
    }
    
    public class HoroscopeUtil
    {
        private HttpClient horoscopeClient { get; set; } = new HttpClient();
        private string[] ValidSigns = new string[]
        {
            "aries", "taurus", "gemini", "cancer", "leo", "libra", "virgo", "scorpio", "sagittarius", "capricorn", "aquarius", "pisces"
        };

        private string Endpoint = "https://aztro.sameerkumar.website/";
        
        public HoroscopeUtil() {}
        public HoroscopeResult GetHoroscope(string sign)
        {
            sign = sign.ToLower();
            var cacheId = GetCacheId(sign);

            if (cacheId == null)
                return null;

            try
            {
                var uri = new Uri($"{Endpoint}?sign={sign}&day=today");
                var response = horoscopeClient.PostAsync(uri, new StringContent("")).Result;
                return JsonConvert.DeserializeObject<HoroscopeResult>(response.Content.ReadAsStringAsync().Result);
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public string GetCacheId(string sign)
        {
            sign = sign.ToLower();
            if (!ValidSigns.Contains(sign))
                return null;
            return $"horoscope:{sign.ToLower()}@{DateTime.Now.ToShortDateString()}";
        }
    }

    public class HoroscopeResult
    {
        [JsonIgnore]
        public string CacheId { get; set; }
        
        [JsonProperty("current_date")]
        public string Date { get; set; }
        public string Description { get; set; }
        public string Mood { get; set; }
        public string Color { get; set; }
        
        [JsonProperty("lucky_number")]
        public string LuckyNumber { get; set; }
        
        [JsonProperty("lucky_time")]
        public string LuckyTime { get; set; }
    }
}