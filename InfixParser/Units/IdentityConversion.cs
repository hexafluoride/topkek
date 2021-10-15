using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public class IdentityConversion : IConversion
    {
        public IdentityConversion()
        {

        }

        public double Convert(double value)
        {
            return value;
        }

        public double ConvertBack(double value)
        {
            return value;
        }
    }
}
