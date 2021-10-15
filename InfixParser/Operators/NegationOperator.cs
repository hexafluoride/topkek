using InfixParser.Units;

namespace InfixParser.Operators
{
    public class NegationOperator : UnaryOperator
    {
        public override int Precedence => 5;
        public override string MainIdentifier => "-";

        public override bool Matches(string identifier)
        {
            return identifier == "-";
        }

        public override CompoundMeasure Operate(CompoundMeasure measure)
        {
            return new CompoundMeasure(-measure.Value, measure.Units.Units);
        }
    }
}