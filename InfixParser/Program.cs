using System;
using System.Collections.Generic;
using System.Linq;
using InfixParser.Functions;
using InfixParser.Operators;
using InfixParser.Units;

namespace InfixParser
{
    public class Program
    {
        //static List<string> operators = new List<string>() { "+", "-", "/", "*" };
        //static Dictionary<string, int> OperatorPrecedence = new Dictionary<string, int>()
        //{
        //    {"^", 4 },
        //    {"*", 3 },
        //    {"/", 3 },
        //    {"+", 2 },
        //    {"-", 2 }
        //};

        private static void SendMessage(string msg, string src)
        {
            Console.WriteLine(msg);
        }

        private static void Main(string[] args)
        {
            UnitDefinitions.Quantities["Time"].BaseUnit = UnitDefinitions.Quantities["Time"]["Second"];
            UnitDefinitions.Quantities["Temperature"].BaseUnit = UnitDefinitions.Quantities["Temperature"]["Celsius"];
            UnitDefinitions.Quantities["Length"].BaseUnit = UnitDefinitions.Quantities["Length"]["Meter"];
            UnitDefinitions.Quantities["Mass"].BaseUnit = UnitDefinitions.Quantities["Mass"]["Gram"];
            UnitDefinitions.Quantities["Voltage"].BaseUnit = UnitDefinitions.Quantities["Voltage"]["Volt"];
            UnitDefinitions.Quantities["Current"].BaseUnit = UnitDefinitions.Quantities["Current"]["Ampere"];
            UnitDefinitions.Quantities["Amount"].BaseUnit = UnitDefinitions.Quantities["Amount"]["Mole"];
            UnitDefinitions.Quantities["Data Amount"].BaseUnit = UnitDefinitions.Quantities["Data Amount"]["Byte"];

            OperatorRegistry.Initialize();
            FunctionRegistry.Initialize();

            var cm = UnitDefinitions.Quantities["Length"]["Centimeter"];
            var inch = UnitDefinitions.Quantities["Length"]["Inch"];

            for (int inches = 60; inches < 84; inches++)
            {
                var value = new Measure(inches, UnitDefinitions.Quantities["Length"]["Inch"]);
                var next_value = new Measure(inches + 1, UnitDefinitions.Quantities["Length"]["Inch"]);

                Console.WriteLine($"{value.ConvertTo(cm).Value.ToString("0")}cm-{next_value.ConvertTo(cm).Value.ToString("0")}cm ({inches / 12}'{inches % 12}-{(inches + 1) / 12}'{(inches + 1) % 12})");
            }

            while (true)
            {
                var source = "";

                try
                {
                    var query = Console.ReadLine();
                    var sides = query.Split(new[] {" to ", " in "}, StringSplitOptions.RemoveEmptyEntries);

                    if (sides.Length == 0)
                    {
                        SendMessage("That doesn't make much sense.", source);
                        return;
                    }

                    if (sides.Length == 1)
                    {
                        var measure = Evaluate(sides[0]);

                        if (Math.Abs(measure.Value - measure.Simplify().Value) < 1e-5)
                            SendMessage(measure.ToString(), source);
                        else
                            SendMessage($"{measure} = {measure.Simplify()}", source);
                        //SendMessage(measure.ToString(), source);
                        continue;
                    }

                    //var to_convert = MeasureParser.ParseMeasure(sides[0]);
                    var to_convert = Evaluate(sides[0]);
                    //var target = MeasureParser.ParseUnitCollection(sides[1]);
                    var target = Evaluate(sides[1]).Units;

                    SendMessage($"{to_convert} = {to_convert.ConvertTo(target)}", source);
                }
                catch (Exception ex)
                {
                    SendMessage($"Oops: {ex.Message}", source);
                }
            }

            Console.ReadLine();
        }

        public static CompoundMeasure Evaluate(string str)
        {
            return Evaluate(Tokenize(str));
        }

        public static CompoundMeasure Evaluate(List<Token> tokens)
        {
            var measure = new CompoundMeasure(1, new Dictionary<Unit, int>());

            var measures = new CompoundMeasure[tokens.Count];

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (token.Type == TokenType.Brackets)
                {
                    measures[i] = Evaluate(Tokenize(token.Content));
                    tokens[i].Type = TokenType.Input;
                    continue;
                }

                if (token.Type == TokenType.FunctionCall)
                {
                    var function_name = token.Content.Split('(')[0];
                    var function = FunctionRegistry.GetFunction(function_name);

                    var args_block = token.Content.Substring(function_name.Length + 1);
                    args_block = args_block.Substring(0, args_block.Length - 1);

                    var bracket_balance = 0;
                    var args_list = new List<string>();
                    var current_buff = "";

                    for (var k = 0; k < args_block.Length; k++)
                    {
                        var current_char = args_block[k];

                        if (current_char == ',')
                        {
                            if (bracket_balance == 0)
                            {
                                args_list.Add(current_buff);
                                current_buff = "";
                                continue;
                            }
                        }
                        else if (current_char == '(' || current_char == ')')
                        {
                            bracket_balance += current_char == '(' ? 1 : -1;
                        }

                        current_buff += current_char;
                    }

                    if (bracket_balance == 0)
                        args_list.Add(current_buff);

                    var args = args_list.Select(argument => Evaluate(Tokenize(argument))).ToArray();

                    measures[i] = function.Execute(args);
                    tokens[i].Type = TokenType.Input;
                    continue;
                }

                if (token.Type != TokenType.Input)
                    continue;
                
                measures[i] = MeasureParser.ParseMeasure(token.Content);
            }

            var last_calculation = 0;

            while (measures.Any(m => m == null))
            {
                var highest_precedence = -1;
                var operator_index = -1;
                var operator_token = new Token("", TokenType.None);

                for (var i = 0; i < tokens.Count; i++) // detect operator with highest precedence
                {
                    var token = tokens[i];

                    if (token.Type != TokenType.Operator || measures[i] != null) // make sure this isnt solved
                        continue;

                    var ops = OperatorRegistry.GetOperators(token.Content);

                    var precedence = ops.OrderByDescending(o => o.Value.Precedence).First().Value.Precedence;

                    if (precedence > highest_precedence)
                    {
                        highest_precedence = precedence;
                        operator_token = token;
                        operator_index = i;
                    }
                }

                if (operator_token.Type == TokenType.None)
                    break;

                // find left measure

                CompoundMeasure left_measure = null;

                for (var i = operator_index - 1; i >= 0; i--)
                {
                    var token = tokens[i];

                    if (token.Type == TokenType.Input)
                    {
                        left_measure = measures[i];
                        tokens[i].Type = TokenType.None;
                        break;
                    }

                    if (token.Type == TokenType.Operator) break;
                }

                // find right measure

                CompoundMeasure right_measure = null;

                for (var i = operator_index + 1; i < tokens.Count; i++)
                {
                    var token = tokens[i];

                    if (token.Type == TokenType.Input)
                    {
                        right_measure = measures[i];
                        tokens[i].Type = TokenType.None;
                        break;
                    }
                }

                CompoundMeasure result = null;

                var possible_ops = OperatorRegistry.GetOperators(operator_token.Content);
                var num_of_args = 0;

                if (left_measure != null)
                    num_of_args++;

                if (right_measure != null)
                    num_of_args++;

                if (!possible_ops.ContainsKey(num_of_args))
                    throw new Exception(string.Format("Cannot find operator \"{0}\" for \"{1}\" arguments",
                        operator_token.Content, num_of_args));

                var op = possible_ops[num_of_args];
                result = op.Operate(left_measure, right_measure);

                tokens[operator_index] = new Token(result.ToString(), TokenType.Input);
                measures[operator_index] = result;
                last_calculation = operator_index;
            }

            return measures[last_calculation];
        }

        private static List<Token> Tokenize(string input)
        {
            var bracket_balance = 0;
            var token_buffer = "";

            var current_type = TokenType.None;

            var tokens = new List<Token>();

            for (var i = 0; i < input.Length; i++)
            {
                var current_char = input[i];
                var token_type = TokenType.None;

                if (OperatorRegistry.GetOperators(current_char.ToString()).Any())
                {
                    token_type = TokenType.Operator;
                }
                else if (current_char == '(' || current_char == ')')
                {
                    bracket_balance += current_char == '(' ? 1 : -1;
                    //current_type = TokenType.Brackets;
                    token_type = TokenType.Brackets;

                    if (FunctionRegistry.GetFunction(token_buffer) != null || current_type == TokenType.FunctionCall)
                    {
                        current_type = TokenType.FunctionCall;
                        token_type = TokenType.FunctionCall;
                    }
                }
                else
                {
                    if (current_type == TokenType.Input)
                        token_type = TokenType.Input;
                    else if (current_char != ' ')
                        token_type = TokenType.Input;
                }

                if (bracket_balance > 0)
                {
                    if (current_type == TokenType.FunctionCall)
                        token_type = current_type;
                    else
                        token_type = TokenType.Brackets;
                }

                if (token_type != current_type)
                {
                    if (current_type != TokenType.None)
                    {
                        if (current_type == TokenType.Brackets)
                            token_buffer = token_buffer.Substring(1, token_buffer.Length - 2);

                        Console.WriteLine("Emitted token {0}: {1}", current_type, token_buffer);
                        tokens.Add(new Token(token_buffer, current_type));
                    }

                    current_type = token_type;
                    token_buffer = "";
                }

                token_buffer += current_char;
            }

            // flush last token

            if (current_type != TokenType.None)
            {
                if (current_type == TokenType.Brackets)
                    token_buffer = token_buffer.Substring(1, token_buffer.Length - 2);

                Console.WriteLine("Emitted token {0}: {1}", current_type, token_buffer);
                tokens.Add(new Token(token_buffer, current_type));
            }

            return tokens;
        }
    }
}