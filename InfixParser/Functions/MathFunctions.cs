using System;
using System.Linq;
using InfixParser.Units;

namespace InfixParser.Functions
{
    public static class MathFunctions
    {
        [Function(new[] {"power", "pow"})]
        public static CompoundMeasure Power(CompoundMeasure @base, CompoundMeasure power)
        {
            var result = @base;

            result.Value = Math.Pow(result.Value, power.Value);

            if (@base.Units.Any())
                result.Units.Units = result.Units.ToDictionary(k => k.Unit, k => k.Power * (int) power.Value);

            return result;
        }

        [Function]
        public static CompoundMeasure Sin(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Sin(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Cos(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Cos(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Tan(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Tan(result.Value);
            return result;
        }

        [Function(new[] {"ceiling", "ceil"})]
        public static CompoundMeasure Ceiling(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Ceiling(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Floor(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Floor(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Round(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Round(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Sqrt(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Sqrt(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Abs(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Abs(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Min(CompoundMeasure first, CompoundMeasure second)
        {
            var first_s = first.CreateCopy().Simplify();
            var second_s = second.CreateCopy().Simplify();

            if (!first_s.Units.SequenceEqual(second_s.Units))
                throw new Exception("Cannot compare these two units");

            return first_s.Value < second_s.Value ? first : second;
        }

        [Function]
        public static CompoundMeasure Max(CompoundMeasure first, CompoundMeasure second)
        {
            var first_s = first.CreateCopy().Simplify();
            var second_s = second.CreateCopy().Simplify();

            if (!first_s.Units.SequenceEqual(second_s.Units))
                throw new Exception("Cannot compare these two units");

            return first_s.Value > second_s.Value ? first : second;
        }

        [Function]
        public static CompoundMeasure Ln(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Log(result.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Log(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Log(result.Value, 10);
            return result;
        }

        [Function]
        public static CompoundMeasure Log(CompoundMeasure val, CompoundMeasure _base)
        {
            var result = val.CreateCopy();
            result.Value = Math.Log(result.Value, _base.Value);
            return result;
        }

        [Function]
        public static CompoundMeasure Log2(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Log(result.Value, 2);
            return result;
        }

        [Function]
        public static CompoundMeasure Log10(CompoundMeasure val)
        {
            var result = val.CreateCopy();
            result.Value = Math.Log(result.Value, 10);
            return result;
        }
    }
}