using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Heimdall;

namespace HeimdallBase
{
    public class HeimdallBase
    {   
        public ConnectionToRouter Connection;
        public DateTime Start = DateTime.Now;

        public string Name = "";

        public void Init(string[] args, Action act = null)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Config.Load();
            
            string host = "localhost";
            int port = 9933;

            if (args.Any(a => !a.StartsWith("--")))
            {
                host = args.First(a => !a.StartsWith("--"));

                if (host.Contains(':'))
                {
                    var parts = host.Split(':');
                    host = parts[0];
                    port = int.Parse(parts[1]);
                }
            }

            ConnectionInit(host, port);

            if (act != null)
                act();

            while (true)
            {
                if(args.Any() && args[0] == "--daemon")
                {
                    Thread.Sleep(int.MaxValue);
                }

                string str = Console.ReadLine();
                if (str == "quit")
                {
                    Connection.End();
                }
                else if(str == "reconnect")
                {
                    ConnectionInit(host, port);
                }
                else
                {
                    Thread.Sleep(500);
                }
            }
        }
        
        public void GetUptime(Connection conn, Message msg)
        {
            Message reply = msg.Clone(true);

            reply.MessageType = "uptime";
            reply.Data = BitConverter.GetBytes((int)(DateTime.Now - Start).TotalSeconds);

            Connection.SendMessage(reply);
        }

        public void ConnectionInit(string host, int port)
        {
            Connection = new ConnectionToRouter(host, port, Name);
            Connection.AddHandler("get_uptime", GetUptime);
            Connection.AddHandler("rehash", (c, m) => { Config.Load(); });
            
            ConnectionEstablished();
        }
        
        public string[] GetModules()
        {
            List<string> ret = new List<string>();

            byte[] modules = Connection.WaitFor("", "get_modules", "router", "modules");

            MemoryStream ms = new MemoryStream(modules);

            while (ms.Position != ms.Length)
            {
                ret.Add(ms.ReadString());
            }

            ret.Add("router");
            return ret.ToArray();
        }

        public bool IsModuleUp(string module)
        {
            string[] up_modules = GetModules();
            return up_modules.Contains(module);
        }

        public int GetUptime(string dest)
        {
            if (dest == Connection.Name)
                return (int)(DateTime.Now - Start).TotalSeconds;

            if (!IsModuleUp(dest))
                return -1;

            byte[] data = Connection.WaitFor("", "get_uptime", dest, "uptime");
            int ret = BitConverter.ToInt32(data, 0);
            return ret;
        }

        public virtual void ConnectionEstablished() { }
    }
}