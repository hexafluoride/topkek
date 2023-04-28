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
                string cache_id = "weather:" + lat.ToString("0.000000") + "," + lng.ToString("0.000000") + "#" + unit_code;

                try
                {
                    var item = LinkResolver.Cache.Get(cache_id);

                    if (item != null)
                    {
                        return item.Content;
                    }

                    var raw = Client.DownloadString(string.Format(
                        "https://dev.pirateweather.net/forecast/{0}/{1},{2}?units={3}", Config.GetString("weather.key"), lat,
                        lng, unit_code));
                    var resp = JObject.Parse(raw);

                    //Console.WriteLine(raw);


                    var currently = (JObject) resp["currently"];
                    var minutely = resp["minutely"];
                    var hourly = resp["hourly"];
                    var daily = resp["daily"];
                    
                    var iconMap = new Dictionary<string, string>()
                    {
                        {"rain", "🌧️"},
                        {"clear-day", "☀️"},
                        {"clear-night", "🌙"},
                        {"cloudy", "☁️"},
                        {"partly-cloudy-day", "⛅"},
                        {"snow", "🌧️"},
                        {"sleet", "🌨️️"},
                        {"wind", "💨"},
                        {"fog", "🌁"},
                        {"partly-cloudy-night", "️☁"},
                    };
                    var emoji = "";

                    if (currently.ContainsKey("icon"))
                    {
                        var iconName = currently.Value<string>("icon");
                        if (iconMap.ContainsKey(iconName))
                            emoji = iconMap[iconName] + "  ";
                    }
                    
                    string format = $"{emoji}{{0}}, current temperature: \x02{{1}}\x02, feels like \x02{{2}}\x02";

                    List<string> summaries = new List<string>();

                    if (currently.ContainsKey("humidity"))
                        summaries.Add(string.Format("Humidity: \x02{0}%\x02",
                            (int)(currently.Value<double>("humidity") * 100d)));

                    if (currently.ContainsKey("windBearing") && currently.ContainsKey("windSpeed"))
                    {
                        var windSpeed = currently.Value<double>("windSpeed");
                        var windBearing = currently.Value<double>("windBearing");

                        var windDirectionString = windBearing switch
                        {
                            >= 315 or <= 45 => "N",
                            < 315 and >= 225 => "W",
                            < 225 and >= 135 => "S",
                            < 135 and > 45 => "E",
                            _ => "?"
                        };

                        if (preferMetric)
                            windSpeed *= 1.609;
                        
                        summaries.Add($"Wind: \x02{windSpeed:0.00} {(preferMetric ? "kph" : "mph")}\x02 {windDirectionString}");
                    }

                    // if (minutely != null)
                    //     summaries.Add(minutely.Value<string>("summary"));
                    //
                    // if (hourly != null)
                    //     summaries.Add(hourly.Value<string>("summary"));
                    //
                    // if (daily != null)
                    //     summaries.Add(daily.Value<string>("summary"));

                    var ret = string.Format(format,
                        currently.Value<string>("summary"),
                        FormatTemperature(currently.Value<double>("temperature"), preferMetric),
                        FormatTemperature(currently.Value<double>("apparentTemperature"), preferMetric));
                    
                    summaries.Insert(0, ret);
                    ret = string.Join(" \x000314-\x03 ", summaries);

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