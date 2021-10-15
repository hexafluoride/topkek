using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class LinearConversion : IConversion
    {
        public double Ratio { get; set; }

        public bool Inverted { get; set; }

        public LinearConversion()
        {

        }

        public LinearConversion(double ratio, bool inverted = false)
        {
            Ratio = ratio;

            Inverted = inverted;
        }

        public double Convert(double value)
        {
            if (Inverted)
                return value / Ratio;

            return value * Ratio;
        }

        public double ConvertBack(double value)
        {
            if (Inverted)
                return value * Ratio;

            return value / Ratio;
        }
    }
}
