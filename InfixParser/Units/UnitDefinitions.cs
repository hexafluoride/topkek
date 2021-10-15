using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class UnitDefinitions
    {
        public static Dictionary<string, double> Constants = new Dictionary<string, double>()
        {
            { "pi", Math.PI },
            { "e", Math.E },
            { "phi", 1.618033988749894848204586834365638117720309179805762862135 }
        };

        public static Dictionary<string, Quantity> Quantities = new Dictionary<string, Quantity>()
        {
            {"Temperature", new Quantity("Temperature")
            {
                {"Celsius", "°C", new IdentityConversion() },
                {"Kelvin", "°K", new AffineConversion(1, 273.15, true) },
                {"Fahrenheit", "°F", new AffineConversion(1.8, 32, true) },
            }},
            {"Time", new Quantity("Time")
            {
                GenerateSIMultiples(new Unit("second", "s", new IdentityConversion())),

                { "minute", "min", new LinearConversion(60) },
                { "hour", "h", new LinearConversion(3600) },
                { "day", "d", new LinearConversion(86400) },
                { "week", "w", new LinearConversion(604800) },
                { "month", "mo", new LinearConversion(2592000) },

                { "year", "y", new LinearConversion(31557600) },
                { "decade", new LinearConversion(315576000) },
                { "century", new LinearConversion(3155760000) },
                { "millenium", new LinearConversion(31557600000) },

                { "Planck time", "t_p", new LinearConversion(1e-44) },
            }},
            {"Length", new Quantity("Length")
            {
                GenerateSIMultiples(new Unit("meter", "m", new IdentityConversion())),

                { "inch", "in", new LinearConversion(0.0254) },
                { "foot", "ft", new LinearConversion(0.3048) },
                { "yard", "yd", new LinearConversion(0.9144) },
                { "mile", "mi", new LinearConversion(1609.34) }
            }},
            {"Mass", new Quantity("Mass")
            {
                GenerateSIMultiples(new Unit("gram", "g", new IdentityConversion())),

                { "ton", "t", new LinearConversion(1000000) },
                { "electrovolt", "eV/c²", new LinearConversion(1.783e-36) },
                { "pound", new []{ "lb", "lbs" }, new LinearConversion(453.592) },
                { "ounce", "oz", new LinearConversion(28.3495) },
                { "stone", "st", new LinearConversion(6350.29) },
                { "eighth", new LinearConversion(3.5) },
                { "dimebag", "dimebag (some good shit)", new LinearConversion(1) },
                { "grain", new LinearConversion(0.0648) }
            }},
            {"Current", new Quantity("Current")
            {
                GenerateSIMultiples(new Unit("ampere", "A", new IdentityConversion()))
            }},
            {"Amount", new Quantity("Amount")
            {
                { "mole", "mol", new IdentityConversion() }
            }},
            {"Voltage", new Quantity("Voltage")
            {
                GenerateSIMultiples(new Unit("volt", "V", new IdentityConversion()))
            }},
            {"Data Amount", new Quantity("Data Amount")
            {
                GenerateSIMultiples(new Unit("byte", "B", new IdentityConversion()), create_below: false),
                GenerateSIMultiples(new Unit("byte", "B", new IdentityConversion()), create_below: false, iec: true, create_self: false),
                GenerateSIMultiples(new Unit("bit", "b", new IdentityConversion()), 1d/8d, create_below: false),
                GenerateSIMultiples(new Unit("bit", "b", new IdentityConversion()), 1d/8d, create_below: false, iec: true, create_self: false),

            }},
            {"Currency", new Quantity("Currency")
            {
                {"dollar", "$", new IdentityConversion() },
                {"cent", new IdentityConversion() }
            } }
        };

        public static CompoundUnitDictionary CompoundUnits = new CompoundUnitDictionary()
        {
            { GenerateSIMultiples(new CompoundUnit("newton", "N") { Value = 1, Units = new UnitCollection()
            {
                { Quantities["Mass"]["Kilogram"], 1 },
                { Quantities["Length"]["Meter"], 1 },
                { Quantities["Time"]["Second"], -2 },
            } }) },
            { GenerateSIMultiples(new CompoundUnit("joule", "J") { Value = 1, Units = new UnitCollection()
            {
                { Quantities["Mass"]["Kilogram"], 1 },
                { Quantities["Length"]["Meter"], 2 },
                { Quantities["Time"]["Second"], -2 },
            } }) },
            { GenerateSIMultiples(new CompoundUnit("watt", "W") { Value = 1, Units = new UnitCollection()
            {
                { Quantities["Mass"]["Kilogram"], 1 },
                { Quantities["Length"]["Meter"], 2 },
                { Quantities["Time"]["Second"], -3 },
            } }) },

            { new CompoundUnit("faggot") { Value = 0.02704, Units = new UnitCollection()
            {
                { Quantities["Length"]["Meter"], 3 },
            } } },
            { new CompoundUnit("butt") { Value = 0.477, Units = new UnitCollection()
            {
                { Quantities["Length"]["Meter"], 3 },
            } } },
            { GenerateSIMultiples(new CompoundUnit(new []{ "liter", "litre" }, "L") { Value = 0.001, Units = new UnitCollection()
            {
                { Quantities["Length"]["Meter"], 3 },
            } }) },

            { GenerateSIMultiples(new CompoundUnit("pascal", "Pa") { Value = 1, Units = new UnitCollection()
            {
                { Quantities["Mass"]["Kilogram"], 1 },
                { Quantities["Length"]["Meter"], -1 },
                { Quantities["Time"]["Second"], -2 }
            }
            }) },

            { GenerateSIMultiples(new CompoundUnit("bar", "bar") { Value = 100000, Units = new UnitCollection()
            {
                { Quantities["Mass"]["Kilogram"], 1 },
                { Quantities["Length"]["Meter"], -1 },
                { Quantities["Time"]["Second"], -2 }
            }
            }) },

            { new CompoundUnit("pounds per square inch", "psi") { Value = 1, Units = new UnitCollection()
            {
                { Quantities["Mass"]["Pound"], 1 },
                { Quantities["Length"]["Inch"], -2 }
            }
            } },

            { new CompoundUnit("tablespoon", "tbsp") { Value = 1.478676478125E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit("teaspoon", "tsp") { Value = 0.49892159375E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit("shot") { Value = 4.436029434375E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit("cup") { Value = 23.65882365E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit("pint") { Value = 47.3176473E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit("quart") { Value = 94.6352946E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit("gallon") { Value = 378.5411784E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } },
            { new CompoundUnit(new []{ "fluid ounce", "fl oz" }, "floz") { Value = 2.957E-05, Units = new UnitCollection() { { Quantities["Length"]["Meter"], 3 }, } } }
        };

        public static IEnumerable<Unit> GenerateSIMultiples(Unit base_unit, double multiplier = 1, bool create_above = true, bool create_below = true, bool create_self = true, bool iec = false)
        {
            var multiples = new List<Tuple<string, string, double>>()
            {
                new Tuple<string, string, double>("yocto", "y", 1e-24),
                new Tuple<string, string, double>("zepto", "z", 1e-21),
                new Tuple<string, string, double>("atto", "a", 1e-18),
                new Tuple<string, string, double>("femto", "f", 1e-15),
                new Tuple<string, string, double>("pico", "p", 1e-12),
                new Tuple<string, string, double>("nano", "n", 1e-9),
                new Tuple<string, string, double>("micro", "µ", 1e-6),
                new Tuple<string, string, double>("milli", "m", 1e-3),
                new Tuple<string, string, double>("centi", "c", 1e-2),
                new Tuple<string, string, double>("deci", "d", 1e-1),
                new Tuple<string, string, double>("deca", "da", 1e1),
                new Tuple<string, string, double>("hecto", "h", 1e2),
                new Tuple<string, string, double>("kilo", "k", 1e3),
                new Tuple<string, string, double>("mega", "M", 1e6),
                new Tuple<string, string, double>("giga", "G", 1e9),
                new Tuple<string, string, double>("tera", "T", 1e12),
                new Tuple<string, string, double>("peta", "P", 1e15),
                new Tuple<string, string, double>("exa", "E", 1e18),
                new Tuple<string, string, double>("zetta", "Z", 1e21),
                new Tuple<string, string, double>("yotta", "Y", 1e24),
            };

            var iec_multiples = new List<Tuple<string, string, double>>()
            {
                new Tuple<string, string, double>("kibi", "Ki", 1024),
                new Tuple<string, string, double>("mebi", "Mi", Math.Pow(1024, 2)),
                new Tuple<string, string, double>("gibi", "Gi", Math.Pow(1024, 3)),
                new Tuple<string, string, double>("tebi", "Ti", Math.Pow(1024, 4)),
                new Tuple<string, string, double>("pebi", "Pi", Math.Pow(1024, 5)),
                new Tuple<string, string, double>("exbi", "Ei", Math.Pow(1024, 6)),
                new Tuple<string, string, double>("zebi", "Zi", Math.Pow(1024, 7)),
                new Tuple<string, string, double>("yobi", "Yi", Math.Pow(1024, 8)),
            };

            foreach (var multiple in (iec ? iec_multiples : multiples))
            {
                if (multiple.Item3 > 1 && !create_above)
                    continue;

                if (multiple.Item3 < 1 && !create_below)
                    continue;

                if (base_unit is CompoundUnit)
                {
                    if (!string.IsNullOrWhiteSpace(base_unit.Symbol))
                        yield return new CompoundUnit(multiple.Item1 + base_unit.Name, multiple.Item2 + base_unit.Symbol) { Units = ((CompoundUnit)base_unit).Units, Value = ((CompoundUnit)base_unit).Value * multiple.Item3 * multiplier };
                    else
                        yield return new CompoundUnit(multiple.Item1 + base_unit.Name, multiple.Item2 + base_unit.Name) { Units = ((CompoundUnit)base_unit).Units, Value = ((CompoundUnit)base_unit).Value * multiple.Item3 * multiplier };
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(base_unit.Symbol))
                        yield return new Unit(multiple.Item1 + base_unit.Name, multiple.Item2 + base_unit.Symbol, new LinearConversion(multiple.Item3 * multiplier));
                    else
                        yield return new Unit(multiple.Item1 + base_unit.Name, multiple.Item2 + base_unit.Name, new LinearConversion(multiple.Item3 * multiplier));
                }
            }

            if(create_self)
                yield return base_unit;
        }
    }
}
