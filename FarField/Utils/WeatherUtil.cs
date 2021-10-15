using System;
using System.Collections.Generic;
using System.Net;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public class WeatherUtil
    {
        WebClient Client = new WebClient();

        public string TryGetSummary(double lat, double lng, bool preferMetric)
        {
            lock (Client)
            {
                string unit_code = preferMetric ? "si" : "us";
//                string unit_code = "si";
                string cache_id = "weather:" + lat.ToString() + "," + lng.ToString() + "#" + unit_code;

                try
                {
                    var item = LinkResolver.Cache.Get(cache_id);

                    if (item != null)
                    {
                        return item.Content;
                    }

                    var raw = Client.DownloadString(string.Format(
                        "https://api.darksky.net/forecast/{0}/{1},{2}?units={3}", Config.GetString("weather.key"), lat,
                        lng, unit_code));
                    var resp = JObject.Parse(raw);

                    //Console.WriteLine(raw);

                    string format = "{0}, current temperature: {1}, feels like {2}. {3}";

                    var currently = (JObject) resp["currently"];
                    var minutely = resp["minutely"];
                    var hourly = resp["hourly"];
                    var daily = resp["daily"];

                    List<string> summaries = new List<string>();

                    if (currently.ContainsKey("humidity") && currently.ContainsKey("dewPoint"))
                        summaries.Add(string.Format("Humidity: {0}%, dew point: {1}.",
                            currently.Value<double>("humidity") * 100d,
                            FormatTemperature(currently.Value<double>("dewPoint"), preferMetric)));

                    if (minutely != null)
                        summaries.Add(minutely.Value<string>("summary"));

                    if (hourly != null)
                        summaries.Add(hourly.Value<string>("summary"));

                    if (daily != null)
                        summaries.Add(daily.Value<string>("summary"));

                    var ret = string.Format(format,
                        currently.Value<string>("summary"),
                        FormatTemperature(currently.Value<double>("temperature"), preferMetric),
                        FormatTemperature(currently.Value<double>("apparentTemperature"), preferMetric),
                        string.Join(" ", summaries));

                    LinkResolver.Cache.Add(cache_id, ret, TimeSpan.FromMinutes(10));
                    return ret;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.StackTrace);
                    throw;
                }
            }
        }

        public string FormatTemperature(double temp, bool metric)
        {
            return $"{temp:0.}°{(metric ? "C" : "F")}";
        }
    }
}