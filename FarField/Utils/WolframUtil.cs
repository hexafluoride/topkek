using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web;
using HeimdallBase;
using Newtonsoft.Json.Linq;

namespace FarField
{
    public partial class FarField
    {
        bool PrefersMetric(string source, string nick)
        {
            if (source.Contains('/'))
                source = source.Substring(0, source.IndexOf('/'));
            
            return GetUserDataForSourceAndNick<string>(source, nick, "unit_preference") == "metric";
        }
        
        WebClient wa_client = new WebClient();

        List<string> preferred_pods = new List<string>()
        {
            "Result",
            "Values",
        };

        List<string> preferred_scanners = new List<string>()
        {
            "Series"
        };

        string MakeWolframQuery(string query, bool metric = true, bool images = false)
        {
            return wa_client.DownloadString(string.Format("https://api.wolframalpha.com/v2/query?appid={0}&input={1}&output=json&format={3}plaintext&units={2}&plotwidth=1000&reinterpret=true", Config.GetString("wolfram.key"), HttpUtility.UrlEncode(query), metric ? "metric" : "nonmetric", images ? "image," : ""));
        }

        void Wolfram(string args, string source, string n)
        {
            if (args.StartsWith(">waplot"))
                return;

            string query = args.Substring(".wa".Length).Trim();
            string result = "";

            string suggestion = "";

            try
            {
                string response = MakeWolframQuery(query, PrefersMetric(source, n));
                var obj = JObject.Parse(response);

                Console.WriteLine(response);

                var queryresult = obj["queryresult"];

                if (!queryresult.Value<bool>("success")) // check for did you means
                {
                    if (queryresult["didyoumeans"] != null)
                    {
                        if (queryresult["didyoumeans"].Type == JTokenType.Array)
                        {
                            suggestion = queryresult["didyoumeans"].OrderByDescending(p => p.Value<double>("score")).First().Value<string>("val");
                        }
                        else
                            suggestion = queryresult["didyoumeans"].Value<string>("val");

                        response = MakeWolframQuery(suggestion, PrefersMetric(source, n));
                        obj = JObject.Parse(response);
                        queryresult = obj["queryresult"];
                    }
                    else
                        throw new Exception();
                }

                if(queryresult["warnings"] != null && queryresult["warnings"]["text"] != null)
                {
                    suggestion = queryresult["warnings"].Value<string>("text");

                    if (queryresult["warnings"]["new"] != null)
                        suggestion += " " + queryresult["warnings"].Value<string>("new");
                }

                var pods = queryresult["pods"];

                JToken result_pod = null;

                for (int i = 0; i < preferred_pods.Count; i++)
                {
                    string pod_id = preferred_pods[i];

                    if (pods.Any(p => p.Value<string>("id") == pod_id))
                    {
                        result_pod = pods.First(p => p.Value<string>("id") == pod_id);
                        break;
                    }
                }

                if (result_pod == null)
                {
                    for (int i = 0; i < preferred_scanners.Count; i++)
                    {
                        string scanner_id = preferred_scanners[i];

                        if (pods.Any(p => p.Value<string>("scanner") == scanner_id))
                        {
                            result_pod = pods.First(p => p.Value<string>("scanner") == scanner_id);
                            break;
                        }
                    }
                }

                if (result_pod == null && pods.Any(p => p.Value<string>("id") != "Input"))
                {
                    result_pod = pods.First(p => p.Value<string>("id") != "Input");
                }
                //var input_pod = preferred_pods.First(pods.Any(p => p.Value<string>("id") == "Input");
                var input_pod = pods.First(p => p.Value<string>("id") == "Input");

                result = string.Format("10{0} = 12{1}", input_pod["subpods"].First.Value<string>("plaintext"), result_pod["subpods"].First.Value<string>("plaintext").Replace("\n", " "));

                if (suggestion != "")
                    result += string.Format(" (11{0}12)", suggestion);
            }
            catch (Exception ex)
            {
                try
                {
                    result = wa_client.DownloadString(string.Format("https://api.wolframalpha.com/v1/result?appid={0}&i={1}&units=metric", Config.GetString("wolfram.key"), HttpUtility.UrlEncode(query)));
                }
                catch (Exception ex2)
                {
                    SendMessage("[4Wolfram] 4Couldn't display answer", source);
                    Console.WriteLine(ex);
                    Console.WriteLine(ex2);
                    return;
                }
            }

            SendMessage(string.Format("[4Wolfram] {1}", n, result), source);
        }

        void WolframPlot(string args, string source, string n)
        {
            List<string> preferred_pods = new List<string>()
            {
                "Plot",
                "Result",
            };

            List<string> preferred_scanners = new List<string>()
            {
                "Plotter"
            };

            if (args.StartsWith(">plot"))
                args = args.Substring(">plot".Length).Trim();
            else
                args = args.Substring(".waplot".Length).Trim();

            string query = args;
            string result = "";

            string suggestion = "";

            try
            {
                string response = MakeWolframQuery(query, PrefersMetric(source, n), true);
                var obj = JObject.Parse(response);

                var queryresult = obj["queryresult"];

                if (!queryresult.Value<bool>("success")) // check for did you means
                {
                    if (queryresult["didyoumeans"] != null)
                    {
                        if (queryresult["didyoumeans"].Type == JTokenType.Array)
                        {
                            suggestion = queryresult["didyoumeans"].OrderByDescending(p => p.Value<double>("score")).First().Value<string>("val");
                        }
                        else
                            suggestion = queryresult["didyoumeans"].Value<string>("val");

                        response = MakeWolframQuery(suggestion, PrefersMetric(source, n), true);
                        obj = JObject.Parse(response);
                        queryresult = obj["queryresult"];
                    }
                    else
                        throw new Exception();
                }

                var pods = queryresult["pods"];

                JToken result_pod = null;

                for (int i = 0; i < preferred_pods.Count; i++)
                {
                    string pod_id = preferred_pods[i];

                    if (pods.Any(p => p.Value<string>("id") == pod_id))
                    {
                        result_pod = pods.First(p => p.Value<string>("id") == pod_id);
                        break;
                    }
                }

                if (result_pod == null)
                {
                    for (int i = 0; i < preferred_scanners.Count; i++)
                    {
                        string scanner_id = preferred_scanners[i];

                        if (pods.Any(p => p.Value<string>("scanner") == scanner_id))
                        {
                            result_pod = pods.First(p => p.Value<string>("scanner") == scanner_id);
                            break;
                        }
                    }
                }

                if (result_pod == null && pods.Any(p => p.Value<string>("id") != "Input"))
                {
                    result_pod = pods.First(p => p.Value<string>("id") != "Input");
                }
                //var input_pod = preferred_pods.First(pods.Any(p => p.Value<string>("id") == "Input");
                var input_pod = pods.First(p => p.Value<string>("id") == "Input");

                result = string.Format("plot(10{0}) = {1}", input_pod["subpods"].First.Value<string>("plaintext"), result_pod["subpods"].First["img"].Value<string>("src").Replace("\n", " "));

                if (suggestion != "")
                    result += string.Format(" (suggested as 11did you mean \"{0}\"12)", suggestion);
            }
            catch (Exception ex)
            {
                //try
                //{
                //    result = wa_client.DownloadString(string.Format("https://api.wolframalpha.com/v1/result?appid={0}&i={1}&units=metric", Config.GetString("wolfram.key"), HttpUtility.UrlEncode(query)));
                //}
                //catch (Exception ex2)
                //{
                    SendMessage("[4Wolfram] 4Couldn't display answer", source);
                    Console.WriteLine(ex);
                    return;
                //}
            }

            SendMessage(string.Format("[4Wolfram] {1}", n, result), source);
        }
    }
}