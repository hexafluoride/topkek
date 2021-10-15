using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class AffineConversion : IConversion
    {
        public double Ratio { get; set; }
        public double Offset { get; set; }

        public bool Inverted { get; set; }

        public AffineConversion()
        {

        }

        public AffineConversion(double ratio, bool inverted = false) :
            this(ratio, 0, inverted)
        {

        }

        public AffineConversion(double ratio, double offset, bool inverted = false)
        {
            Ratio = ratio;
            Offset = offset;

            Inverted = inverted;
        }

        public double Convert(double value)
        {
            if (Inverted)
                return (value - Offset) / Ratio;

            return (value * Ratio) + Offset;
        }

        public double ConvertBack(double value)
        {
            if (Inverted)
                return (value * Ratio) + Offset;

            return (value - Offset) / Ratio;
        }
    }
}
