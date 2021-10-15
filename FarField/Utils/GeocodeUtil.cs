using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public class GeocodeUtil
    {
        WebClient Client = new WebClient();

        public Tuple<double, double> GetLatLong(string human)
        {
            lock (Client)
            {
                human = HttpUtility.UrlEncode(human.ToLower());

                try
                {
                    var item = LinkResolver.Cache.Get("geocoder:" + human);

                    if (item != null)
                    {
                        var parts = item.Content.Split(',').Select(double.Parse).ToArray();
                        return new Tuple<double, double>(parts[0], parts[1]);
                    }

                    var raw = Client.DownloadString(string.Format("https://maps.googleapis.com/maps/api/geocode/json?address={0}&key={1}", human, Config.GetString("geocoding.key")));
                    var resp = JObject.Parse(raw);

                    Console.WriteLine(raw);

                    if (resp.Value<string>("status") != "OK")
                        return null;

                    var location = resp["results"][0]["geometry"]["location"];

                    LinkResolver.Cache.Add("geocoder:" + human, location.Value<double>("lat").ToString() + "," + location.Value<double>("lng").ToString(), TimeSpan.FromDays(365));

                    return new Tuple<double, double>(location.Value<double>("lat"), location.Value<double>("lng"));
                }
                catch (Exception ex)
                {
                    return null;
                }
            }
        }

        public string GetHuman(double lat, double lng, out string[] debug_info)
        {
            string[] preferred_types = new string[]
            {
                "postal_code",
                "administrative_area_level_2",
                "administrative_area_level_4",
                "administrative_area_level_1"
            };

            List<string> debug = new List<string>();
            debug_info = new string[0];

            lock (Client)
            {
                try
                {
                    var item = LinkResolver.Cache.Get(string.Format("r-geocoder:{0:0.0000},{1:0.0000}", lat, lng));

                    if (false && item != null)
                    {
                        return item.Content;
                    }

                    var raw = Client.DownloadString(string.Format("https://maps.googleapis.com/maps/api/geocode/json?latlng={0},{1}&key={2}", lat, lng, Config.GetString("geocoding.key")));
                    var resp = JObject.Parse(raw);

                    Console.WriteLine(raw);

                    if (resp.Value<string>("status") != "OK")
                        return null;

                    var results = resp["results"];
                    string location = "";

                    int best_order = -2;
                    int best_index = 0;

                    int index = 0;

                    foreach (var result in results)
                    {
                        debug.Add(string.Format("{0},{1}: {2} | {3}", lat, lng, result["formatted_address"].Value<string>(), string.Join(", ", result["types"].Select(t => t.Value<string>()))));
                        var value = result["formatted_address"].Value<string>();
                        var type = result["types"][0].Value<string>();
                        int preference = Array.IndexOf(preferred_types, type);

                        if (preference != -1)
                        {
                            if (best_order < 0 || preference < best_order)
                            {
                                best_order = preference;
                                location = value;
                                best_index = index;
                            }
                        }
                        else
                        {
                            if(best_order == -2)
                            {
                                best_order = -1;
                                location = value;
                                best_index = index;
                            }
                        }

                        index++;
                    }

                    for(int i = 0; i < index; i++)
                    {
                        if (i == best_index)
                            debug[i] = "[x] " + debug[i];
                        else
                            debug[i] = "[ ] " + debug[i];
                    }
                    
                    //var location = resp["results"][0]["formatted_address"].Value<string>();

                    LinkResolver.Cache.Add(string.Format("r-geocoder:{0:0.0000},{1:0.0000}", lat, lng), location, TimeSpan.FromDays(365));
                    debug_info = debug.ToArray();

                    return location;
                }
                catch (Exception ex)
                {
                    return null;
                }
            }
        }
    }
}