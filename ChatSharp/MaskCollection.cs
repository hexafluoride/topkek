using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ChatSharp
{
    public class MaskCollection : IEnumerable<Mask>
    {
        internal MaskCollection()
        {
            Masks = new List<Mask>();
        }

        private List<Mask> Masks { get; set; }

        public void Add(Mask mask)
        {
            Masks.Add(mask);
        }

        public void Remove(Mask mask)
        {
            Masks.Remove(mask);
        }

        public bool Contains(Mask mask)
        {
            return Masks.Contains(mask);
        }

        public bool ContainsMask(Mask mask)
        {
            return Masks.Any(m => m.Value == mask.Value);
        }

        public Mask this[int index]
        {
            get
            {
                return Masks[index];
            }
        }

        public bool ContainsMatch(IrcUser user)
        {
            return Masks.Any(m => user.Match(m.Value));
        }

        public Mask GetMatch(IrcUser user)
        {
            var match = Masks.FirstOrDefault(m => user.Match(m.Value));
            if (match == null)
                throw new KeyNotFoundException("No mask matches the specified user.");
            return match;
        }

        public IEnumerator<Mask> GetEnumerator()
        {
            return Masks.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
