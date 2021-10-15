using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public class ChannelCollection : IEnumerable<IrcChannel>
    {
        internal ChannelCollection(IrcClient client)
        {
            Channels = new List<IrcChannel>();
            Client = client;
        }

        private IrcClient Client { get; set; }
        private List<IrcChannel> Channels { get; set; }

        internal void Add(IrcChannel channel)
        {
            try
            {
                if (Channels.Contains(channel))
                    Channels.Remove(channel);
                if (Channels.Any(c => c.Name == channel.Name))
                    return;
                Channels.Add(channel);
            }
            catch
            {

            }

        }

        internal void Remove(IrcChannel channel)
        {
            Channels.Remove(channel);
        }

        public void Join(string name)
        {
            Client.JoinChannel(name);
        }

        public bool Contains(string name)
        {
            return Channels.Any(c => c.Name == name);
        }

        public IrcChannel this[int index]
        {
            get
            {
                return Channels[index];
            }
        }

        public IrcChannel this[string name]
        {
            get
            {
                var channel = Channels.FirstOrDefault(c => c.Name == name);
                if (channel == null)
                    throw new KeyNotFoundException();
                return channel;
            }
        }

        public IEnumerator<IrcChannel> GetEnumerator()
        {
            return Channels.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
