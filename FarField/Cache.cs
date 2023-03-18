using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace FarField
{
    public class TimedCache
    {
        public List<CacheItem> List = new List<CacheItem>();
        public static TimeSpan DefaultExpiry = TimeSpan.FromDays(1);

        public string LastHit { get; set; }
        
        public ulong RequestCount { get; set; }
        public ulong HitCount { get; set; }

        public void Load()
        {
            return;
            if (!File.Exists("./cache"))
                return;

            try
            {
                var formatter = new BinaryFormatter();
                using (FileStream fs = new FileStream("./cache", FileMode.OpenOrCreate))
                {
                    List = (List<CacheItem>)formatter.Deserialize(fs);
                }

                LoadStats();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to load cache.");
                Console.WriteLine(ex);
            }
        }

        public void Save()
        {
            return;
            
            try
            {
                var formatter = new BinaryFormatter();
                using (FileStream fs = new FileStream("./cache", FileMode.OpenOrCreate))
                {
                    formatter.Serialize(fs, List);
                }

                SaveStats();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to save cache.");
                Console.WriteLine(ex);
            }
        }

        public void LoadStats()
        {
            if (!File.Exists("./cache-stats"))
                return;

            var stats = File.ReadAllLines("./cache-stats");
            RequestCount = ulong.Parse(stats[0]);
            HitCount = ulong.Parse(stats[1]);
        }

        public void SaveStats()
        {
            // File.WriteAllLines("./cache-stats", new string[] { RequestCount.ToString(), HitCount.ToString() });
        }

        public TimedCache()
        {
            Load();
            Task.Factory.StartNew(PurgeLoop);
        }

        private void PurgeLoop()
        {
            while(true)
            {
                if (List.RemoveAll(item => item.Expired()) > 0)
                    Save();

                Thread.Sleep(10000);
            }
        }

        public void Add(string id, string content, TimeSpan expiry)
        {
            var item = Get(id, true);

            if (item != null)
            {
                item.Expiry = expiry;
                // item = new CacheItem(id, content, expiry);
                return;
            }

            List.Add(new CacheItem(id, content, expiry));
            Save();
        }

        public bool Remove(string id)
        {
            var item = Get(id);

            if (item != null)
            {
                List.Remove(item);
                Save();
                return true;
            }

            return false;
        }

        public CacheItem Get(string id, bool add = false)
        {
            if (!add)
                RequestCount++;

            foreach (var item in List)
            {
                if (item.ID == id)
                {
                    if (!add)
                        HitCount++;

                    LastHit = id;
                    return item;
                }
            }

            return null;
        }

        public bool GetAndExecute(string id, Action<CacheItem> action)
        {
            var item = Get(id);

            if (item == null)
                return false;

            action(item);
            return true;
        }
    }

    [Serializable]
    public class CacheItem
    {
        public string ID { get; set; }
        public string Content { get; set; }
        public DateTime Added { get; set; }
        public TimeSpan Expiry { get; set; }

        public CacheItem()
        {

        }

        public CacheItem(string id, string content, TimeSpan expires)
        {
            ID = id;
            Content = content;
            Added = DateTime.Now;
            Expiry = expires;
        }

        public bool Expired()
        {
            return (Added.Add(Expiry)) < DateTime.Now;
        }
    }
}