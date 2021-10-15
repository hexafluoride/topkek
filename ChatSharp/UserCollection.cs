using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatSharp
{
    public class UserCollection : IEnumerable<IrcUser>
    {
        internal UserCollection()
        {
            Users = new List<IrcUser>();
        }
        
        private List<IrcUser> Users { get; set; }

        internal void Add(IrcUser user)
        {
            if (Users.Any(u => u.Hostmask == user.Hostmask))
                return;
            Users.Add(user);
        }

        internal void Remove(IrcUser user)
        {
            Users.Remove(user);
        }

        internal void Remove(string nick)
        {
            if (Contains(nick))
                Users.Remove(this[nick]);
        }

        public bool ContainsMask(string mask)
        {
            return Users.Any(u => u.Match(mask));
        }

        public bool Contains(string nick)
        {
            return Users.Any(u => u.Nick == nick);
        }

        public bool Contains(IrcUser user)
        {
            return Users.Any(u => u.Hostmask == user.Hostmask);
        }

        public IrcUser this[int index]
        {
            get
            {
                return Users[index];
            }
        }

        public IrcUser this[string nick]
        {
            get
            {
                var user = Users.FirstOrDefault(u => u.Nick == nick);
                if (user == null)
                    throw new KeyNotFoundException();
                return user;
            }
        }

        public IEnumerator<IrcUser> GetEnumerator()
        {
            return Users.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
