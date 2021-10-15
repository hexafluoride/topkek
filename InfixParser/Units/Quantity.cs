using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class Quantity : IEnumerable<Unit>
    {
        public string Name { get; set; }
        public Unit BaseUnit { get; set; }

        public bool Imaginary { get; set; }

        public List<Unit> Units = new List<Unit>();

        public Unit this[string key]
        {
            get => Units.OrderByDescending(unit => unit.CompareUnit(key)).FirstOrDefault(unit => unit.CompareUnit(key) > 0);
        }
    
        public Quantity(string name, bool imaginary = false)
        {
            Name = name;
            Imaginary = imaginary;
        }

        public void AddUnit(Unit unit)
        {
            unit.Quantity = this;

            Units.Add(unit);
        }

        public IEnumerator<Unit> GetEnumerator()
        {
            return ((IEnumerable<Unit>)Units).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<Unit>)Units).GetEnumerator();
        }

        public void Add(IEnumerable<Unit> units)
        {
            foreach (var unit in units)
                AddUnit(unit);
        }

        public void Add(string name, string symbol)
        {
            Add(name, symbol, new IdentityConversion());
        }

        public void Add(string name, IConversion conversion)
        {
            Add(name, (string)null, conversion);
        }

        public void Add(string name, string symbol, IConversion conversion)
        {
            AddUnit(new Unit(name, symbol, conversion));
        }

        public void Add(string name, IEnumerable<string> symbols, IConversion conversion)
        {
            Add(new [] { name }, symbols, conversion);
        }

        public void Add(IEnumerable<string> names, string symbol, IConversion conversion)
        {
            Add(names, new [] { symbol }, conversion);
        }

        public void Add(IEnumerable<string> names, IEnumerable<string> symbols, IConversion conversion)
        {
            AddUnit(new Unit(names, symbols, conversion));
        }

        public bool Contains(string name)
        {
            return this[name] != null;
        }

        public static bool operator ==(Quantity left, Quantity right)
        {
            if (ReferenceEquals(left, null) && ReferenceEquals(right, null))
                return true;

            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(Quantity left, Quantity right)
        {
            return !(left == right);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Quantity))
                return false;

            var _q = obj as Quantity;

            return Imaginary == _q.Imaginary && Name == _q.Name;
        }

        public override string ToString()
        {
            if (Imaginary)
                return Name + " (imaginary)";
            return Name;
        }

        public override int GetHashCode()
        {
            var hashCode = -1678388388;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + Imaginary.GetHashCode();
            return hashCode;
        }
    }
}
