using System;
using System.Linq;
using InfixParser.Units;

namespace InfixParser.Operators
{
    public interface IOperator
    {
        int ArgumentCount { get; }
        int Precedence { get; }
        string MainIdentifier { get; }
        bool Matches(string identifier);
    }

    public abstract class UnaryOperator : IOperator
    {
        public abstract int Precedence { get; }
        public abstract string MainIdentifier { get; }

        public abstract bool Matches(string identifier);

        public int ArgumentCount => 1;
        public abstract CompoundMeasure Operate(CompoundMeasure measure);
    }

    public abstract class BinaryOperator : IOperator
    {
        public abstract int Precedence { get; }
        public abstract string MainIdentifier { get; }

        public abstract bool Matches(string identifier);

        public int ArgumentCount => 2;
        public abstract CompoundMeasure Operate(CompoundMeasure left_measure, CompoundMeasure right_measure);
    }

    public static class OperatorExtensions
    {
        public static CompoundMeasure Operate(this IOperator op, params CompoundMeasure[] arguments)
        {
            var args = arguments.Where(arg => arg != null).Take(op.ArgumentCount).ToArray();

            if (op is UnaryOperator)
                return (op as UnaryOperator).Operate(args[0]);

            if (op is BinaryOperator)
                return (op as BinaryOperator).Operate(args[0], args[1]);

            throw new Exception("Cannot recognize IOperator");
        }
    }
}