using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfixParser.Units
{
    public interface IConversion
    {
        double Convert(double value);
        double ConvertBack(double value);
    }
}
