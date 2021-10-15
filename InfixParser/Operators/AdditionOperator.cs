using InfixParser.Units;

namespace InfixParser.Operators
{
    public class AdditionOperator : BinaryOperator
    {
        public override int Precedence => 2;
        public override string MainIdentifier => "+";

        public override bool Matches(string identifier)
        {
            return identifier == "+";
        }

        public override CompoundMeasure Operate(CompoundMeasure left, CompoundMeasure right)
        {
            return left + right;
        }
    }
}