﻿using BasicBuffer;
using Exchange;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HeimdallBase;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace CryptoData
{
    public class TickerDataManager
    {
        //public static FileStream Stream { get; set; }
        public static List<IExchange> Exchanges { get; set; } = new();
        public static RingBufferCollection Buffers { get; set; }

        public static int SIZE = 86400 * 7;

        private static Dictionary<string, List<(string, int)>> BufferSizeOverride = new Dictionary<string, List<(string, int)>>();
        
        public static bool Loaded = false;

        public static Logger Log = LogManager.GetCurrentClassLogger();

        public static DateTime EpochTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static void Init(bool create_bufs = false)
        {
            if (Config.GetInt("crypto.tickers.default_size") > 0)
            {
                SIZE = Config.GetInt("crypto.tickers.default_size");
            }

            if (Config.GetValue<JObject>("crypto.tickers.size_override") != null)
            {
                var override_obj = Config.GetValue<JObject>("crypto.tickers.size_override");
                foreach (var pair in override_obj)
                {
                    if (pair.Value.Type == JTokenType.Integer)
                    {
                        BufferSizeOverride[pair.Key.ToUpperInvariant()] = new List<(string, int)>()
                            {("", pair.Value.Value<int>())};
                    }
                    else if (pair.Value.Type == JTokenType.Array)
                    {
                        if (!BufferSizeOverride.ContainsKey(pair.Key.ToUpperInvariant()))
                            BufferSizeOverride[pair.Key.ToUpperInvariant()] = new List<(string, int)>();
                        
                        foreach (var child in pair.Value as JArray)
                            BufferSizeOverride[pair.Key.ToUpperInvariant()].Add((child[0].Value<string>().ToUpperInvariant(), child[1].Value<int>()));   
                    }
                }
            }
            
            Log.Info($"Buffer size overrides: {JsonConvert.SerializeObject(BufferSizeOverride)}");
        
            if (!Directory.Exists("./tickers") || Directory.GetFiles("./tickers", "*.buf").Length == 0 || create_bufs)
            {
                Log.Info($"Creating buffers of size {SIZE} from scratch... DO NOT INTERRUPT");

                //Stream = new FileStream(Filename, FileMode.OpenOrCreate);
                Directory.CreateDirectory("./tickers/");
                Buffers = new RingBufferCollection("./tickers/");
                
                Buffers.LoadBuffers();
                
                if (!Buffers.Buffers.ContainsKey(""))
                    Buffers.AddBuffer("", 8);

                int q = 0;
                int total = CryptoHandler.Pairs.Count;
                int t = 0;

                foreach(var pair in CryptoHandler.Pairs)
                {
                    t++;
                    string id = string.Join("_", pair.First, pair.Second, pair.Exchange);
                    //var buffer = RingBuffer.Create(Stream, SIZE);
                    if (Buffers.Buffers.ContainsKey(id))
                    {
                        Log.Info($"Buffer {id} exists");
                        continue;
                    }

                    var size = SIZE;

                    if (BufferSizeOverride.ContainsKey(pair.First.ToUpperInvariant()))
                    {
                        foreach (var p in BufferSizeOverride[pair.First.ToUpperInvariant()])
                        {
                            if (p.Item1 == "" || p.Item1.Equals(pair.Second, StringComparison.InvariantCultureIgnoreCase))
                            {
                                size = p.Item2;
                            }
                        }
                    }
                    if (BufferSizeOverride.ContainsKey(pair.Second.ToUpperInvariant()))
                    {
                        foreach (var p in BufferSizeOverride[pair.Second.ToUpperInvariant()])
                        {
                            if (p.Item1 == "" || p.Item1.Equals(pair.First, StringComparison.InvariantCultureIgnoreCase))
                            {
                                size = p.Item2;
                            }
                        }
                    }
                            
                    
                    Buffers.AddBuffer(id, size);
                    //buffer.Save();

                    Log.Info($"Added buffer {id} of {size} elems ({t}/{total})");

                    //Buffers.Buffers[id].Save();
                    q++;

                    if (q % (Buffers.MaximumBufferCountPerFile / 2) == 0)
                    {
                        GC.Collect();
                        if (true || Console.KeyAvailable)
                        {
                            Log.Info($"Shutting down...");
                            Buffers.Save();
                            Buffers.Files.ForEach(f => f.Flush());
                            Buffers.Files.ForEach(f => f.Close());
                            Environment.Exit(0);
                        }
                    }
                    //Stream.Flush();
                }

                Buffers.Save();
            }

            Load();

            /*CryptoHandler.Binance.OnTickerUpdateReceived += HandleTickerUpdate;
            CryptoHandler.Bitfinex.OnTickerUpdateReceived += HandleTickerUpdate;*/

            Exchanges = CryptoHandler.Exchanges.Values.ToList();

            foreach (var exchange in Exchanges)
                exchange.OnTickerUpdateReceived += HandleTickerUpdate;

            Task.Factory.StartNew(delegate 
            {
                while(true)
                {
                    var buffers = Buffers.Buffers.Values.ToArray();
                    for (int i = 0; i < buffers.Length; i++)
                    {
                        var buf = buffers[i];
                        buf.Save();
                        if (i % 30 == 0)
                            Thread.Sleep(1000);
                    }
                    
                    Thread.Sleep(1000);
                    //int saved = Buffers.Save();
                    //Log.Debug("Saved {0} buffer entries.", saved);
                }
            });
        }

        public static TickerData TickerDataFromBufferElement(Ticker ticker, BufferElement element)
        {
            return new TickerData() { Ticker = ticker, Timestamp = (EpochTime.AddSeconds(element.Timestamp)).ToLocalTime(), LastTrade = element.Data };
        }

        public static List<BufferElement> GetRangeForTicker(Ticker ticker, DateTime start, DateTime end)
        {
            int start_time = (int)(start - EpochTime).TotalSeconds;
            int end_time = (int)(end - EpochTime).TotalSeconds;

            string id = GetTickerId(ticker);

            if (!Buffers.Buffers.ContainsKey(id))
            {
                Log.Error("Ticker {0} not found!", ticker);
                return null;
            }

            var buffer = Buffers.Buffers[id];
            var ret = new List<BufferElement>();

            for(int i = 0; i < buffer.Size; i++)
            {
                var element = buffer.Read(i);
                int time = (int)element.Timestamp;

                if (time < start_time)
                    continue;

                if (time > end_time)
                    continue;

                ret.Add(element);
            }

            Log.Info("Retrieved {0} elements from {1} between {2} and {3}", ret.Count, id, start_time, end_time);

            return ret;
        }

        public static TickerData GetTickerDataForTime(Ticker ticker, DateTime time)
        {
            string id = GetTickerId(ticker);

            if (!Buffers.Buffers.ContainsKey(id))
            {
                Log.Error("Ticker {0}/{1} not found!", ticker, id);
                return null;
            }

            var buffer = Buffers.Buffers[id];

            int search_timestamp = (int)(time.ToUniversalTime() - EpochTime).TotalSeconds;

            int low = 0;
            int high = buffer.Size;

            int location = 0;
            int last_location = -1;

            while (true)
            {
                location = (low + high) / 2;

                if (last_location == -1)
                    last_location = location;
                else if (last_location == location)
                    return TickerDataFromBufferElement(ticker, buffer.Read(location));
                else
                    last_location = location;

                int timestamp = (int)buffer.Read(location).Timestamp;

                if (timestamp == search_timestamp)
                    return TickerDataFromBufferElement(ticker, buffer.Read(location));

                if (timestamp > search_timestamp)
                {
                    high = location;
                }
                else
                    low = location;
            }
        }

        public static TickerData GetOldestTickerData(Ticker ticker, out int rawtime, out int index)
        {
            string id = GetTickerId(ticker);
            Log.Info(id);

            rawtime = -1;
            index = 0;

            try
            {
                if (!Buffers.Buffers.ContainsKey(id))
                {
                    Log.Error("Ticker {0}/{1} not found!", ticker, id);
                    return null;
                }

                var buffer = Buffers.Buffers[id];
                var data = buffer.Read(index);

                while (data.Timestamp == 0 && index < buffer.Size - 1)
                    data = buffer.Read(++index);
                //Log.Info("Read data for ticker {0}", id);
                rawtime = (int)data.Timestamp;
                return TickerDataFromBufferElement(ticker, data);
            }
            catch (Exception ex)
            {
                Log.Error("Exception occurred while reading ticker data: {0}", ex.Message);
                return null;
            }
        }

        public static string GetTickerId(Ticker ticker)
        {
            return string.Join("_", ticker.First, ticker.Second, ticker.Exchange);
        }
        
        private static void HandleTickerUpdate(Exchange.IExchange instance, Exchange.TickerData data)
        {
            if (!Loaded)
                return;

            string id = GetTickerId(data.Ticker);

            //Log.Debug(id);
            //return;

            if(!Buffers.Buffers.ContainsKey(id))
            {
                Log.Warn("Buffer {0} not found!", id);
                //var buffer = RingBuffer.Create(id, SIZE);
                Buffers.AddBuffer(id, SIZE);
                //buffer.Save();
                //Stream.Flush();
            }

            int timestamp = (int)(data.Timestamp.ToUniversalTime() - EpochTime).TotalSeconds;
            Buffers.Buffers[id].Write(new BufferElement(timestamp, (float)data.LastTrade));
        }

        public static void Load()
        {
            try
            {
                try
                {
                    //Stream.Close();
                    foreach (var buffersFile in Buffers.Files)
                    {
                        lock (buffersFile)
                        {
                            buffersFile.Close();
                        }
                    }
                }
                catch
                {

                }

                //Stream = new FileStream(Filename, FileMode.Open);
                
                Buffers = new RingBufferCollection("./tickers");
                Buffers.LoadBuffers();
                Loaded = true;
            }
            catch(Exception ex)
            {
                Log.Error("Error while loading: {0}", ex.Message);
            }
        }

        public static int Save()
        {
            try
            {
                return Buffers.Save();
            }
            catch (Exception ex)
            {
                Log.Error("Error while saving: {0}", ex.Message);
                return 0;
            }
        }
    }
}
