using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp.Events
{
    public class ErronousNickEventArgs : EventArgs
    {
        private static Random random;
        private static string GenerateRandomNick()
        {
            const string nickCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

            if (random == null)
                random = new Random();
            var nick = new char[8];
            for (int i = 0; i < nick.Length; i++)
                nick[i] = nickCharacters[random.Next(nickCharacters.Length)];
            return new string(nick);
        }

        public string InvalidNick { get; set; }
        public string NewNick { get; set; }
        public bool DoNotHandle { get; set; }

        public ErronousNickEventArgs(string invalidNick)
        {
            InvalidNick = invalidNick;
            NewNick = GenerateRandomNick();
            DoNotHandle = false;
        }
    }
}
