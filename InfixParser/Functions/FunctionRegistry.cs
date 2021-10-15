using System;
using System.Collections.Generic;
using System.Linq;

namespace InfixParser.Functions
{
    public class FunctionRegistry
    {
        public static List<Function> Functions = new List<Function>();

        public static void AddFunction(Function func)
        {
            Functions.Add(func);
        }

        public static void AddFunction(IEnumerable<Function> functions)
        {
            Functions.AddRange(functions);
        }

        public static void Initialize()
        {
            AddFunction(DynamicFunction.FromType(typeof(MathFunctions)));
        }

        public static Function GetFunction(string identifier)
        {
            return Functions.FirstOrDefault(func =>
                func.Identifiers.Any(id => id.Equals(identifier, StringComparison.InvariantCultureIgnoreCase)));
        }
    }
}