using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Heimdall;
using Microsoft.VisualBasic;

namespace UserLedger
{
    class Program
    {
        static void Main(string[] args)
        {
            new UserLedger().Start(args);
        }
    }

    class UserLedger : HeimdallBase.HeimdallBase
    {
        public Ledger Ledger { get; set; }

        public void Start(string[] args)
        {
            Name = "ledger";
            Ledger = new Ledger();
            Ledger.Load();
            
            Init(args, UserLedgerMain);
        }

        void UserLedgerMain()
        {
            Task.Factory.StartNew(delegate
            {
                while (true)
                {
                    Thread.Sleep(15000);
                    Ledger.Save();
                }
            }, TaskCreationOptions.LongRunning);
        }
        
        public override void ConnectionEstablished()
        {
            base.ConnectionEstablished();
            
            Connection.AddHandler("get_user_data", GetUserData);
            Connection.AddHandler("set_user_data", SetUserData);
        }

        void GetUserData(Connection c, Message m)
        {
            using (var ms = new MemoryStream(m.Data))
            {
                var source = ms.ReadString();
                var nick = ms.ReadString();
                var key = ms.ReadString();

                var data = Ledger.GetData(source, nick, key);
                data ??= "";

                using (var resp = new MemoryStream())
                {
                    resp.WriteString(data);
                    c.SendMessage(resp.ToArray(), "user_data", m.Source);
                }
            }
        }

        void SetUserData(Connection c, Message m)
        {
            using (var ms = new MemoryStream(m.Data))
            {
                var source = ms.ReadString();
                var nick = ms.ReadString();
                var key = ms.ReadString();
                var data = ms.ReadString();

                bool success = true;
                
                try
                {
                    Ledger.SetData(source, nick, key, data);
                }
                catch (Exception ex)
                {
                    Console.Write("Exception thrown: ");
                    Console.WriteLine(ex);
                    success = false;
                }

                using (var resp = new MemoryStream())
                {
                    resp.WriteString(data);
                    c.SendMessage(success ? "+" : "-", "save_user_data", m.Source);
                }
            }
        }
    }
}