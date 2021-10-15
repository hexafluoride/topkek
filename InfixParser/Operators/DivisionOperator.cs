using InfixParser.Units;

namespace InfixParser.Operators
{
    public class DivisionOperator : BinaryOperator
    {
        public override int Precedence => 3;
        public override string MainIdentifier => "/";

        public override bool Matches(string identifier)
        {
            return identifier == "/";
        }

        public override CompoundMeasure Operate(CompoundMeasure left, CompoundMeasure right)
        {
            return left / right;
        }
    }
}