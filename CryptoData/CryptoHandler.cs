using Exchange;

using System;
using System.Numerics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Net;
using Newtonsoft.Json.Linq;
using NLog;
using BasicBuffer;
using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using NLog.Targets.Wrappers;
using OsirisBase;

namespace CryptoData
{
    class CryptoHandler
    {
        //static Dictionary<string, HashSet<string>> PairGraph = new Dictionary<string, HashSet<string>>();
        //static List<KeyValuePair<string, string>> ActualPairs = new List<KeyValuePair<string, string>>();
        //public static List<Ticker> 
        public static List<Ticker> Pairs = new List<Ticker>();
        public static HashSet<string> Tickers = new HashSet<string>();

        //public static BitfinexInstance Bitfinex = new BitfinexInstance();
        //public static BinanceInstance Binance = new BinanceInstance();

        static Logger Log = LogManager.GetCurrentClassLogger();
        static WebClient Client = new WebClient();
        static List<IExchange> PeckingOrder = new List<IExchange>();
        public static Dictionary<string, IExchange> Exchanges = new();

        public static void Init(bool actually_connect = true)
        {
            // Exchanges["Binance"] = new BinanceInstance();
            Exchanges["Kucoin"] = new KucoinInstance();
            
            //PopulatePairGraph();
            //Bitfinex.PopulatePairGraph("./bitfinex-pairs.txt");
            // (Exchanges["Binance"] as BinanceInstance).PopulatePairGraph();
            (Exchanges["Kucoin"] as KucoinInstance).PopulatePairGraph();

                foreach (var exchange in Exchanges.Values)
                {
                    if (actually_connect)
                        Task.Run(exchange.Connect);
                    Pairs.AddRange(exchange.ActualPairs.Select(p => new Ticker(p.Key, p.Value, exchange)));
                    //exchange.OnTickerUpdateReceived += TickerMan
                }
            

            /*Task.Run((Action)Bitfinex.Connect);
            Task.Run((Action)Binance.Connect);*/

            PeckingOrder = new List<IExchange>() { /*Bitfinex,*/ /*Exchanges["Binance"], */Exchanges["Kucoin"] };
            //Tickers = new HashSet<string>(Exchanges["Binance"].Currencies);
            Tickers = Exchanges.Values.SelectMany(e => e.Currencies).ToHashSet();
        }
        
        

        public static (string, string) GetBar(int start, int end, int high = -1, int low = -1, bool doRsi = false, (double, double, double, double) actualCandle = default)
        {
            (var entry, var exit, _, _) = actualCandle;
            var map = new char[] { ' ', '▁', '▂','▃','▄','▅','▆','▇','█' };
            //var cope_map = new string[] { "🭶","🭷","🭸","🭹","🭺","🭻" };
            var cope_map = new char[] { '⎺','⎻','⎼','⎽' };

            bool color_inv = false;

            if (end < start)
            {
                color_inv = true;
                var temp = end;
                end = start;
                start = temp;
            }

            if (high > low)
            {
                var temp = high;
                high = low;
                low = temp;
            }

            var color_r = color_inv ? 'g' : 'r';
            var color_i = color_inv ? 'j' : 'i';

            var color_orig_r = color_r;
            var color_orig_i = color_i;
            
            if (doRsi)
            {
                color_r = 'S';
                color_i = 'B';
            }

            var length = end - start;

            
            var start_full_offset = (int)Math.Floor(start / 9d);

            var output = "";
            var invert = "";

            output += new string('█', start_full_offset);
            invert += new string('0', start_full_offset);
            
            if (length < 9 && (start / 9 == end / 9))
            {
                if (length == 0)
                {
                    output += '█';
                    invert += '0';
                    goto final;
                }
                
                if (length <= 2)
                {
                    var start_reindexed = (int)((start % 9d) / 9d * cope_map.Length);
                    output += cope_map[start_reindexed];
                    invert += color_r;
                    goto final;
                }
                
                repeat:
                if (start % 9 == 0)
                {
                    output += map[8 - length];
                    invert += color_i;
                    goto final;
                }
                else if (end % 9 == 0)
                {
                    output += map[8 - length];
                    invert += color_r;
                    goto final;
                }
                else
                {
                    var start_dist = start - Math.Round(start / 9d);
                    var end_dist = end - Math.Round(end / 9d);

                    int nudge = start_dist > end_dist ? end % 9 : start % 9;
                    
                    start -= nudge;
                    end -= nudge;
                    goto repeat;
                }
            }
            /*if (end - start < 9)
            {
                
                return (new string(' ', start_full_offset) + map[(end - start)], new string(' ', start_full_offset) + (color_i));
            }*/
            
            var start_fraction = 8 - (start % 9);
            var consumed_by_start_fraction = 8 - (start % 9);
            
            var end_fraction = (end % 9);
            var full_length = ((end - end_fraction) - (start + start_fraction)) / 9;
            
            //output += new string(' ', start_full_offset);
            //invert += new string(color_r, start_full_offset);
            output += map[start_fraction];
            invert += color_r;

            if (full_length > 0)
            {
                output += new string(map[8], full_length);
                invert += new string(color_r, full_length);
            }
            output += map[8 - end_fraction];
            invert += color_i;
            
            final:

            if (high != -1 && low != -1)
            {
                var high_start = high / 9d;
                var high_end = start / 9d;

                if (high_end - high_start > 1d)
                {
                    for (int i = (int) Math.Floor(high_start); i < Math.Floor(high_end); i++)
                    {
                        if (output[i] != ' ')
                        {
                            Console.WriteLine($"Warn: replacing {output[i]} with high bar");
                        }

                        output = output.Remove(i, 1);

                        if (i == Math.Floor(high_start) && high % 9 > 4)
                        {
                            output = output.Insert(i, "╷");
                        }
                        else
                            output = output.Insert(i, "│");

                        invert = invert.Remove(i, 1);
                        invert = invert.Insert(i, "n");
                    }
                }                
                
                var low_start = low / 9d;
                var low_end = end / 9d;

                if (low_start - low_end > 1d)
                {
                    for (int i = (int) Math.Ceiling(low_end); i < Math.Floor(low_start); i++)
                    {
                        while (output.Length <= i)
                        {
                            output += '█';
                            invert += '0';
                        }

                        if (output[i] != ' ')
                        {
                            Console.WriteLine($"Warn: replacing {output[i]} with low bar");
                        }

                        output = output.Remove(i, 1);

                        if (i == Math.Floor(low_start) - 1 && low % 9 < 5)
                        {
                            output = output.Insert(i, "╵");
                        }
                        else
                        {
                            output = output.Insert(i, "│");
                        }

                        invert = invert.Remove(i, 1);
                        invert = invert.Insert(i, "n");
                    }
                }
            }

            return (output, invert);
        }

        public static void PrintCandlesticks(List<(string, string)> strs)
        {
            int k = 0;

            var actual_strs = strs.Select(s => s.Item1.EnumerateRunes().ToArray()).ToArray();
            
            while (true)
            {
                int printed_real = 0;

                for (int i = 0; i < strs.Count; i++)
                {
                    if (actual_strs[i].Length <= k)
                    {
                        Console.Write(' ');
                        continue;
                    }

                    printed_real++;

                    switch (strs[i].Item2[k])
                    {
                        case 'r':
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.BackgroundColor = ConsoleColor.Black;
                            break;
                        case 'g':
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.BackgroundColor = ConsoleColor.Black;
                            break;
                        case 'i':
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.BackgroundColor = ConsoleColor.Red;
                            break;
                        case 'j':
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.BackgroundColor = ConsoleColor.Green;
                            break;
                        case 'n':
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.BackgroundColor = ConsoleColor.Black;
                            break;
                    }

                    Console.Write(actual_strs[i][k]);
                    
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.BackgroundColor = ConsoleColor.Black;
                }

                Console.WriteLine();
                k++;

                if (printed_real == 0)
                    break;
            }
        }

        public static string[] IrcPrintCandlesticks(List<(string, string)> strs, double uppermost, double scale_per_line, double seconds_per_column, double current_price, bool printPercentageAxis = true)
        {
            int k = 0;

            //StringBuilder output = new StringBuilder();
            List<string> outlines = new List<string>();

            int last_fg = 0;
            int last_bg = 0;

            int next_fg = 1;
            int next_bg = 1;

            var total_rows = strs.Max(s => s.Item1.Length);

            int percentage_center = (int)Math.Floor((uppermost - current_price) / (9 * scale_per_line));
            
            string GetAxisLabel(int row)
            {
                int digits_required = (int)Math.Abs(Math.Log10(uppermost - (scale_per_line * row * 9)) - 5);
                return Math.Round(uppermost - (scale_per_line * row * 9), digits_required).ToString("#,#,#0.########");
            }

            string GetPercentageAxisLabel(int row)
            {
                double fraction = 0;
                if (row == percentage_center)
                    fraction = 1;
                else
                    fraction = (uppermost - (scale_per_line * row * 9)) / current_price;

                fraction -= 1d;
                return (fraction * 100d).ToString("0.00").PadLeft(4, ' ') + '%';
            }

            var label_len = Enumerable.Range(0, total_rows).Max(i => GetAxisLabel(i).Length);

            int rows_printed = 0;
            int row_every = 2;

            var percentage_offset = percentage_center % row_every;
            
            while (true)
            {
                int printed_real = 0;
                last_fg = 0;
                last_bg = 0;

                string curr_out = " │";

                if (rows_printed % row_every == 0)
                {
                    curr_out = $"{GetAxisLabel(rows_printed).PadRight(label_len, ' ')} ┤";
                }
                else
                {
                    curr_out = $"{new string(' ', label_len)} │";
                }

                double lineLevel = double.Parse(GetAxisLabel(rows_printed));

                for (int i = 0; i < strs.Count; i++)
                {
                    if (strs[i].Item1.Length <= k)
                    {
                        if (next_fg != last_fg || next_bg != last_bg)
                        {
                            curr_out += ((char) 3 + $"{next_fg},{next_bg}");
                            last_fg = next_fg;
                            last_bg = next_bg;
                        }
                        curr_out += ('█');
                        continue;
                    }

                    printed_real++;

                    var colorChar = strs[i].Item2[k];

                    if (colorChar == 'S' || colorChar == 'B')
                    {
                        if (lineLevel > 70)
                        {
                            colorChar = colorChar == 'S' ? 'g' : 'j';
                        }
                        else if (lineLevel < 30)
                        {
                            colorChar = colorChar == 'S' ? 'r' : 'i';
                        }
                    }

                    switch (colorChar)
                    {
                        case 'r':
                            next_fg = 4;
                            next_bg = 1;
                            break;
                        case 'g':
                            next_fg = 3;
                            next_bg = 1;
                            break;
                        case 'i':
                            next_fg = 1;
                            next_bg = 4;
                            break;
                        case 'j':
                            next_fg = 1;
                            next_bg = 3;
                            break;
                        case 'n':
                            next_fg = 0;
                            next_bg = 1;
                            break;
                        case '0':
                            next_fg = 1;
                            next_bg = 1;
                            break;
                        case 'S':
                            next_fg = 13;
                            next_bg = 1;
                            break;
                        case 'B':
                            next_fg = 1;
                            next_bg = 13;
                            break;
                    }

                    if (next_fg != last_fg || next_bg != last_bg)
                    {
                        curr_out += ((char) 3 + $"{next_fg},{next_bg}");
                        last_fg = next_fg;
                        last_bg = next_bg;
                    }

                    curr_out += (strs[i].Item1[k]);
                    
                    next_fg = 1;
                    next_bg = 1;
                }
                if (printed_real == 0)
                    break;

                curr_out += (char) 3;

                if (printPercentageAxis)
                {
                    if (rows_printed % row_every == percentage_offset)
                    {
                        curr_out += $"├ {GetPercentageAxisLabel(rows_printed)}";
                    }
                    else
                    {
                        curr_out += "│ ";
                    }
                }

                outlines.Add(curr_out);
                k++;
                rows_printed++;

            }

            var x_axis = $"{new string(' ', label_len)} └";

            var label_every = 10;
            
            for (int i = 0; i < strs.Count; i++)
            {
                if ((strs.Count - (i + 1)) % label_every != 0)
                {
                    x_axis += "─";
                }
                else
                {
                    x_axis += "┬";
                }
            }

            x_axis += "┘";
            outlines.Add(x_axis);
            var new_axis = $"{new string(' ', x_axis.Length)} ";
            var start = label_len + 2;
            
            for (int i = 0; i < strs.Count; i++)
            {
                if ((strs.Count - (i + 1)) % label_every != 0)
                {
                    continue;
                }

                var str_index = start + i;
                var amount_back_in_time = TimeSpan.FromSeconds((strs.Count - (i + 1)) * seconds_per_column);
                //var stringified = Utilities.TimeSpanToPrettyString(amount_back_in_time, true, false);
                var stringified = OsirisNext.Utilities.DisplaySeconds(((int) amount_back_in_time.TotalSeconds));

                if (string.IsNullOrWhiteSpace(stringified))
                    stringified = "now";
                    
                if (stringified.Contains(' '))
                {
                    stringified = stringified.Split(' ')[0];
                }

                if (stringified.Length == 3)
                {
                    new_axis = new_axis.Remove(start + i - 1, 3);
                    new_axis = new_axis.Insert(start + i - 1, stringified);
                }
                else
                {
                    new_axis = new_axis.Remove(start + (int)(i - Math.Floor(stringified.Length / 2d)), stringified.Length);
                    new_axis = new_axis.Insert(start + (int)(i - Math.Floor(stringified.Length / 2d)), stringified);

                    i += (int)Math.Ceiling(stringified.Length / 2d);
                }
            }

            outlines.Add(new_axis);

            return outlines.ToArray();
        }

        public static (List<double>, double, double) TransformRSI(List<(double, double, double, double)> data, int period)
        {
            double u = 0;
            double d = 0;
            double max = -1;
            double min = -1;
            double scale = 0;

            var pointsOut = new List<double>();
            double lastClose = data[0].Item2;
            
            for (int i = 0; i < data.Count; i++)
            {
                (var entry, var exit, var low, var high) = data[i];

                var localU = Math.Max(0, exit - lastClose);
                var localD = Math.Max(0, lastClose - exit);

                u = ((u * (period - 1)) + localU) / period;
                d = ((d * (period - 1)) + localD) / period;

                lastClose = exit;

                if (i >= (period * 2))
                {
                    if (d == 0)
                    {
                        d = double.Epsilon;
                    }
                    var relativeStrength = u / d;
                    var rsi = 100 - (100 / (1 + relativeStrength));
                    
                    pointsOut.Add(rsi);

                    if (max == -1 || rsi > max)
                        max = rsi;

                    if (min == -1 || rsi < min)
                        min = rsi;
                }
            }

            scale = max - min;

            return (pointsOut, max, scale);
        }

        public static (List<(string, string)>, double, double) RenderRSI(List<double> rsiPoints, double max, double scale)
        {
            var candlesticks = new List<(double, double, double, double)>();

            var lastRsi = rsiPoints[0];
            for (int i = 1; i < rsiPoints.Count; i++)
            {
                var rsi = rsiPoints[i];
                
                candlesticks.Add((lastRsi, rsi, Math.Min(lastRsi, rsi), Math.Max(lastRsi, rsi)));
                lastRsi = rsi;
            }

            return RenderCandlesticks(candlesticks, doRsi: true);
        }

        public static (List<(string, string)>, double, double) RenderCandlesticks(List<(double, double, double, double)> data, bool doRsi = false)
        {
            //var scale = data.Min(d => Math.Min(d.Item1, d.Item2));
            var min = -1d;
            var max = -1d;

            for (int i = 0; i < data.Count; i++)
            {
                (var entry, var exit, var low, var high) = data[i];

                if (entry == -1 || exit == -1)
                    continue;

                if (min == -1 || min > Math.Min(low, Math.Min(entry, exit)))
                    min = Math.Min(low, Math.Min(entry, exit));

                if (max == -1 || max < Math.Max(high, Math.Max(entry, exit)))
                    max = Math.Max(high, Math.Max(entry, exit));
            }

            var orig_min = min;

            var nudge = min * 0.005;

            min -= nudge;
            max += nudge / 3;

            var scale = (max - min) / (15 * 9);
            var floor = min;

            int Transform(double val)
            {
                return (int)(((max - min) - (val - floor)) / scale);
            }

            List<(string, string)> output = new List<(string, string)>();

            for (int i = 0; i < data.Count; i++)
            {
                (var entry, var exit, var low, var high) = data[i];

                if (entry == -1 || exit == -1)
                {
                    output.Add(("", ""));
                    continue;
                }
                
                output.Add(GetBar(Transform(entry), Transform(exit), Transform(high), Transform(low), doRsi));
            }

            return (output, max, scale);
        }
        
        public static Dictionary<int, string> YahooCandlestickIntervals = new()
        {
            {60, "1m"},
            {120, "2m"},
            {300, "5m"},
            {900, "15m"},
            {1800, "30m"},
            {3600, "1h"},
            {14400, "4h"},
            {86400, "1d"},
            {604800, "1w"},
            {2592000, "1M"},
            {2592000 * 3, "3M"},
            //{86400 * 365, "1y"},

            /*1m
                3m
                5m
                15m
                30m
                1h
            2h
            4h
            6h
            8h
            12h
            1d
            3d
            1w
            1M*/


        };

        private static Dictionary<string, double> appropriateInterval = new()
        {
            {"1d", 86400},
            {"5d", 5 * 86400},
            {"1mo", 30 * 86400},
            {"3mo", 90 * 86400},
            {"6mo", 180 * 86400},
            {"1y", 365 * 86400},
            {"2y", 730 * 86400},
            {"5y", 5 * 365 * 86400},
            {"10y", 10 * 365 * 86400},
            {"max", double.MaxValue}
        };

        public class YahooTickerInfo
        {
            public string Symbol { get; set; }
            public string LongName { get; set; }
            public string ShortName { get; set; }
            public string QuoteCurrency { get; set; }
        }

        public static Dictionary<string, YahooTickerInfo> YahooTickers = new();

        public static (TickerData, List<(double, double, double, double)>) GrabCandlestickDataFromYahoo(string ticker,
            string interval, DateTime start, int candle_count)
        {
            var originalInterval = interval;
            
            interval = interval.Replace("M", "mo");
            interval = interval.Replace("w", "wk");

            var startThing = (int)(start - DateTime.UnixEpoch).TotalSeconds;
            var endThing = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

            Console.WriteLine(originalInterval);
            Console.WriteLine(YahooCandlestickIntervals.FirstOrDefault(y => y.Value == originalInterval).Key);
            Console.WriteLine(appropriateInterval.Values.OrderBy(v => v).FirstOrDefault(f => f > (endThing - startThing)));

            var rightRange = appropriateInterval.Values.OrderBy(v => v)
                .FirstOrDefault(f => f > (endThing - startThing));
            //var rightRange = appropriateInterval.Values.OrderBy(v => v).FirstOrDefault(f => f > YahooCandlestickIntervals.FirstOrDefault(y => y.Value == originalInterval).Key);

            Console.WriteLine(rightRange);
            
            if (rightRange == 0)
                return (new TickerData(), new List<(double, double, double, double)>());

            var range = appropriateInterval.First(f => f.Value == rightRange).Key;
            // range = range.Replace("M", "mo");
            // range = range.Replace("w", "wk");
            
            var url =
                $"https://query1.finance.yahoo.com/v8/finance/chart/{ticker}?symbol={ticker}&range={range}&useYfid=true&interval={interval}&includePrePost=true&events=div|split|earn&lang=en-US&region=US&corsDomain=finance.yahoo.com";

            Console.WriteLine(url);
            
            var response = Client.DownloadString(url);
            var responseParsed = JObject.Parse(response);
            var relevant = responseParsed["chart"]["result"][0];
            var indicators = relevant["indicators"]["quote"][0];

            var sequenceLength = (indicators["close"] as JArray).Count;

            var oclh = new List<(double, double, double, double)>();

            double lastPrice = -1;

            for (int i = 0; i < sequenceLength; i++)
            {
                if (indicators["open"][i].Type == JTokenType.Null ||
                    indicators["close"][i].Type == JTokenType.Null ||
                    indicators["high"][i].Type == JTokenType.Null ||
                    indicators["low"][i].Type == JTokenType.Null
                    ) 
                {
                    if (lastPrice == -1)
                        continue;
                    
                    oclh.Add((lastPrice, lastPrice, lastPrice, lastPrice));
                    continue;
                }

                lastPrice = indicators["close"][i].Value<double>();
                
                oclh.Add((
                    indicators["open"][i].Value<double>(),
                    indicators["close"][i].Value<double>(),
                    indicators["low"][i].Value<double>(),
                    indicators["high"][i].Value<double>()
                    ));
            }

            if (!YahooTickers.ContainsKey(ticker.ToUpperInvariant()))
            {
                var tickerInfo = new YahooTickerInfo();
                var tickerUrl = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={ticker}";
                var tickerParsed = JObject.Parse(Client.DownloadString(tickerUrl))["quoteResponse"]["result"][0] as JObject;
                tickerInfo.Symbol = tickerParsed["symbol"].Value<string>();
                tickerInfo.ShortName = tickerParsed["shortName"].Value<string>();
                if (tickerParsed.ContainsKey("longName"))
                    tickerInfo.LongName = tickerParsed["longName"].Value<string>();
                else
                    tickerInfo.LongName = tickerInfo.ShortName;

                if (tickerParsed.ContainsKey("currency"))
                    tickerInfo.QuoteCurrency = tickerParsed["currency"].Value<string>();
                else
                    tickerInfo.QuoteCurrency = "USD";

                YahooTickers[ticker.ToUpperInvariant()] = tickerInfo;
                YahooTickers[tickerInfo.Symbol] = tickerInfo;
            }

            var tickerData = new TickerData()
            {
                LastTrade = relevant["meta"]["regularMarketPrice"].Value<double>(),
                Ticker = new Ticker(ticker.ToUpperInvariant(), YahooTickers[ticker.ToUpperInvariant()].QuoteCurrency)
            };

            if ((relevant["meta"] as JObject).ContainsKey("chartPreviousClose"))
                tickerData.DailyChangePercentage =
                    (tickerData.LastTrade / relevant["meta"]["chartPreviousClose"].Value<double>()) - 1;
            
            return (tickerData, oclh);
        }

        public static Dictionary<int, string> BinanceCandlestickIntervals = new()
        {
            {1, "1s"},
            {60, "1m"},
            {180, "3m"},
            {300, "5m"},
            {900, "15m"},
            {1800, "30m"},
            {3600, "1h"},
            {7200, "2h"},
            {14400, "4h"},
            {21600, "6h"},
            {28800, "8h"},
            {43200, "12h"},
            {86400, "1d"},
            {259200, "3d"},
            {604800, "1w"},
            {2592000, "1M"},

            /*1m
                3m
                5m
                15m
                30m
                1h
            2h
            4h
            6h
            8h
            12h
            1d
            3d
            1w
            1M*/


        };

        public static List<(double, double, double, double)> GrabCandlestickDataFromBinance(Ticker ticker,
            TimeSpan candle_width, int candle_count, DateTime start)
        {
            if (!BinanceCandlestickIntervals.ContainsKey((int) candle_width.TotalSeconds))
                throw new Exception($"No defined interval found matching {candle_width.TotalSeconds} seconds");

            var interval = BinanceCandlestickIntervals[(int) candle_width.TotalSeconds];
            var startTime = (start - DateTime.UnixEpoch).Milliseconds;

            var response =
                Client.DownloadString(
                    $"https://api.binance.com:36969/api/v3/klines?symbol={ticker.First}{ticker.Second}&interval={interval}&limit={candle_count}");//&startTime={startTime}");

            var parsed = JArray.Parse(response);

            List<(double, double, double, double)> out_pairs = new List<(double, double, double, double)>();
            foreach (var point in parsed)
            {
                var pointParsed = point as JArray;
                
                // open, close, low, high
                out_pairs.Add((
                    double.Parse(pointParsed[1].Value<string>()),
                    double.Parse(pointParsed[4].Value<string>()),
                    double.Parse(pointParsed[3].Value<string>()),
                    double.Parse(pointParsed[2].Value<string>())
                    ));
            }

            return out_pairs;
        }

        public static List<(double, double, double, double)> GrabCandlestickData(Ticker ticker, TimeSpan candle_width, int candle_count, DateTime start)
        {
            var data = TickerDataManager.GetRangeForTicker(ticker, start, (start + (candle_count * candle_width)));

            if (TickerDataManager.Buffers.Buffers.ContainsKey(TickerDataManager.GetTickerId(ticker)))
            {
                var buf = TickerDataManager.Buffers.Buffers[TickerDataManager.GetTickerId(ticker)];
                buf.Flush();
            }

            var bins = new List<List<BufferElement>>();
            
            for (int i = 0; i < candle_count; i++)
                bins.Add(new List<BufferElement>());

            for (int i = 0; i < data.Count; i++)
            {
                var point = data[i];
                var point_time = DateTime.UnixEpoch.AddSeconds(point.Timestamp);

                var bin_index = (int)Math.Floor((point_time - start) / candle_width);

                if (bin_index >= candle_count || bin_index < 0)
                {
                    Console.WriteLine($"Point @ {point_time} is out of range (bin index is {bin_index} out of {candle_count} bins)!");
                    continue;
                }

                bins[bin_index].Add(point);
            }

            double last_price = -1;
            List<(double, double, double, double)> out_pairs = new List<(double, double, double, double)>();

            for (int i = 0; i < bins.Count; i++)
            {
                var bin = bins[i];

                if (!bin.Any())
                {
                    out_pairs.Add((-1, -1, -1, -1));
                    continue;
                }

                var bin_ordered = bin.OrderBy(b => b.Timestamp).ToList();

                if (last_price == -1)
                    last_price = bin_ordered.First().Data;

                out_pairs.Add((last_price, bin_ordered.Last().Data, bin.Min(b => b.Data), bin.Max(b => b.Data)));
                last_price = bin_ordered.Last().Data;
            }

            return out_pairs;
            //return bins.Select(b => b.OrderBy(q => q.Timestamp)).Select(b => !b.Any() ? (-1, -1) : ((double)b.First().Data, (double)b.Last().Data)).ToList();
        }

        public static bool LooksLikeAddress(string addr)
        {
            string valid_chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            return ((addr.StartsWith("1") || addr.StartsWith("3")) &&
                addr.Length > 26 && addr.Length < 35 &&
                !addr.Any(a => !valid_chars.Contains(a)));
        }

        public static bool LooksLikeTxid(string tx)
        {
            string valid_chars =
                   "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            return (tx.Length == 64 &&
                !tx.Any(a => !valid_chars.Contains(a)));
        }

        public static string GetAddressInfo(string addr)
        {
            string valid_chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            if ((!addr.StartsWith("1") && !addr.StartsWith("3")) || 
                addr.Length > 35 ||
                addr.Any(a => !valid_chars.Contains(a)))
                return "That doesn't seem like a valid Bitcoin address.";

            string raw_response = Client.DownloadString(string.Format("https://blockchain.info/rawaddr/{0}", addr));
            var response = JObject.Parse(raw_response);

            double satoshi = 100000000d;

            double balance = response.Value<double>("final_balance") / satoshi;
            double received = response.Value<double>("total_received") / satoshi;
            double sent = response.Value<double>("total_sent") / satoshi;

            double btcusd = GetCurrentTickerData(PeckingOrder[0], new Ticker("BTC", "USDT")).LastTrade;

            double balance_usd = balance * btcusd;
            double received_usd = received * btcusd;
            double sent_usd = sent * btcusd;

            int n_tx = response.Value<int>("n_tx");

            //return string.Format("Bitcoin address {0}: {1} BTC/{2} USD balance after {3} transactions, {4} BTC/{5} USD received, {6} BTC/{7} USD sent", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
            //return string.Format("Bitcoin address {0}: 07{1:##,#0.########} BTC/03${2:##,#0.##} USD balance after {3} transactions, 07{4:##,#0.########} BTC/03${5:##,#0.##} USD received, 07{6:##,#0.########} BTC/03${7:##,#0.##} USD sent", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
            //return string.Format("Bitcoin address {0} has a balance of 07{1:##,#0.########} BTC/03${2:##,#0.##} USD after receiving 07{4:##,#0.########} BTC/03${5:##,#0.##} USD, and sending 07{6:##,#0.########} BTC/03${7:##,#0.##} USD in {3} transactions.", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
            return string.Format("Bitcoin address {0} has a balance of 07{1:##,#0.########} BTC(03${2:##,#0.##}) after receiving 07{4:##,#0.########} BTC(03${5:##,#0.##}) and sending 07{6:##,#0.########} BTC(03${7:##,#0.##}) in {3} transactions.", addr, balance, balance_usd, n_tx, received, received_usd, sent, sent_usd);
        }

        static DateTime last_block_query = DateTime.Now;
        static int last_block_height = -1;
        static string last_block_hash = "";
        static DateTime last_block_time = DateTime.Now;

        static int GetLatestBlock()
        {
            if (last_block_height > 0 && (DateTime.Now - last_block_query).TotalMinutes > 5)
                return last_block_height;

            string raw_response = Client.DownloadString("https://blockchain.info/latestblock");
            var response = JObject.Parse(raw_response);

            last_block_height = response.Value<int>("height");
            last_block_hash = response.Value<string>("hash");
            last_block_time = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(response.Value<long>("time"));
            last_block_query = DateTime.Now;

            return last_block_height;
        }

        public static Ticker TickerFromString(string ticker)
        {
            var exchange = ticker.Substring(ticker.IndexOf('@') + 1);
            // (exchange)
            exchange = exchange.ToLowerInvariant();
            exchange = char.ToUpper(exchange[0]) + exchange.Substring(1);
            
            var rest = ticker.Substring(0, ticker.Length - (exchange.Length + 1));

            var left = rest.Split(':')[0];
            var right = rest.Split(':')[1];
            return new Ticker(left, right, exchange);
            //return PeckingOrder[0].TickerFromString(ticker);
        }

        public static string GetTransactionInfo(string tx)
        {
            double satoshi = 100000000d;
            string valid_chars =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            if (tx.Length != 64 ||
                tx.Any(a => !valid_chars.Contains(a)))
                return "That doesn't seem like a valid Bitcoin transaction ID.";

            string raw_response = Client.DownloadString(string.Format("https://blockchain.info/rawtx/{0}", tx));
            var response = JObject.Parse(raw_response);

            int block_height = GetLatestBlock();

            string confirm_status = "";

            if (!response.TryGetValue("block_height", out JToken tx_block))
                confirm_status = "4Unconfirmed transaction";
            else
            {
                int confirmations = block_height - (int)tx_block;
                if (confirmations < 6)
                {
                    confirm_status = string.Format("Transaction with 08{0} confirmations", confirmations);
                }
                else
                {
                    confirm_status = string.Format("Transaction with 03{0} confirmations", confirmations);
                }
            }


            string input_addr = response["inputs"][0]["prev_out"].Value<string>("addr");
            int out_count = response["out"].Count();

            double total_transacted = response["inputs"].Sum(i => i["prev_out"].Value<long>("value") / satoshi);
            double total_transacted_usd = total_transacted * GetCurrentTickerData(PeckingOrder[0], new Ticker("BTC", "USDT")).LastTrade;

            string estimated_output = "";
            double estimated_btc = 0;
            double estimated_usd = 0;

            try
            {
                estimated_output = response["out"].First(o => o.Value<int>("n") == 1).Value<string>("addr");
                estimated_btc = response["out"].First(o => o.Value<int>("n") == 1).Value<long>("value") / satoshi;

                estimated_usd = estimated_btc * GetCurrentTickerData(PeckingOrder[0], new Ticker("BTC", "USDT")).LastTrade;
            }
            catch
            {

            }

            string transacted_info = string.Format(", total {0:##,#0.########} BTC/{1:##,#0.##} USD transacted", total_transacted, total_transacted_usd);

            return string.Format("{0} from input {1} to {2} outputs{3}{4}",
                confirm_status,
                input_addr,
                out_count,
                estimated_output != "" ? string.Format(", estimated path: {0} --- {1:##,#0.########} BTC/{2:##,#0.##} USD ---> {3}", input_addr, estimated_btc, estimated_usd, estimated_output)
                : "",
                transacted_info);
        }

        //public static bool TickerExists(string ticker)
        //{
        //    if (ticker.Length < 6 || ticker.Length > 7)
        //        return false;

        //    ticker = ticker.Trim().TrimStart('t');
        //    ticker = ticker.ToUpper();

        //    string first3 = ticker.Substring(0, 3);
        //    string last3 = ticker.Substring(3, 3);

        //    return (Tickers.Any(t => t.StartsWith(first3)) && Tickers.Any(t => t.StartsWith(last3)));
        //}

        public static string GetLastBlockInfo()
        {
            GetLatestBlock();
            return string.Format("Last block has height {0}, hash {1}, mined {2} ago", last_block_height, last_block_hash, Utilities.TimeSpanToPrettyString(DateTime.UtcNow - last_block_time));
        }

        public static double ConvertPrice(string first, string second, double value)
        {
            first = first.ToUpper();
            second = second.ToUpper();

            //var path = FindPath(first, second, out string graph, out IExchange exchange);
            var pair = new KeyValuePair<string, string>();
            IExchange exchange = null;
            
            foreach (var e in PeckingOrder)
            {
                if (e.PairGraph[first].Contains(second))
                {
                    //Log.Debug("Direct pair exists: {0}:{1}", start_ticker, end_ticker);
                    //return new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>(start_ticker, end_ticker) };
                    exchange = e;
                    pair = new KeyValuePair<string, string>(first, second);
                    break;
                }
            }

            if (exchange == null)
                return -1;

            // if (path == null)
            //     return -1;

            double temp_value = value;

            //var pair = path[0];
            double age = 0;
            TickerData ticker_data = null;

            if (exchange.ActualPairs.Any(p => p.Key == pair.Key && p.Value == pair.Value))
            {
                var ticker = new Ticker(pair.Key, pair.Value);

                ticker_data = GetCurrentTickerData(exchange, ticker);
                double price = ticker_data.LastTrade;
                temp_value *= price;

                age = (DateTime.UtcNow - ticker_data.Timestamp).TotalSeconds;

                Log.Debug("Price of t{0}{1} is {2}", pair.Key, pair.Value, price);
            }
            else if (exchange.ActualPairs.Any(p => p.Key == pair.Value && p.Value == pair.Key))
            {
                var ticker = new Ticker(pair.Value, pair.Key);

                ticker_data = GetCurrentTickerData(exchange, ticker);
                double price = ticker_data.LastTrade;
                temp_value /= price;

                age = (DateTime.UtcNow - ticker_data.Timestamp).TotalSeconds;

                Log.Debug("Price of t{0}{1} is {2}", pair.Value, pair.Key, price);
            }

            if (age > 30)
                exchange.Reconnect();

            return temp_value;
        }

        public static string Convert(string first, string second, double value)
        {
            Stopwatch sw = Stopwatch.StartNew();

            first = first.ToUpper();
            second = second.ToUpper();

            var path = FindPath(first, second, out string graph, out IExchange exchange);

            if (path == null)
                return string.Format("Couldn't find path from ticker {0} to {1}.", first, second);

            double temp_value = value;

            if (path.Count == 1)
            {
                var pair = path[0];
                double age = 0;
                TickerData ticker_data = null;

                if (exchange.ActualPairs.Any(p => p.Key == pair.Key && p.Value == pair.Value))
                {
                    var ticker = new Ticker(pair.Key, pair.Value);

                    ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value *= price;

                    age = (DateTime.UtcNow - ticker_data.Timestamp).TotalSeconds;

                    Log.Debug("Price of t{0}{1} is {2}", pair.Key, pair.Value, price);
                }
                else if (exchange.ActualPairs.Any(p => p.Key == pair.Value && p.Value == pair.Key))
                {
                    var ticker = new Ticker(pair.Value, pair.Key);

                    ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value /= price;

                    age = (DateTime.UtcNow - ticker_data.Timestamp).TotalSeconds;

                    Log.Debug("Price of t{0}{1} is {2}", pair.Value, pair.Key, price);
                }

                if (age > 30)
                    exchange.Reconnect();

                if (ticker_data.DailyVolume == 0)
                {
                    return string.Format("{0} {1} = {2:##,#0.########} {3} // (direct pair, ticker data is {4}, {5}s old)", value, first, temp_value, second,
                        age < 15 ? "3fresh" :
                        age < 30 ? "8stale" :
                        "4expired, attempting to reconnect", (int)age);
                }

                return string.Format("{0} {1} = {2:##,#0.########} {3} // 24h stats: high 03{6:##,#0.########}, low 04{7:##,#0.########}, volume {8:##,#}, change {9} (direct pair, ticker data is {4}, {5}s old)", value, first, temp_value, second, 
                    age < 15 ? "3fresh" :
                    age < 30 ? "8stale" :
                               "4expired, attempting to reconnect", (int)age,
                    ticker_data.DailyHigh,
                    ticker_data.DailyLow,
                    ticker_data.DailyVolume,
                    string.Format(ticker_data.DailyChangePercentage > 0 ? "03↑{0:0.##}%" : "04↓{0:0.##}%", ticker_data.DailyChangePercentage * 100));
            }

            //Dictionary<string, double> ages = new Dictionary<string, double>();
            List<KeyValuePair<Ticker, double>> ages = new List<KeyValuePair<Ticker, double>>();
            bool[] reverse = new bool[path.Count];
            int q = 0;
            
            foreach (var pair in path)
            {
                if (exchange.ActualPairs.Any(p => p.Key == pair.Key && p.Value == pair.Value))
                {
                    var ticker = new Ticker(pair.Key, pair.Value, exchange);

                    var ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value *= price;
                    reverse[q++] = false;

                    ages.Add(new KeyValuePair<Ticker, double>(ticker, (DateTime.UtcNow - ticker_data.Timestamp).TotalSeconds));

                    Log.Debug("Price of t{0}{1} is {2}", pair.Key, pair.Value, price);
                }
                else if (exchange.ActualPairs.Any(p => p.Key == pair.Value && p.Value == pair.Key))
                {
                    var ticker = new Ticker(pair.Value, pair.Key, exchange);

                    var ticker_data = GetCurrentTickerData(exchange, ticker);
                    double price = ticker_data.LastTrade;
                    temp_value /= price;
                    reverse[q++] = true;

                    ages.Add(new KeyValuePair<Ticker, double>(ticker, (DateTime.UtcNow - ticker_data.Timestamp).TotalSeconds));

                    Log.Debug("Price of t{0}{1} is {2}", pair.Value, pair.Key, price);
                }
                else
                {
                    Log.Warn("Invalid pair {0}:{1}", pair.Key, pair.Value);
                    throw new Exception("Invalid pair");
                }
            }

            var pairs_with_buffer = new Dictionary<Ticker, List<BufferElement>>();

            int longest_buffer = 0;
            int shortest_buffer = int.MaxValue;

            float first_value = -1;
            float low_value = float.MaxValue;
            float high_value = 0;

            foreach (var pair in ages)
            {
                bool reverse_s = !exchange.ActualPairs.Any(p => p.Key == pair.Key.First && p.Value == pair.Key.Second);

                var ticker = reverse_s ? new Ticker(pair.Key.Second, pair.Key.First) { Exchange = pair.Key.Exchange } : pair.Key;
                var buffer = TickerDataManager.GetRangeForTicker(ticker, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

                if(buffer == null)
                {
                    goto calc_end;
                }

                longest_buffer = Math.Max(longest_buffer, buffer.Count);
                shortest_buffer = Math.Min(shortest_buffer, buffer.Count);

                pairs_with_buffer.Add(pair.Key, buffer);
            }

            var low_combination = new List<KeyValuePair<Ticker, BufferElement>>();
            var high_combination = new List<KeyValuePair<Ticker, BufferElement>>();

            Ticker[] tickers = ages.Select(p => p.Key).ToArray();
            Dictionary<Ticker, double> current_values = new Dictionary<Ticker, double>();
            int[] indices = new int[tickers.Length];
            int total_length = pairs_with_buffer.Sum(p => p.Value.Count);

            for (int j = 0; j < tickers.Length; j++)
            {
                var ticker = tickers[j];

                current_values[ticker] = pairs_with_buffer[ticker][0].Data;
                //reverse[j] = !exchange.ActualPairs.Any(p => p.Key == ticker.First && p.Value == ticker.Second);
            }

            for(int i = 0; i < total_length; i++)
            {
                int earliest_ticker_index = -1;
                Ticker earliest_ticker = new Ticker("", "");
                BufferElement earliest_data = new BufferElement(int.MaxValue, 0);

                for(int j = 0; j < tickers.Length; j++)
                {
                    var ticker = tickers[j];

                    if (indices[j] >= pairs_with_buffer[ticker].Count)
                        continue;

                    var data = pairs_with_buffer[ticker][indices[j]];

                    if(data.Timestamp < earliest_data.Timestamp)
                    {
                        earliest_data = data;
                        earliest_ticker = ticker;
                        earliest_ticker_index = j;
                    }
                }

                current_values[earliest_ticker] = earliest_data.Data;

                double temp_val = value;
                for (int j = 0; j < tickers.Length; j++)
                {
                    var ticker = tickers[j];
                    //var data_point = pairs_with_buffer[ticker][indices[j]];
                    var data_point = current_values[ticker];
                    
                    if (reverse[j])
                        temp_val /= data_point;
                    else
                        temp_val *= data_point;
                }

                if (first_value == -1)
                    first_value = (float)temp_val;

                low_value = (float)Math.Min(temp_val, low_value);
                high_value = (float)Math.Max(temp_val, high_value);

                indices[earliest_ticker_index]++;
            }

            //for (int i = 0; i < longest_buffer; i++)
            //{
            //    double temp_val = value;
            //    int ref_timestamp = 0;

            //    for (int j = 0; j < tickers.Length; j++)
            //    {
            //        var ticker = tickers[j];
            //        bool reverse = !exchange.ActualPairs.Any(p => p.Key == ticker.First && p.Value == ticker.Second);
            //        var data_point = pairs_with_buffer[ticker][indices[j]];

            //        if(ref_timestamp == 0)
            //        {
            //            ref_timestamp = (int)data_point.Timestamp;
            //        }
            //        else
            //        {
            //            int current_diff = (int)Math.Abs(data_point.Timestamp - ref_timestamp);

            //            data_point = pairs_with_buffer[ticker][indices[j]];

            //        }

            //        if (reverse)
            //            temp_val /= pairs_with_buffer[ticker][indices[j]].Data;
            //        else
            //            temp_val *= pairs_with_buffer[ticker][indices[j]].Data;
            //    }

            //    if (first_value == -1)
            //        first_value = (float)temp_val;

            //    low_value = (float)Math.Min(temp_val, low_value);
            //    high_value = (float)Math.Max(temp_val, high_value);
            //}

            calc_end:

            float change_percentage = (float)(temp_value - first_value) / first_value;
            
            var oldest_pair = ages.Aggregate((l, r) => l.Value < r.Value ? l : r);

            if (oldest_pair.Value > 30)
                exchange.Reconnect();

            sw.Stop();

            if (low_value != float.MaxValue)
            {
                return string.Format("{0} {1} = {2:##,#0.########} {3} // 24h stats: high 03{8:##,#0.########}, low 04{9:##,#0.########}, change {11} (virtual pair({7}), ticker data is {4}, oldest ticker is {5}, {6}s old, {10:0.00}s)", value, first, temp_value, second,
                    oldest_pair.Value < 15 ? "3fresh" :
                    oldest_pair.Value < 30 ? "8stale" :
                               "4expired, attempting to reconnect", oldest_pair.Key, (int)oldest_pair.Value, graph, high_value, low_value, sw.ElapsedMilliseconds / 1000d,
                    string.Format(change_percentage > 0 ? "03↑{0:0.##}%" : "04↓{0:0.##}%", change_percentage * 100));
            }
            else
            {

                return string.Format("{0} {1} = {2:##,#0.########} {3} // (virtual pair({7}), ticker data is {4}, oldest ticker is {5}, {6}s old, {8:0.00}s)", value, first, temp_value, second,
                    oldest_pair.Value < 15 ? "3fresh" :
                    oldest_pair.Value < 30 ? "8stale" :
                               "4expired, attempting to reconnect", oldest_pair.Key, (int)oldest_pair.Value, graph, sw.ElapsedMilliseconds / 1000d);
            }
            //string.Join(" -> ", ages.Select(p => string.Format("({0}, {1}s)", p.Key, (int)p.Value)))
            //Console.WriteLine("{0} {1} = {2} {3}", value, first, temp_value, second);
        }

        static string GetTickerName(string first, string second)
        {
            return string.Format("t{0}{1}", first.ToUpper().Substring(0, 3), second.ToUpper().Substring(0, 3));
        }

        public static TickerData GetCurrentTickerData(IExchange exchange, Ticker pair)
        {
            return exchange.GetCurrentTickerData(pair);
            
            if ((DateTime.Now - exchange.LastMessage).TotalSeconds > 10)
            {
                exchange.Reconnect();
                Thread.Sleep(1000);
            }

            if (exchange.TickerData.ContainsKey(pair))
                return exchange.TickerData[pair];

            exchange.SubscribeToTicker(pair);

            int waited = 0;

            while (!exchange.TickerData.ContainsKey(pair) && ++waited < 20)
                Thread.Sleep(100);

            if (waited == 20 && (DateTime.Now - exchange.LastMessage).TotalSeconds > 10)
            {
                exchange.Reconnect();
                Thread.Sleep(1000);
                exchange.SubscribeToTicker(pair);
            }

            while (!exchange.TickerData.ContainsKey(pair) && ++waited < 100)
                Thread.Sleep(100);

            if(waited == 100)
                throw new Exception(string.Format("Couldn't get price for pair {0}, retry in a couple of seconds", pair));

            return exchange.TickerData[pair];
        }
        
        public static TickerData GetCurrentTickerData2(IExchange exchange, Ticker pair)
        {
            return exchange.GetCurrentTickerData(pair);

            if ((DateTime.Now - exchange.LastMessage).TotalSeconds > 10)
            {
                exchange.Reconnect();
                Thread.Sleep(1000);
            }

            if (exchange.TickerData.ContainsKey(pair))
                return exchange.TickerData[pair];
            
            return null;

            // exchange.SubscribeToTicker(pair);
            //
            // int waited = 0;
            //
            // while (!exchange.TickerData.ContainsKey(pair) && ++waited < 20)
            //     Thread.Sleep(100);
            //
            // if (waited == 20 && (DateTime.Now - exchange.LastMessage).TotalSeconds > 10)
            // {
            //     exchange.Reconnect();
            //     Thread.Sleep(1000);
            //     exchange.SubscribeToTicker(pair);
            // }
            //
            // while (!exchange.TickerData.ContainsKey(pair) && ++waited < 100)
            //     Thread.Sleep(100);
            //
            // if(waited == 100)
            //     throw new Exception(string.Format("Couldn't get price for pair {0}, retry in a couple of seconds", pair));
            //
            // return exchange.TickerData[pair];
        }

        static int GetWeight(string ticker)
        {
            if (ticker == "USD" || ticker == "EUR" || ticker == "BNB" || ticker == "BTC" || ticker == "USDT")
                return 1;
            else if (ticker == "ETH")
                return 2;

            return 3;
        }

        static List<KeyValuePair<string, string>> FindPath(string start_ticker, string end_ticker, out string graph, out IExchange final_exchange)
        {
            foreach (var exchange in PeckingOrder)
            {
                Log.Debug("Trying {0}", exchange.ExchangeName);
                
                var path = FindPath(exchange, start_ticker, end_ticker, out graph);

                if (path != null)
                {
                    final_exchange = exchange;
                    return path;
                }
            }

            throw new Exception("Couldn't find path");
        }

        static List<KeyValuePair<string, string>> FindPath(IExchange exchange, string start_ticker, string end_ticker, out string graph)
        {
            graph = "";

            if (!exchange.PairGraph.ContainsKey(start_ticker))
            {
                Log.Debug("Unknown ticker {0}", start_ticker);
                return null;
            }

            if (!exchange.PairGraph.ContainsKey(end_ticker))
            {
                Log.Debug("Unknown ticker {0}", end_ticker);
                return null;
            }

            if (exchange.PairGraph[start_ticker].Contains(end_ticker))
            {
                Log.Debug("Direct pair exists: {0}:{1}", start_ticker, end_ticker);
                //return new List<KeyValuePair<string, string>>() { new KeyValuePair<string, string>(start_ticker, end_ticker) };
            }

            Dictionary<string, int> distance = new Dictionary<string, int>();
            Dictionary<string, bool> visited = new Dictionary<string, bool>();
            Dictionary<string, string> previous = new Dictionary<string, string>();

            HashSet<string> unvisited = new HashSet<string>(exchange.PairGraph.Keys);
            unvisited.Remove(start_ticker);

            foreach (var key in exchange.PairGraph.Keys)
            {
                distance[key] = int.MaxValue;
                visited[key] = false;
            }

            distance[start_ticker] = 0;

            string current_node = start_ticker;

            while (!visited[end_ticker] && unvisited.Any())
            {
                current_node = distance.Where(p => !visited[p.Key]).Aggregate((l, r) => l.Value < r.Value ? l : r).Key;
                var neighbors = exchange.PairGraph[current_node];

                foreach (var neighbor in neighbors)
                {
                    /*var ticker = exchange.TickerFromSymbol($"{current_node}{neighbor}");

                    if (!exchange.TickerData.ContainsKey(ticker))
                        ticker = exchange.TickerFromSymbol($"{neighbor}{current_node}");
                    
                    var age = !exchange.TickerAge.ContainsKey(ticker) ? 100 : (DateTime.Now - exchange.TickerAge[ticker]).TotalSeconds;
                    Log.Debug($"{ticker} is {age}s old");*/
                    
                    if (distance[neighbor] > (int)(distance[current_node] + GetWeight(current_node)))
                    {
                        distance[neighbor] = (int)(distance[current_node] + GetWeight(current_node));
                        previous[neighbor] = current_node;
                    }
                }

                visited[current_node] = true;
                unvisited.Remove(current_node);
            }

            Queue<string> best_path_q = new Queue<string>();

            current_node = end_ticker;

            while (current_node != start_ticker)
            {
                best_path_q.Enqueue(current_node);
                current_node = previous[current_node];
            }

            best_path_q.Enqueue(current_node);

            var best_path = best_path_q.Reverse().ToList();
            graph = string.Join(" -> ", best_path);

            Log.Debug("Best path from {0} to {1} is via {2}", start_ticker, end_ticker, string.Join(" -> ", best_path));

            var best_path_pairified = new List<KeyValuePair<string, string>>();

            for (int i = 0; i < best_path.Count - 1; i++)
            {
                best_path_pairified.Add(new KeyValuePair<string, string>(best_path[i], best_path[i + 1]));
            }

            return best_path_pairified;
        }
    }
}
