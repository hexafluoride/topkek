using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using InfixParser.Units;

namespace InfixParser.Functions
{
    public abstract class Function
    {
        public abstract string Name { get; }
        public abstract List<string> Identifiers { get; }

        public abstract CompoundMeasure Execute(params CompoundMeasure[] arguments);
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class FunctionAttribute : Attribute
    {
        public FunctionAttribute()
        {
        }

        public FunctionAttribute(string name) :
            this(name, new[] {name})
        {
        }

        public FunctionAttribute(string[] identifiers) :
            this(identifiers[0], identifiers)
        {
        }

        public FunctionAttribute(string name, string[] identifiers)
        {
            Name = name;
            Identifiers = identifiers.ToArray();
        }

        public string Name { get; set; }
        public string[] Identifiers { get; set; }
    }

    public class DynamicFunction : Function
    {
        private readonly Delegate _func;

        private DynamicFunction(string name, string[] identifiers, Delegate func)
        {
            Name = name;
            Identifiers = identifiers.ToList();
            _func = func;
        }

        public override string Name { get; }
        public override List<string> Identifiers { get; }

        public override CompoundMeasure Execute(params CompoundMeasure[] arguments)
        {
            return _func.DynamicInvoke(arguments) as CompoundMeasure;
        }

        public static DynamicFunction FromAttribute(FunctionAttribute attrib, Delegate func)
        {
            return new DynamicFunction(attrib.Name, attrib.Identifiers.ToArray(), func);
        }

        public static IEnumerable<DynamicFunction> FromType(Type type)
        {
            var methods = type.GetMethods();

            foreach (var method in methods)
            {
                var attribute = (FunctionAttribute) Attribute.GetCustomAttribute(method, typeof(FunctionAttribute));

                if (attribute == null)
                    continue;

                if (string.IsNullOrWhiteSpace(attribute.Name)) attribute = new FunctionAttribute(method.Name);

                //yield return FromAttribute(attribute, Delegate.CreateDelegate(typeof(Delegate), method));
                yield return FromAttribute(attribute, method.CreateDelegate(Expression.GetDelegateType(
                    (from parameter in method.GetParameters() select parameter.ParameterType)
                    .Concat(new[] {method.ReturnType})
                    .ToArray())));
            }
        }
    }
}