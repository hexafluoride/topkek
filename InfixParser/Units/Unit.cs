using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class CompoundUnit : Unit
    {
        public new Quantity Quantity { get => null; set => throw new InvalidOperationException(); }

        public double Value { get; set; }

        public UnitCollection Units { get; set; }
        
        public CompoundUnit(string name)
            : this(name, "")
        {
        }

        public CompoundUnit(string name, string symbol)
            : base()
        {
            Name = name;
            Symbol = symbol;

            Names.Add(name);
            Symbols.Add(symbol);
        }

        public CompoundUnit(IEnumerable<string> names, string symbol) :
            this(names, new string[] { symbol })
        {

        }

        public CompoundUnit(string name, IEnumerable<string> symbols) :
            this(new string[] { name }, symbols)
        {

        }

        public CompoundUnit(IEnumerable<string> names, IEnumerable<string> symbols) :
            base()
        {
            Name = names.First();
            Symbol = symbols.First();

            Names.AddRange(names);
            Symbols.AddRange(symbols);
        }

        public new string ToString(bool plural = false)
        {
            if (!string.IsNullOrWhiteSpace(Symbol))
                return Symbol;

            if (plural && !string.IsNullOrWhiteSpace(PluralName))
                return PluralName;

            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            return Units.ToString();
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Symbol))
                return Symbol;

            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            return Units.ToString();
        }

        public new int CompareUnit(string name)
        {
            return CompareUnit(name, this);
        }

        public static int CompareUnit(string name, CompoundUnit unit)
        {
            if (unit.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                return 4;

            if (!string.IsNullOrWhiteSpace(unit.Symbol))
            {
                if (unit.Symbol.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    return 3;

                if (unit.Symbol.ToLowerInvariant().Where(char.IsLetterOrDigit).SequenceEqual(name.ToLowerInvariant().Where(char.IsLetterOrDigit)))
                    return 2;
            }

            if (unit.PluralName.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                return 1;

            return 0;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CompoundUnit))
                return false;

            return (obj as CompoundUnit).Name == Name;
        }

        public override int GetHashCode()
        {
            var hashCode = -1811552046;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<UnitCollection>.Default.GetHashCode(Units);
            return hashCode;
        }
    }

    public class Unit
    {
        public Quantity Quantity { get; set; }

        public string Name { get; set; }
        public string Symbol { get; set; }

        public List<string> Names = new List<string>();
        public List<string> Symbols = new List<string>();

        private string _plural_name = null;
        public string PluralName { get => _plural_name ?? (_plural_name = Utilities.MakePlural(Name)); set => _plural_name = value; }

        public IConversion ConversionToBase { get; set; }

        internal Unit()
        {

        }

        public Unit(IEnumerable<string> names, IEnumerable<string> symbols, IConversion conversion)
        {
            Names = names.ToList();
            Symbols = symbols.ToList();

            Name = Names.FirstOrDefault();
            Symbol = Symbols.FirstOrDefault();

            ConversionToBase = conversion;
        }

        public Unit(string name) :
            this(name, "", new IdentityConversion())
        {
        }

        public Unit(string name, IConversion conversion)
            : this(name, "", conversion)
        {
        }

        public Unit(string name, string symbol, IConversion conversion)
        {
            Name = name;
            Symbol = symbol;
            ConversionToBase = conversion;

            Names.Add(name);
            Symbols.Add(symbol);
        }

        public string ToString(bool plural = false)
        {
            if (!string.IsNullOrWhiteSpace(Symbol))
                return Symbol;

            if (plural && !string.IsNullOrWhiteSpace(PluralName))
                return PluralName;

            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            if (!string.IsNullOrWhiteSpace(Quantity?.Name))
                return string.Format("unit of quantity {0}", Quantity.Name);

            return null;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Symbol))
                return Symbol;

            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            if (!string.IsNullOrWhiteSpace(Quantity?.Name))
                return string.Format("unit of quantity {0}", Quantity.Name);

            return null;
        }
        
        public int CompareUnit(string name)
        {
            return CompareUnit(name, this);
        }

        public static int CompareUnit(string name, Unit unit)
        {
            if (unit.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                return 4;

            if (!string.IsNullOrWhiteSpace(unit.Symbol))
            {
                if (unit.Symbols.Any(s => s.Equals(name, StringComparison.InvariantCulture)))
                    return 4;

                if (unit.Symbols.Any(s => s.Equals(name, StringComparison.InvariantCultureIgnoreCase)))
                    return 3;

                if (unit.Symbols.Any(s => s.ToLowerInvariant().Where(char.IsLetterOrDigit).SequenceEqual(name.ToLowerInvariant().Where(char.IsLetterOrDigit))))
                    return 2;
            }

            if (unit.PluralName.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                return 1;

            return 0;
        }

        public static bool operator ==(Unit left, Unit right)
        {
            if (ReferenceEquals(left, null) && ReferenceEquals(right, null))
                return true;

            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(Unit left, Unit right)
        {
            return !(left == right);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Unit))
                return false;

            return (obj as Unit)?.Name == Name;
        }

        public override int GetHashCode()
        {
            var hashCode = 998483807;
            if(Quantity != null) hashCode = hashCode * -1521134295 + Quantity.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Symbol);
            return hashCode;
        }
    }
}
