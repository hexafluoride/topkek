using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using OsirisBase;

namespace Backyard
{
    public partial class Backyard
    {
        private string ramLock = "ram-acquisition";
        private List<IntPtr> allocatedPtrs = new List<IntPtr>();
        public void ShowRam(string args, string source, string nick)
        {
            if (CanAct("admin.ram", source, nick))
                MarkAct("admin.ram", source, nick);
            else
            {
                SendMessage("Just wait a second.", source);
                return;
            }
            
            {
                var moduleMap = new Dictionary<string, string>()
                {
                    {"ledger", "userledger"},
                    {"crypto", "cryptodata"},
                    {"irc", "osirisnext"},
                    {"router", "vindler"}
                };

                var modulesByRam = GetModules().ToDictionary(m => m,
                    m => new
                    {
                        RamUsage = GetRamForService(moduleMap.ContainsKey(m) ? moduleMap[m] : m), Uptime = GetUptime(m)
                    });
                var totalRam = modulesByRam.Sum(m => m.Value.RamUsage);

                SendMessage(string.Join(" | ",
                                modulesByRam.Select(m =>
                                    $"{m.Key} up {Utilities.TimeSpanToPrettyString(TimeSpan.FromSeconds(m.Value.Uptime))}{(m.Value.RamUsage > 0 ? " " + (m.Value.RamUsage / 1048576d).ToString("0.00") + " MB" : "")}")) +
                            (totalRam > 0 ? " | total RAM: " + (totalRam / 1048576d).ToString("0.00") + " MB (dont laugh)" : ""),
                    source);
            }
        }

        private long GetRamForService(string serviceName)
        {
            if (!File.Exists("/home/kate/Scripts/get-ram.sh"))
                return -1;

            var proc = Process.Start(new ProcessStartInfo("/home/kate/Scripts/get-ram.sh", serviceName)
            {
                RedirectStandardOutput = true
            });
            if (!proc.WaitForExit(500))
                proc.Kill();

            if (proc.StandardOutput.EndOfStream)
                return -1;

            if (long.TryParse(proc.StandardOutput.ReadToEnd(), out long mem))
                return mem;
            return -1;
        }
        
        public void Rehash(string args, string source, string nick)
        {
            foreach (var module in GetModules())
                Connection.SendMessage(new byte[0], "rehash", module);
            SendMessage("done", source);
        }
        
        public void Configuration(string args, string source, string nick)
        {
            args = args.Substring("$config".Length).Trim();
            var parts = args.Split(' ');
            var verb = parts[0].ToLowerInvariant() == "set" ? "set" : "get";
            if (parts[0].ToLowerInvariant() == verb)
                parts = parts.Skip(1).ToArray();
            
            var target = parts[0];
            var key = parts[1];
            var targetParts = target.Split('/');
            string targetSource, targetNick;
                    
            if (targetParts.Length == 1)
            {
                targetSource = source;
                targetNick = targetParts[0];
            }
            else if (targetParts.Length == 2)
            {
                targetSource = target.Trim('/');
                targetNick = "";

                if (targetParts[1].StartsWith('@'))
                {
                    targetNick = targetParts[1].Substring(1);
                    targetSource = targetParts[0];
                }
            }
            else if (targetParts.Length == 3)
            {
                targetSource = targetParts[0] + "/" + targetParts[1];
                targetNick = targetParts[2];
            }
            else
            {
                return;
            }

            string value = "";

            switch (verb.ToLowerInvariant())
            {
                case "set":
                    value = string.Join(' ', parts.Skip(2));
                    SetUserDataForSourceAndNick(targetSource, targetNick, key, value);
                    value = GetUserDataForSourceAndNick<string>(targetSource, targetNick, key);
                    SendMessage($"{targetSource}/{targetNick}/{key} = \"{value}\"", source);
                    break;
                case "get":
                    value = GetUserDataForSourceAndNick<string>(targetSource, targetNick, key);
                    SendMessage($"{targetSource}/{targetNick}/{key} = \"{value}\"", source);
                    break;
            }
        }
    }
}