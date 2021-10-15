using System;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public partial class FarField
    {
        void GetWeather(string args, string source, string n)
        {
            try
            {
                args = args.Substring(".weather".Length).Trim();
                var trimmedSource = source.Contains('/') ? source.Substring(0, source.IndexOf('/')) : source;
                
                bool hideLocation = GetUserDataForSourceAndNick<bool>(source, n, "weather.privacy");
                hideLocation = hideLocation || GetUserDataForSourceAndNick<bool>(source, "", "weather.privacy");
                
                string human = "";
                
                if (!string.IsNullOrWhiteSpace(args))
                {
                    human = args;
                    SetUserDataForSourceAndNick(trimmedSource, n, "weather.location", human);
                    hideLocation = false;
                }
                else
                {
                    human = GetUserDataForSourceAndNick<string>(trimmedSource, n, "weather.location");
                    if (human == default)
                    {
                        SendMessage("You need to specify a location with .weather <location> first.", source);
                        return;
                    }
                }

                var coords = GeocodeUtil.GetLatLong(human);

                if (hideLocation)
                    human = "your location";
                else
                    human = GeocodeUtil.GetHuman(coords.Item1, coords.Item2, out string[] debug_info);
                
                var results = WeatherUtil.TryGetSummary(coords.Item1, coords.Item2, PrefersMetric(source, n));

                SendMessage(string.Format("{0}, Weather for {1}: {2}", n, human, results), source);
            }
            catch (Exception ex)
            {
                SendMessage("Something happened: " + ex.Message, source);
            }
        }
    }
}