using System.Collections.Generic;
using System.Linq;

namespace InfixParser.Operators
{
    public class OperatorRegistry
    {
        public static List<IOperator> Operators = new List<IOperator>();

        public static void AddOperator(IOperator op)
        {
            Operators.Add(op);
        }

        public static void Initialize()
        {
            AddOperator(new NegationOperator());

            AddOperator(new AdditionOperator());
            AddOperator(new SubtractionOperator());
            AddOperator(new MultiplicationOperator());
            AddOperator(new DivisionOperator());
            AddOperator(new ExponentiationOperator());
        }

        public static Dictionary<int, IOperator> GetOperators(string identifier)
        {
            return Operators.Where(op => op.Matches(identifier)).GroupBy(op => op.ArgumentCount)
                .Select(group => group.OrderByDescending(op => op.Precedence).First(op => op.Matches(identifier)))
                .ToDictionary(op => op.ArgumentCount, op => op);
        }
    }
}