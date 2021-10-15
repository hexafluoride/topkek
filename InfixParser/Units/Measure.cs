using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class CompoundMeasure
    {
        //public Dictionary<Unit, int> Units { get; set; }
        public UnitCollection Units { get; set; }

        public double Value { get; set; }

        public bool IsSimple { get => Units.GroupBy(unit => unit.Unit.Quantity).Count() == 1; }

        public CompoundMeasure(double value, Dictionary<Unit, int> units)
        {
            Value = value;
            Units = new UnitCollection(units);
        }

        private CompoundMeasure()
        {
            //Units = new Dictionary<Unit, int>();
            Units = new UnitCollection();
        }

        public CompoundMeasure ConvertTo(Unit target) 
        {
            if (!IsSimple)
                throw new Exception("Cannot convert compound measure to a single target unit");

            if (target.Quantity != Units.First().Unit.Quantity)
                throw new Exception(string.Format("Cannot convert simple measure of quantity {0} to unit {1} of quantity {2}", Units.First().Unit.Quantity, target, target.Quantity));

            var total_power = Units.Sum(u => u.Power);

            return ConvertTo(new UnitCollection(new Dictionary<Unit, int>() { { target, total_power } }));
        }

        public CompoundMeasure ConvertTo(UnitCollection target_units)
        {
            if ((target_units.Any(u => u.Unit.ConversionToBase is AffineConversion) && target_units.Units.Count > 1) ||
                (Units.Any(u => u.Unit.ConversionToBase is AffineConversion) && Units.Units.Count > 1))
                throw new Exception("Cannot perform complex conversion on compound measures involving affine conversions");

            bool any_affine = target_units.Any(u => u.Unit.ConversionToBase is AffineConversion) || Units.Any(u => u.Unit.ConversionToBase is AffineConversion);

            var new_measure = new CompoundMeasure();
            new_measure.Value = Value;

            //foreach(var unit in Units)
            //{
            //    if (!(unit.Unit is CompoundUnit))
            //        continue;

            //    if (target_units.Any(u => u.Unit == unit.Unit))
            //        continue;

            //    Units.DeconstructCompoundUnit(unit.Unit as CompoundUnit);
            //}

            var _temp_measure = new CompoundMeasure(Value, Units.Units);
            _temp_measure = _temp_measure.Simplify();

            new_measure.Value = _temp_measure.Value;

            var new_units = new UnitCollection(_temp_measure.Units.ToDictionary(p => p.Unit, p => p.Power));
            var mult = new_units.DeconstructAllCompoundUnits();

            new_measure.Value *= mult;
            //new_measure = new_measure.Simplify();

            var target_simplified = new CompoundMeasure(1, target_units.ToDictionary(p => p.Unit, p => p.Power)).Simplify();
            var real_target_units = new UnitCollection(target_units.ToDictionary(p => p.Unit, p => p.Power));
            target_units = target_simplified.Units;

            bool compound_units_in_target = target_units.Any(u => u.Unit is CompoundUnit);

            var units_by_quantity = new_units.GroupBy(unit_pair => unit_pair.Unit.Quantity);

            var compound_units = target_units.Where(u => u.Unit is CompoundUnit);

            //if (compound_units_in_target)
            //{
            //    // let's try to construct the compound units in the target before we do anything else

            //    foreach(var compound_unit_pair in compound_units)
            //    {
            //        bool polarity = compound_unit_pair.Power > 0;
            //        var compound_unit = compound_unit_pair.Unit as CompoundUnit;

            //        if(polarity) // consume all of the sub-units from our input
            //        {
            //            for(int i = 0; i < compound_unit_pair.Power; i++)
            //            {
            //                foreach (var subunit in compound_unit.Units)
            //                {
            //                    target_units.AddPower(subunit.Unit, subunit.Power);
            //                }
            //            }
            //        }
            //        else
            //        {
            //            for (int i = 0; i < compound_unit_pair.Power; i++)
            //            {
            //                foreach (var subunit in compound_unit.Units)
            //                {
            //                    target_units.AddPower(subunit.Unit, -subunit.Power);
            //                }
            //            }
            //        }
            //    }
            //}

            foreach (var quantity_pair in units_by_quantity)
            {
                var quantity = quantity_pair.Key;

                foreach (var unit_pair in quantity_pair)
                {
                    var unit = unit_pair.Unit;
                    var power = unit_pair.Power;

                    if (power >= 0)
                    {
                        for (int k = 0; k < power; k++)
                        {
                            var available_unit_for_quantity = target_units.FirstOrDefault(p => p.Unit.Quantity == quantity && p.Power > 0)?.Unit;

                            if (available_unit_for_quantity == null)
                            {
                                if(!compound_units_in_target)
                                    throw new Exception($"Ran out of units of quantity {quantity} (looks like you're trying to use units that aren't defined. Try spelling the name of the unit out.)");

                                continue;
                            }

                            target_units.AddPower(available_unit_for_quantity, -1);

                            var temp_measure = new Measure(new_measure.Value, unit);
                            var converted_measure = temp_measure.ConvertTo(available_unit_for_quantity);

                            new_units.AddPower(unit, -1);
                            new_measure.AddPower(available_unit_for_quantity, 1);
                            new_measure.Value = converted_measure.Value;

                            unit_pair.Power--;
                        }
                    }
                    else
                    {
                        for (int k = power; k < 0; k++)
                        {
                            var available_unit_for_quantity = target_units.FirstOrDefault(p => p.Unit.Quantity == quantity && p.Power < 0)?.Unit;

                            if (available_unit_for_quantity == null)
                            {
                                if (!compound_units_in_target)
                                    throw new Exception($"Ran out of units of quantity {quantity} (looks like you're trying to use units that aren't defined. Try spelling the name of the unit out.)");

                                continue;
                            }

                            target_units.AddPower(available_unit_for_quantity, 1);

                            var temp_measure = new Measure(new_measure.Value, available_unit_for_quantity);
                            var converted_measure = temp_measure.ConvertTo(unit);

                            new_units.AddPower(unit, 1);
                            new_measure.AddPower(available_unit_for_quantity, -1);
                            new_measure.Value = converted_measure.Value;

                            unit_pair.Power++;
                        }
                    }
                }
            }

            //new_units = new UnitCollection(new_measure.Units.Concat(new_units));

            foreach (var compound_unit_pair in compound_units)
            {
                bool polarity = compound_unit_pair.Power > 0;
                var compound_unit = compound_unit_pair.Unit as CompoundUnit;

                if (true && polarity) // consume all of the sub-units from our input
                {
                    for (int i = 0; i < compound_unit_pair.Power; i++)
                    {
                        foreach (var subunit in compound_unit.Units)
                        {
                            var subunit_polarity = subunit.Power > 0;
                            int subunit_power = subunit.Power;

                            if (!polarity)
                                subunit_polarity = !subunit_polarity;

                            var subunit_quantity = subunit.Unit.Quantity;

                            for (int k = 0; k < Math.Abs(subunit_power); k++)
                            {
                                var all_suitable_units = new_units.Units.Where(unit => unit.Key.Quantity == subunit_quantity).ToList();

                                if (!all_suitable_units.Any())
                                    throw new Exception("oh no");

                                var suitable_unit_pair = all_suitable_units.First();
                                var suitable_unit = suitable_unit_pair.Key;

                                if (suitable_unit_pair.Value < 0 && subunit_polarity ||
                                    suitable_unit_pair.Value > 0 && !subunit_polarity)
                                {
                                    all_suitable_units.Remove(suitable_unit_pair);
                                    continue;
                                }

                                var temporary_measure_unit = subunit_polarity ? suitable_unit : subunit.Unit;
                                var conversion_unit = !subunit_polarity ? suitable_unit : subunit.Unit;

                                var temporary_measure = new Measure(new_measure.Value, temporary_measure_unit);
                                new_measure.Value = temporary_measure.ConvertTo(conversion_unit).Value;

                                new_units.AddPower(suitable_unit, subunit_polarity ? -1 : +1);
                                new_units.PruneUnits();
                            }
                        }
                    }
                }
                else
                {
                    //for (int i = 0; i < compound_unit_pair.Power; i++)
                    //{
                    //    foreach (var subunit in compound_unit.Units)
                    //    {
                    //        var subunit_polarity = subunit.Power > 0;
                    //        int subunit_power = subunit.Power;

                    //        var subunit_quantity = subunit.Unit.Quantity;

                    //        for (int k = 0; k < Math.Abs(subunit_power); k++)
                    //        {
                    //            var all_suitable_units = new_units.Units.Where(unit => unit.Key.Quantity == subunit_quantity).ToList();

                    //            if (!all_suitable_units.Any())
                    //                throw new Exception("oh no");

                    //            var suitable_unit_pair = all_suitable_units.First();
                    //            var suitable_unit = suitable_unit_pair.Key;

                    //            if (suitable_unit_pair.Value < 0 && subunit_polarity ||
                    //                suitable_unit_pair.Value > 0 && !subunit_polarity)
                    //            {
                    //                all_suitable_units.Remove(suitable_unit_pair);
                    //                continue;
                    //            }

                    //            var temporary_measure_unit = subunit_polarity ? suitable_unit : subunit.Unit;
                    //            var conversion_unit = !subunit_polarity ? suitable_unit : subunit.Unit;

                    //            var temporary_measure = new Measure(new_measure.Value, temporary_measure_unit);
                    //            new_measure.Value = temporary_measure.ConvertTo(conversion_unit).Value;

                    //            new_units.AddPower(suitable_unit, polarity ? -1 : +1);
                    //            new_units.PruneUnits();
                    //        }
                    //    }
                    //}
                }

                if (polarity)
                    new_measure.Value *= Math.Pow(compound_unit.Value, compound_unit_pair.Power);
                else
                    new_measure.Value /= Math.Pow(compound_unit.Value, compound_unit_pair.Power);

                new_measure.Units.Add(compound_unit, compound_unit_pair.Power);
            }

            var composite_conversion = real_target_units.GetCompositeConversion();

            if(any_affine)
                new_measure.Value = new Measure(Value, Units.First().Unit).ConvertTo(real_target_units.First().Unit).Value;
            else
                new_measure.Value /= target_simplified.Value;
            new_measure.Units = real_target_units;

            return new_measure;
        }

        public CompoundMeasure Simplify()
        {
            var copy = CreateCopy();
            var mult = copy.Units.DeconstructAllCompoundUnits();

            var new_measure = new CompoundMeasure();
            new_measure.Value = Value * mult;

            var units_by_quantity = copy.Units.GroupBy(unit_pair => unit_pair.Unit.Quantity);
            
            foreach(var quantity_pair in units_by_quantity)
            {
                var quantity = quantity_pair.Key;
                var base_unit = quantity.BaseUnit;

                if (quantity.Imaginary || base_unit == null)
                {
                    foreach (var unit_pair in quantity_pair)
                        new_measure.AddPower(unit_pair.Unit, unit_pair.Power);

                    continue;
                }

                foreach(var unit_pair in quantity_pair)
                {
                    var unit = unit_pair.Unit;
                    var power = unit_pair.Power;

                    if (power >= 0)
                    {
                        for (int k = 0; k < power; k++)
                        {
                            var temp_measure = new Measure(new_measure.Value, unit);
                            var converted_measure = temp_measure.ConvertTo(base_unit);

                            new_measure.AddPower(base_unit, 1);
                            new_measure.Value = converted_measure.Value;
                        }
                    }
                    else
                    {
                        for (int k = power; k < 0; k++)
                        {
                            var temp_measure = new Measure(new_measure.Value, base_unit);
                            var converted_measure = temp_measure.ConvertTo(unit);

                            new_measure.AddPower(base_unit, -1);
                            new_measure.Value = converted_measure.Value;
                        }
                    }
                }
            }

            return new_measure;
        }

        public CompoundMeasure CreateCopy()
        {
            return new CompoundMeasure(Value, Units.ToDictionary(u => u.Unit, u => u.Power));
        }

        public void Multiply(CompoundMeasure measure)
        {
            Value *= measure.Value;

            foreach (var subunit in measure.Units)
                AddPower(subunit.Unit, subunit.Power);
        }

        public void Divide(CompoundMeasure measure)
        {
            Value /= measure.Value;
            
            foreach (var subunit in measure.Units)
                AddPower(subunit.Unit, -subunit.Power);
        }

        public void Multiply(Measure measure)
        {
            Value *= measure.Value;
            AddPower(measure.Unit, 1);
        }

        public void Divide(Measure measure)
        {
            Value /= measure.Value;
            AddPower(measure.Unit, -1);
        }

        internal void AddPower(Unit unit, int power)
        {
            Units.AddPower(unit, power);
        }

        //internal void AddPower(CompoundUnit unit, int power)
        //{
        //    Units.AddPower(unit, power);
        //}

        public void Multiply(Unit unit)
        {
            Multiply(new Measure(1, unit));
        }

        public void Divide(Unit unit)
        {
            Divide(new Measure(1, unit));
        }

        public static CompoundMeasure operator *(CompoundMeasure a, CompoundMeasure b)
        {
            var new_measure = new CompoundMeasure(a.Value, a.Units.ToDictionary(k => k.Unit, k => k.Power));
            new_measure.Multiply(b);

            return new_measure;
        }

        public static CompoundMeasure operator /(CompoundMeasure a, CompoundMeasure b)
        {
            var new_measure = new CompoundMeasure(a.Value, a.Units.ToDictionary(k => k.Unit, k => k.Power));
            new_measure.Divide(b);

            return new_measure;
        }

        public static CompoundMeasure operator +(CompoundMeasure a, CompoundMeasure b)
        {
            if(!a.Units.SequenceEqual(b.Units))
            {
                var temp_a = a.Simplify();
                var temp_b = b.Simplify();

                if (!temp_a.Units.SequenceEqual(temp_b.Units))
                    throw new Exception("Cannot add these two measures");

                return new CompoundMeasure(temp_a.Value + temp_b.Value, temp_a.Units.Units);
            }

            return new CompoundMeasure(a.Value + b.Value, a.Units.Units);
        }

        public static CompoundMeasure operator -(CompoundMeasure a, CompoundMeasure b)
        {
            if (!a.Units.SequenceEqual(b.Units))
            {
                var temp_a = a.Simplify();
                var temp_b = b.Simplify();

                if (!temp_a.Units.SequenceEqual(temp_b.Units))
                    throw new Exception("Cannot subtract these two measures");

                return new CompoundMeasure(temp_a.Value - temp_b.Value, temp_a.Units.Units);
            }

            return new CompoundMeasure(a.Value - b.Value, a.Units.Units);
        }

        public override string ToString()
        {
            return string.Format("{0:#,##.#####} {1}", 
                Value,
                Units);
        }
    }

    public class Measure
    {
        public Quantity Quantity { get => Unit.Quantity; }
        public Unit Unit { get; set; }
        public double Value { get; set; }

        public Measure(double value, Unit unit)
        {
            Value = value;
            Unit = unit;
        }

        public Measure ConvertTo(Unit unit)
        {
            if (unit.Quantity != Quantity)
                throw new InvalidOperationException(string.Format("Cannot convert measure of quantity {0} to measure of quantity {1}", Quantity?.Name, unit?.Quantity?.Name));

            if (unit == Unit)
                return this;

            return new Measure(unit.ConversionToBase.ConvertBack(Unit.ConversionToBase.Convert(Value)), unit);
        }

        public override string ToString()
        {
            return string.Format("{0:#,##.#####} {1}", Value, Unit);
        }
    }
}
