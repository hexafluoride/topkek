using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace UserLedger
{
    public class Ledger
    {
        public Dictionary<string, Dictionary<string, string>> Data = new();
        private bool dirty = true;

        public Ledger()
        {
            
        }

        public string GetIdentifier(string source, string nick) => $"{source}/{nick}";

        public string FindIdentifier(string source, string nick, string key)
        {
            var localIdentifier = GetIdentifier(source, nick);
            var broadIdentifier = GetIdentifier(source.Split('/')[0], nick);
            var globalIdentifier = GetIdentifier("", nick);

            if (Data.ContainsKey(localIdentifier) && Data[localIdentifier].ContainsKey(key))
                return localIdentifier;
            
            if (Data.ContainsKey(broadIdentifier) && Data[broadIdentifier].ContainsKey(key))
                return broadIdentifier;
            
            if (Data.ContainsKey(globalIdentifier) && Data[globalIdentifier].ContainsKey(key))
                return globalIdentifier;

            return localIdentifier;
        }

        public string GetData(string source, string nick, string key)
        {
            var identifier = FindIdentifier(source, nick, key);
            
            if (!Data.ContainsKey(identifier))
                return null;

            if (!Data[identifier].ContainsKey(key))
                return null;

            return Data[identifier][key];
        }

        public void SetData(string source, string nick, string key, string value)
        {
            lock (Data)
            {
                var identifier = GetIdentifier(source, nick);
                if (!Data.ContainsKey(identifier))
                {
                    Data[identifier] = new();
                }

                dirty = true;
                Data[identifier][key] = value;
            }
        }

        public void Save()
        {
            if (!dirty)
                return;
            
            lock (Data)
            {
                File.WriteAllText("./data.json.tmp", JsonConvert.SerializeObject(Data));
                var readBack = LoadFrom("./data.json.tmp");

                foreach ((var user, var values) in Data)
                {
                    foreach ((var key, var val) in values)
                    {
                        if (!readBack.ContainsKey(user) || !readBack[user].ContainsKey(key) ||
                            readBack[user][key] != Data[user][key])
                        {
                            Console.WriteLine($"Inconsistent database: ");
                            Console.WriteLine($"written {user}/{key} does not match data in memory");
                            Console.WriteLine($"On disk: \"{val}\", in memory: \"{Data[user][key]}\"");
                            return;
                        }
                    }
                }

                dirty = false;
            }

            File.Copy("./data.json.tmp", "./data.json", true);
        }

        public void Load()
        {
            lock (Data)
            {
                if (!File.Exists("./data.json"))
                    Data = new();
                else
                {
                    Data = LoadFrom("./data.json");
                }

                dirty = true;
            }
        }

        private Dictionary<string, Dictionary<string, string>> LoadFrom(string path) =>
            JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(path));
    }
}