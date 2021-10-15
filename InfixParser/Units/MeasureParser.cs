using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class MeasureParser
    {
        private static Dictionary<string, int> SpecialPowers = new Dictionary<string, int>()
        {
            {"square", 2 },
            {"cube", 3 }
        };

        public static UnitCollection ParseUnitCollection(string str)
        {
            var lowercase = str.ToLower();
            var imaginary_measure = new CompoundMeasure(0, new Dictionary<Unit, int>());

            var per_divisions = lowercase.Split(new string[] { " per ", "/" }, StringSplitOptions.RemoveEmptyEntries);
            

            //var units = FindUnits(per_divisions);

            for (int i = 0; i < per_divisions.Length; i++)
            {
                bool positive = i % 2 == 0;
                var unit_entries = per_divisions[i].Split(' ');

                int power_to_be_consumed = 1;

                foreach (var unit_entry in unit_entries)
                {
                    if (SpecialPowers.ContainsKey(unit_entry))
                    {
                        power_to_be_consumed = SpecialPowers[unit_entry];
                        continue;
                    }

                    int power = positive ? power_to_be_consumed : -power_to_be_consumed;
                    var unit_name = unit_entry;

                    if (unit_entry.Contains('^'))
                    {
                        var power_entry = unit_entry.Split('^')[1];
                        unit_name = unit_entry.Split('^')[0];

                        if (!int.TryParse(power_entry, out power))
                        {
                            power = positive ? 1 : -1;
                        }
                        else
                            power *= positive ? 1 : -1;
                    }

                    var unit_exists = UnitDefinitions.Quantities.Any(quantity => quantity.Value.Contains(unit_name));
                    var compound_unit_exists = UnitDefinitions.CompoundUnits.Contains(unit_name);

                    var create_imaginary_quantity = !unit_exists && !compound_unit_exists;
                    
                    power_to_be_consumed = 1;

                    if (power == 0)
                        continue;

                    if (unit_exists)
                    {
                        var unit = UnitDefinitions.Quantities.First(quantity => quantity.Value.Contains(unit_name)).Value[unit_name];
                        imaginary_measure.AddPower(unit, power);
                    }
                    else if(compound_unit_exists)
                    {
                        var compound_unit = UnitDefinitions.CompoundUnits[unit_name];
                        imaginary_measure.AddPower(compound_unit, power);
                    }
                    else if(create_imaginary_quantity)
                    {
                        unit_name = new Pluralize.NET.Pluralizer().Singularize(unit_name);

                        var new_quantity = new Quantity(unit_name, true);
                        
                        new_quantity.Add(unit_name, new IdentityConversion());
                        imaginary_measure.AddPower(new_quantity[unit_name], power);
                    }
                }
            }

            return imaginary_measure.Units;
        }

        public static CompoundMeasure ParseMeasure(string str)
        {
            str = str.ToLower();
            str = str.Replace(" squared", "^2");
            str = str.Replace(" cubed", "^3");

            var measure = new CompoundMeasure(1, new Dictionary<Unit, int>());

            var amount_best_guess = 0d;
            int amount_length = 0;

            for (int i = 1; i <= str.Length; i++)
            {
                if (double.TryParse(str.Substring(0, i), out double test))
                {
                    amount_best_guess = test;
                    amount_length = i;
                }
                else if (UnitDefinitions.Constants.ContainsKey(str.Substring(0, i).Trim().ToLowerInvariant()))
                {
                    amount_best_guess = UnitDefinitions.Constants[str.Substring(0, i).Trim().ToLowerInvariant()];
                    amount_length = i;
                }
            }

            if (amount_length == 0)
            {
                amount_best_guess = 1;
                //return null;
            }

            measure.Value = amount_best_guess;

            var unit_block = str.Substring(amount_length).Trim();

            measure.Units = ParseUnitCollection(unit_block);

            return measure;
        }

        private static Dictionary<string, Unit> FindUnits(IEnumerable<string> units_)
        {
            var units = units_.ToList();

            foreach(var quantity in UnitDefinitions.Quantities)
            {
                if (units.All(unit => quantity.Value.Contains(unit)))
                    return units.ToDictionary(unit => unit, unit => quantity.Value[unit]);
            }

            return null;
        }
    }
}
