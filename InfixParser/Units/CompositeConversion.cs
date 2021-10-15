using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class CompositeConversion : IConversion
    {
        public List<IConversion> Conversions { get; set; }

        public CompositeConversion()
        {
            Conversions = new List<IConversion>();
        }

        public CompositeConversion(IEnumerable<IConversion> conversions)
        {
            Conversions = conversions.ToList();
        }

        public void Add(IConversion conversion)
        {
            Conversions.Add(conversion);
        }

        public double Convert(double value)
        {
            foreach (var conversion in Conversions)
                value = conversion.Convert(value);

            return value;
        }

        public double ConvertBack(double value)
        {
            foreach (var conversion in Conversions)
                value = conversion.ConvertBack(value);

            return value;
        }
    }
}
