using System;
using System.Linq;
using InfixParser.Units;

namespace InfixParser.Operators
{
    public class ExponentiationOperator : BinaryOperator
    {
        public override int Precedence => 4;
        public override string MainIdentifier => "^";

        public override bool Matches(string identifier)
        {
            return identifier == "^";
        }

        public override CompoundMeasure Operate(CompoundMeasure left_measure, CompoundMeasure right_measure)
        {
            var result = left_measure;

            if (left_measure.Units.Any())
                result.Units.Units = result.Units.ToDictionary(k => k.Unit, k => k.Power * (int) right_measure.Value);
            else
                result.Value = Math.Pow(result.Value, right_measure.Value);

            return result;
        }
    }
}