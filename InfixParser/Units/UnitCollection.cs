using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class CompoundUnitDictionary : IEnumerable<CompoundUnit>
    {
        public Dictionary<string, CompoundUnit> Units = new Dictionary<string, CompoundUnit>();

        public CompoundUnitDictionary()
        {

        }

        public CompoundUnit this[string key]
        {
            get => Contains(key) ? Units.OrderByDescending(pair => pair.Value.CompareUnit(key)).First(pair => pair.Value.CompareUnit(key) > 0).Value : null;
        }

        public CompoundUnitDictionary(Dictionary<string, CompoundUnit> units)
        {
            Units = units;
        }

        public void AddUnit(string name, CompoundUnit unit)
        {
            Units[name] = unit;
        }

        public void Add(CompoundUnit unit)
        {
            AddUnit(unit.Name, unit);
        }

        public void Add(IEnumerable<Unit> units)
        {
            foreach (var unit in units)
            {
                if (!(unit is CompoundUnit))
                    throw new Exception("Unit isn't CompoundUnit");

                Add(unit as CompoundUnit);
            }
        }

        public bool Contains(string key)
        {
            return Units.Any(pair => pair.Value.CompareUnit(key) > 0);
        }

        public IEnumerator<CompoundUnit> GetEnumerator()
        {
            throw new NotImplementedException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }
    }

    public class UnitCollection : IEnumerable<UnitPair>
    {
        public Dictionary<Unit, int> Units = new Dictionary<Unit, int>();
        //public Dictionary<CompoundUnit, int> CompoundUnits = new Dictionary<CompoundUnit, int>();

        public UnitCollection()
        {

        }

        public UnitCollection(Dictionary<Unit, int> units)
        {
            Units = units;
        }

        internal void AddPower(Unit unit, int power)
        {
            if (!Units.ContainsKey(unit))
                Units[unit] = 0;

            Units[unit] += power;
            PruneUnits();
        }

        //internal void AddPower(CompoundUnit unit, int power)
        //{
        //    if (!CompoundUnits.ContainsKey(unit))
        //        CompoundUnits[unit] = 0;

        //    CompoundUnits[unit] += power;
        //    PruneUnits();
        //}

        public CompositeConversion GetCompositeConversion()
        {
            var conversion = new CompositeConversion();

            foreach(var unit in Units)
            {
                conversion.Add(unit.Key.ConversionToBase);
            }

            return conversion;
        }

        public double DeconstructCompoundUnit(CompoundUnit unit)
        {
            if (!Units.ContainsKey(unit))
                throw new Exception("No such compound unit " + unit);

            int power = Units[unit];
            bool polarity = power > 0;

            if (polarity)
            {
                for (int i = 0; i < power; i++)
                {
                    foreach (var sub_unit in unit.Units)
                        AddPower(sub_unit.Unit, sub_unit.Power);

                    AddPower(unit, -1);
                }
            }
            else
            {
                for (int i = 0; i > power; i--)
                {
                    foreach (var sub_unit in unit.Units)
                        AddPower(sub_unit.Unit, sub_unit.Power);

                    AddPower(unit, 1);
                }
            }

            return Math.Pow(unit.Value, power);
        }

        public double DeconstructAllCompoundUnits()
        {
            double temp = 1;

            var compound_units = Units.Where(c => c.Key is CompoundUnit).ToList();

            foreach(var unit in compound_units)
            {
                temp *= DeconstructCompoundUnit(unit.Key as CompoundUnit);
            }

            PruneUnits();

            return temp;
        }

        internal void PruneUnits()
        {
            if (Units.Any(u => u.Value == 0))
            {
                Units = Units.Where(unit => unit.Value != 0).ToDictionary(p => p.Key, p => p.Value);
            }
        }

        IEnumerator<UnitPair> IEnumerable<UnitPair>.GetEnumerator()
        {
            return Units.Select(p => new UnitPair(p.Key, p.Value)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Units.Select(p => new UnitPair(p.Key, p.Value)).GetEnumerator();
        }

        public void Add(Unit unit, int power)
        {
            AddPower(unit, power);
        }

        public override string ToString()
        {
            return string.Join("⋅",
                       Units.Select(unit => unit.Value == 1 ?
                           unit.Key.ToString() :
                           string.Format("{0}{1}", unit.Key, unit.Value.Superscript())));
        }
    }

    public class UnitPair
    {
        public Unit Unit { get; set; }
        public int Power { get; set; }

        public UnitPair(Unit unit)
        {
            Unit = unit;
            Power = 1;
        }

        public UnitPair(Unit unit, int power)
        {
            Unit = unit;
            Power = power;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is UnitPair))
                return false;

            var pair = obj as UnitPair;

            return pair.Unit == Unit && pair.Power == Power;
        }

        public override int GetHashCode()
        {
            var hashCode = 2017522981;
            hashCode = hashCode * -1521134295 + Unit.GetHashCode();
            hashCode = hashCode * -1521134295 + Power.GetHashCode();
            return hashCode;
        }
    }
}
