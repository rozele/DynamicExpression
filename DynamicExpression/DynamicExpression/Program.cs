using DynamicExpression.Chakra;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq;
using System.Linq.CompilerServices;
using System.Linq.Expressions;

namespace ConsoleApplication4
{
    class Program
    {
        static void Main(string[] args)
        {
            ChakraTest();

            var environment = new Dictionary<string, Expression>
            {
                { "console.log", Expression.Constant(new Action<object>(Console.WriteLine)) },
            };

            var bonsai = @"['()', ['$', 'console.log'], ['()', ['=>',['{...}',[[]],[['=',['$',1,0],['+',['.','foo',[':',{'foo':42}]],['$',0,0]]],['$',1,0],['return',[':',99]]]],[['$','x']]], [':', 1]]]";
            var json = JArray.Parse(bonsai);
            var deserializer = new BonsaiDeserializer();
            var expression = deserializer.Visit(json);
            var fvs = FreeVariableScanner.Scan(expression).ToArray();
            var bindings = fvs.Select(p => environment[p.Name]).ToArray();
            var boundExpression = Expression.Invoke(Expression.Lambda(expression, fvs), bindings);
            boundExpression.Evaluate();
        }

        static void ChakraTest()
        {
            var jsrt = JavaScriptRuntime.Create();
            var ctx = jsrt.CreateContext();
            JavaScriptContext.Current = ctx;

            try
            {
                var res = JavaScriptContext.RunScript("(function() { return 42; })()");
                var str = res.ConvertToString();
                Console.WriteLine(str.ToString());
            }
            finally
            {
                JavaScriptContext.Current = JavaScriptContext.Invalid;
            }
        }
    }

    static class ExpressionExtensions
    {
        public static object Evaluate(this Expression expression)
        {
            return Expression.Lambda(expression).Compile().DynamicInvoke();
        }
    }

    static class Discriminators
    {
        public static class Expression
        {
            public const string Constant = ":";
            public const string Default = "default";
            public const string OnesComplement = "~";
            public const string Decrement = "--";
            public const string Increment = "++";
            public const string Not = "!";
            public const string Convert = "<:";
            public const string ConvertChecked = "<:$";
            public const string TypeAs = "as";
            public const string Quote = "`";
            public const string Plus = "+";
            public const string PlusDollar = "+$";
            public const string Add = "+";
            public const string AddChecked = "+$";
            public const string UnaryPlus = "+";
            public const string Minus = "-";
            public const string MinusDollar = "-$";
            public const string Subtract = "-";
            public const string SubtractChecked = "-$";
            public const string Negate = "-";
            public const string NegateChecked = "-$";
            public const string Multiply = "*";
            public const string MultiplyChecked = "*$";
            public const string Divide = "/";
            public const string Modulo = "%";
            public const string Power = "^^";
            public const string RightShift = ">>";
            public const string LeftShift = "<<";
            public const string LessThan = "<";
            public const string LessThanOrEqual = "<=";
            public const string GreaterThan = ">";
            public const string GreaterThanOrEqual = ">=";
            public const string Equal = "==";
            public const string NotEqual = "!=";
            public const string And = "&";
            public const string AndAlso = "&&";
            public const string Or = "|";
            public const string OrElse = "||";
            public const string ExclusiveOr = "^";
            public const string Coalesce = "??";
            public const string TypeIs = "is";
            public const string TypeEqual = "=:";
            public const string Conditional = "?:";
            public const string Lambda = "=>";
            public const string Parameter = "$";
            public const string Index = ".[]";
            public const string Invocation = "()";
            public const string MemberAccess = ".";
            public const string MethodCall = ".()";
            public const string New = "new";
            public const string MemberInit = "{.}";
            public const string ListInit = "{+}";
            public const string NewArrayInit = "new[]";
            public const string NewArrayBounds = "new[*]";
            public const string ArrayLength = "#";
            public const string ArrayIndex = "[]";
            public const string Assign = "=";
            public const string Block = "{...}";
            public const string Empty = "empty";
            public const string Return = "return";
        }
    }

    class BonsaiDeserializer
    {
        private readonly IList<ParameterExpression[]> _env = new List<ParameterExpression[]>();
        private readonly IList<LabelTarget> _labels = new List<LabelTarget>();

        public Expression Visit(JArray expression)
        {
            var discriminator = (JValue)expression[0];
            switch ((string)discriminator.Value)
            {
                case Discriminators.Expression.Add:
                    return VisitBinary(ExpressionType.Add, expression);
                case Discriminators.Expression.Assign:
                    return VisitAssign(ExpressionType.Assign, expression);
                case Discriminators.Expression.Block:
                    return VisitBlock(expression);
                case Discriminators.Expression.Constant:
                    return VisitConstant(expression);
                case Discriminators.Expression.Empty:
                    return VisitEmpty(expression);
                case Discriminators.Expression.Invocation:
                    return VisitInvocation(expression);
                case Discriminators.Expression.Lambda:
                    return VisitLambda(expression);
                case Discriminators.Expression.MemberAccess:
                    return VisitMemberAccess(expression);
                case Discriminators.Expression.Parameter:
                    return VisitParameter(expression);
                case Discriminators.Expression.Return:
                    return VisitReturn(expression);
                default:
                    throw new NotImplementedException();
            }
        }

        private Expression VisitAssign(ExpressionType assign, JArray expression)
        {
            var left = Visit((JArray)expression[1]);
            var right = Visit((JArray)expression[2]);
            return Expression.Assign(left, right);
        }

        private Expression VisitBinary(ExpressionType type, JArray expression)
        {
            var left = Visit((JArray)expression[1]);
            var right = Visit((JArray)expression[2]);
            var argumentInfo = new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null), CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) };
            var binder = Binder.BinaryOperation(CSharpBinderFlags.None, type, typeof(object), argumentInfo);
            return Expression.Dynamic(binder, typeof(object), left, right);
        }

        private Expression VisitBlock(JArray expression)
        {
            var variables = Enumerable.Range(0, ((JArray)expression[1]).Count).Select(i => Expression.Parameter(typeof(object))).ToArray();
            _env.Add(variables);
            var expressionsJson = (JArray)expression[2];
            var expressions = new Expression[expressionsJson.Count];
            for (var i = 0; i < expressionsJson.Count; ++i)
            {
                expressions[i] = Visit((JArray)expressionsJson[i]);
            }
            _env.RemoveAt(_env.Count - 1);

            return Expression.Block(variables, expressions);
        }

        private Expression VisitConstant(JArray expression)
        {
            return Expression.Constant(ConvertConstant(expression[1]), typeof(object));
        }

        private Expression VisitEmpty(JArray expression)
        {
            return Expression.Default(typeof(object));
        }

        private Expression VisitInvocation(JArray expression)
        {
            var arguments = new Expression[expression.Count - 1];
            arguments[0] = Visit((JArray)expression[1]);
            for (var i = 2; i < expression.Count; ++i)
            {
                arguments[i - 1] = Visit((JArray)expression[i]);
            }

            var argumentInfo = new CSharpArgumentInfo[expression.Count - 1];
            for (var i = 0; i < argumentInfo.Length; ++i)
            {
                argumentInfo[i] = CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null);
            }

            // TODO: call-site must know if result is discarded
            var binder = Binder.Invoke(CSharpBinderFlags.ResultDiscarded, typeof(object), argumentInfo);
            return Expression.Dynamic(binder, typeof(object), arguments);
        }

        private Expression VisitLambda(JArray expression)
        {
            var parameters = Enumerable.Range(0, ((JArray)expression[2]).Count).Select(i => Expression.Parameter(typeof(object))).ToArray();
            var label = Expression.Label(typeof(object));
            _env.Add(parameters);
            _labels.Add(label);
            var body = Expression.Block(Visit((JArray)expression[1]), Expression.Label(label, Expression.Default(typeof(object))));
            _labels.RemoveAt(_labels.Count - 1);
            _env.RemoveAt(_env.Count - 1);
            return Expression.Lambda(body, parameters);
        }

        private Expression VisitMemberAccess(JArray expression)
        {
            var member = expression[1].Value<string>();
            var target = Visit((JArray)expression[2]);
            var binder = Binder.GetMember(CSharpBinderFlags.None, member, typeof(ExpandoObject), new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
            return Expression.Dynamic(binder, typeof(object), target);
        }

        private Expression VisitParameter(JArray expression)
        {
            if (expression.Count == 3)
            {
                var scope = expression[1].Value<int>();
                var index = expression[2].Value<int>();
                return _env[scope][index];
            }
            else
            {
                Debug.Assert(expression.Count == 2);
                return Expression.Parameter(typeof(object), expression[1].Value<string>());
            }
        }

        private Expression VisitReturn(JArray expression)
        {
            return Expression.Return(_labels.Last(), Visit((JArray)expression[1]));
        }

        private static object ConvertConstant(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Boolean:
                case JTokenType.Float:
                case JTokenType.Integer:
                case JTokenType.String:
                    return ((JValue)token).Value;
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Array:
                    return ConvertConstantArray((JArray)token);
                case JTokenType.Object:
                    return ConvertConstantObject((JObject)token);
                default:
                    throw new NotImplementedException();
            }
        }

        private static object ConvertConstantArray(JArray token)
        {
            return token.Select(t => ConvertConstant(t)).ToArray();
        }

        private static object ConvertConstantObject(JObject token)
        {
            IDictionary<string, JToken> lookupToken = token;
            IDictionary<string, object> value = new ExpandoObject();
            foreach (var pair in lookupToken)
            {
                value[pair.Key] = ConvertConstant(pair.Value);
            }

            return value;
        }
    }
}
