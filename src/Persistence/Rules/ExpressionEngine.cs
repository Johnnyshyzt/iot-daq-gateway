using System.Globalization;

namespace IotDaq.Persistence.Rules;

/// <summary>
/// Sandboxed expressions. The grammar is fixed: numbers, point names, comparisons,
/// boolean words, and a whitelist of functions. There is no loop, no assignment,
/// and no way to name a method outside the whitelist.
/// </summary>
public static class ExpressionEngine
{
    public const int MaxLength = 2000;
    public const int MaxNodes = 256;

    private static readonly HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase)
    {
        "abs", "min", "max", "round", "floor", "ceil", "sqrt", "pow", "clamp", "if",
        "delta", "rate", "avg", "durationtrue", "counterinc", "since"
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["如果"] = "if",
        ["绝对值"] = "abs",
        ["最小"] = "min",
        ["最大"] = "max",
        ["差"] = "delta",
        ["速率"] = "rate",
        ["平均"] = "avg",
        ["持续"] = "durationtrue",
        ["增量"] = "counterinc",
        ["距变"] = "since"
    };

    public static ExpressionProgram Compile(string? source)
    {
        var text = source ?? "";
        if (text.Length == 0)
        {
            return ExpressionProgram.Fail("表达式不能为空");
        }

        if (text.Length > MaxLength)
        {
            return ExpressionProgram.Fail("表达式超过 2000 个字符");
        }

        if (text.Contains(';') || text.Contains('{') || text.Contains('}') || text.Contains('[') || text.Contains(']') || text.Contains('`'))
        {
            return ExpressionProgram.Fail("表达式不能包含分号、括号块或反引号");
        }

        try
        {
            var parser = new Parser(text);
            var root = parser.Parse();
            if (parser.Error is not null)
            {
                return ExpressionProgram.Fail(parser.Error);
            }

            if (parser.Nodes > MaxNodes)
            {
                return ExpressionProgram.Fail("表达式过于复杂");
            }

            var references = new List<string>();
            var error = Check(root, references);
            if (error is not null)
            {
                return ExpressionProgram.Fail(error);
            }

            return new ExpressionProgram
            {
                Root = root,
                References = references.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        catch (FormatException ex)
        {
            return ExpressionProgram.Fail(ex.Message);
        }
    }

    public static EvalValue Evaluate(ExpressionProgram program, EvalContext context)
    {
        if (!program.Ok || program.Root is null)
        {
            return EvalValue.Fail(program.Error ?? "表达式无效");
        }

        return Eval(program.Root, context);
    }

    private static string? Check(ExprNode node, List<string> references)
    {
        switch (node)
        {
            case NumberNode:
            case BoolNode:
            case TextNode:
                return null;
            case PointNode point:
                references.Add(point.Name);
                return null;
            case UnaryNode unary:
                return Check(unary.Inner, references);
            case BinaryNode binary:
                return Check(binary.Left, references) ?? Check(binary.Right, references);
            case CallNode call:
                var name = Canonical(call.Name);
                if (!Functions.Contains(name))
                {
                    return "不允许调用「" + call.Name + "」。只能使用白名单函数。";
                }

                var countError = ArgumentCount(name, call.Args.Count);
                if (countError is not null)
                {
                    return countError;
                }

                foreach (var arg in call.Args)
                {
                    var error = Check(arg, references);
                    if (error is not null)
                    {
                        return error;
                    }
                }

                if (name is "delta" or "rate" or "avg" or "counterinc" or "since" && call.Args[0] is not PointNode)
                {
                    return name + " 的第一个参数必须是点位名";
                }

                return null;
            default:
                return "表达式节点无效";
        }
    }

    private static string? ArgumentCount(string name, int count) => name switch
    {
        "abs" or "sqrt" or "floor" or "ceil" or "round" or "delta" or "rate" or "since" or "durationtrue" => count == 1 ? null : name + " 需要 1 个参数",
        "min" or "max" or "pow" or "avg" or "counterinc" => count is 1 or 2 ? null : name + " 的参数数量不对",
        "clamp" or "if" => count == 3 ? null : name + " 需要 3 个参数",
        _ => "不允许调用「" + name + "」"
    };

    private static EvalValue Eval(ExprNode node, EvalContext context) => node switch
    {
        NumberNode number => EvalValue.FromNumber(number.Value),
        BoolNode boolean => EvalValue.FromBool(boolean.Value),
        TextNode text => EvalValue.FromText(text.Value),
        PointNode point => context.Read(point.Name),
        UnaryNode unary => EvalUnary(unary, context),
        BinaryNode binary => EvalBinary(binary, context),
        CallNode call => EvalCall(call, context),
        _ => EvalValue.Fail("表达式节点无效")
    };

    private static EvalValue EvalUnary(UnaryNode node, EvalContext context)
    {
        var inner = Eval(node.Inner, context);
        if (!inner.Ok)
        {
            return inner;
        }

        if (node.Op is "!" or "not")
        {
            return EvalValue.FromBool(!inner.Truthy);
        }

        if (!inner.Number.HasValue)
        {
            return EvalValue.Fail("负号需要数值");
        }

        return EvalValue.FromNumber(-inner.Number.Value);
    }

    private static EvalValue EvalBinary(BinaryNode node, EvalContext context)
    {
        if (node.Op is "and" or "or")
        {
            var left = Eval(node.Left, context);
            if (!left.Ok)
            {
                return left;
            }

            if (node.Op == "and" && !left.Truthy)
            {
                return EvalValue.FromBool(false);
            }

            if (node.Op == "or" && left.Truthy)
            {
                return EvalValue.FromBool(true);
            }

            var right = Eval(node.Right, context);
            return right.Ok ? EvalValue.FromBool(right.Truthy) : right;
        }

        var leftValue = Eval(node.Left, context);
        if (!leftValue.Ok)
        {
            return leftValue;
        }

        var rightValue = Eval(node.Right, context);
        if (!rightValue.Ok)
        {
            return rightValue;
        }

        if (node.Op is "==" or "!=")
        {
            var same = Same(leftValue, rightValue);
            return EvalValue.FromBool(node.Op == "==" ? same : !same);
        }

        if (leftValue.Number is not double leftNumber || rightValue.Number is not double rightNumber)
        {
            return EvalValue.Fail("运算需要数值");
        }

        return node.Op switch
        {
            "+" => EvalValue.FromNumber(leftNumber + rightNumber),
            "-" => EvalValue.FromNumber(leftNumber - rightNumber),
            "*" => EvalValue.FromNumber(leftNumber * rightNumber),
            "/" => rightNumber == 0 ? EvalValue.Fail("除零") : EvalValue.FromNumber(leftNumber / rightNumber),
            "%" => rightNumber == 0 ? EvalValue.Fail("除零") : EvalValue.FromNumber(leftNumber % rightNumber),
            "^" => EvalValue.FromNumber(Math.Pow(leftNumber, rightNumber)),
            ">" => EvalValue.FromBool(leftNumber > rightNumber),
            "<" => EvalValue.FromBool(leftNumber < rightNumber),
            ">=" => EvalValue.FromBool(leftNumber >= rightNumber),
            "<=" => EvalValue.FromBool(leftNumber <= rightNumber),
            _ => EvalValue.Fail("不支持的运算符")
        };
    }

    private static bool Same(EvalValue left, EvalValue right)
    {
        if (left.Text is not null || right.Text is not null)
        {
            return string.Equals(left.Text ?? left.Number?.ToString(CultureInfo.InvariantCulture), right.Text ?? right.Number?.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        }

        if (left.Bool.HasValue && right.Bool.HasValue && left.Number is null && right.Number is null)
        {
            return left.Bool == right.Bool;
        }

        return left.Number.HasValue && right.Number.HasValue && left.Number.Value.Equals(right.Number.Value);
    }

    private static EvalValue EvalCall(CallNode call, EvalContext context)
    {
        var name = Canonical(call.Name);
        if (name == "if")
        {
            var cond = Eval(call.Args[0], context);
            if (!cond.Ok)
            {
                return cond;
            }

            return Eval(cond.Truthy ? call.Args[1] : call.Args[2], context);
        }

        if (name == "durationtrue")
        {
            var cond = Eval(call.Args[0], context);
            if (!cond.Ok)
            {
                return cond;
            }

            return EvalValue.FromNumber(context.Memory.DurationTrue(context.DurationScope + ":" + call.Key, cond.Truthy, context.NowMs));
        }

        if (name is "delta" or "rate" or "avg" or "counterinc" or "since")
        {
            var point = ((PointNode)call.Args[0]).Name;
            return name switch
            {
                "delta" => NumberOrFail(context.Memory.Delta(point), "还没有足够的历史"),
                "rate" => NumberOrFail(context.Memory.Rate(point), "还没有足够的历史"),
                "since" => NumberOrFail(context.Memory.Since(point, context.NowMs), "还没有变化记录"),
                "avg" => Average(call, context, point),
                "counterinc" => Counter(call, context, point),
                _ => EvalValue.Fail("函数无效")
            };
        }

        var args = new EvalValue[call.Args.Count];
        for (var i = 0; i < call.Args.Count; i++)
        {
            args[i] = Eval(call.Args[i], context);
            if (!args[i].Ok)
            {
                return args[i];
            }

            if (args[i].Number is null)
            {
                return EvalValue.Fail(name + " 需要数值");
            }
        }

        var n = args.Select(item => item.Number!.Value).ToArray();
        return name switch
        {
            "abs" => EvalValue.FromNumber(Math.Abs(n[0])),
            "sqrt" => n[0] < 0 ? EvalValue.Fail("负数不能开方") : EvalValue.FromNumber(Math.Sqrt(n[0])),
            "floor" => EvalValue.FromNumber(Math.Floor(n[0])),
            "ceil" => EvalValue.FromNumber(Math.Ceiling(n[0])),
            "round" => EvalValue.FromNumber(Math.Round(n[0], MidpointRounding.AwayFromZero)),
            "min" => EvalValue.FromNumber(n.Min()),
            "max" => EvalValue.FromNumber(n.Max()),
            "pow" => EvalValue.FromNumber(Math.Pow(n[0], n[1])),
            "clamp" => EvalValue.FromNumber(Math.Clamp(n[0], Math.Min(n[1], n[2]), Math.Max(n[1], n[2]))),
            _ => EvalValue.Fail("不允许调用「" + name + "」")
        };
    }

    private static EvalValue Average(CallNode call, EvalContext context, string point)
    {
        var window = 60d;
        if (call.Args.Count == 2)
        {
            var seconds = Eval(call.Args[1], context);
            if (!seconds.Ok || seconds.Number is null || seconds.Number <= 0)
            {
                return EvalValue.Fail("平均窗口需要正数秒");
            }

            window = Math.Min(seconds.Number.Value, 3600);
        }

        return NumberOrFail(context.Memory.Average(point, window, context.NowMs), "窗口内没有样本");
    }

    private static EvalValue Counter(CallNode call, EvalContext context, string point)
    {
        double? modulus = null;
        if (call.Args.Count == 2)
        {
            var mod = Eval(call.Args[1], context);
            if (!mod.Ok || mod.Number is null || mod.Number <= 0)
            {
                return EvalValue.Fail("回绕模数需要正数");
            }

            modulus = mod.Number;
        }

        return NumberOrFail(context.Memory.CounterIncrement(point, modulus), "还没有足够的历史");
    }

    private static EvalValue NumberOrFail(double? value, string error) =>
        value.HasValue ? EvalValue.FromNumber(value.Value) : EvalValue.Fail(error);

    private static string Canonical(string name) =>
        Aliases.TryGetValue(name, out var alias) ? alias : name.ToLowerInvariant();

    private sealed class Parser
    {
        private readonly string _text;
        private int _index;
        private int _keys;

        public Parser(string text) => _text = text;

        public string? Error { get; private set; }

        public int Nodes { get; private set; }

        public ExprNode Parse()
        {
            var node = ParseOr();
            Skip();
            if (Error is null && _index < _text.Length)
            {
                Error = "表达式有无法识别的内容";
            }

            return node;
        }

        private ExprNode ParseOr()
        {
            var left = ParseAnd();
            while (Error is null && MatchWord("or", "||", "或者"))
            {
                left = Binary("or", left, ParseAnd());
            }

            return left;
        }

        private ExprNode ParseAnd()
        {
            var left = ParseNot();
            while (Error is null && MatchWord("and", "&&", "并且"))
            {
                left = Binary("and", left, ParseNot());
            }

            return left;
        }

        private ExprNode ParseNot()
        {
            Skip();
            if (MatchWord("not", "!", "非"))
            {
                return Unary("not", ParseNot());
            }

            return ParseCmp();
        }

        private ExprNode ParseCmp()
        {
            var left = ParseAdd();
            Skip();
            var op = MatchAny(">=", "<=", "==", "!=", ">", "<", "=");
            if (op is null)
            {
                return left;
            }

            if (op == "=")
            {
                op = "==";
            }

            return Binary(op, left, ParseAdd());
        }

        private ExprNode ParseAdd()
        {
            var left = ParseMul();
            while (Error is null)
            {
                Skip();
                if (Peek('+'))
                {
                    _index++;
                    left = Binary("+", left, ParseMul());
                    continue;
                }

                if (Peek('-'))
                {
                    _index++;
                    left = Binary("-", left, ParseMul());
                    continue;
                }

                break;
            }

            return left;
        }

        private ExprNode ParseMul()
        {
            var left = ParsePow();
            while (Error is null)
            {
                Skip();
                if (Peek('*') || Peek('/') || Peek('%'))
                {
                    var op = _text[_index].ToString();
                    _index++;
                    left = Binary(op, left, ParsePow());
                    continue;
                }

                break;
            }

            return left;
        }

        private ExprNode ParsePow()
        {
            var left = ParseUnary();
            Skip();
            if (Peek('^'))
            {
                _index++;
                return Binary("^", left, ParsePow());
            }

            return left;
        }

        private ExprNode ParseUnary()
        {
            Skip();
            if (Peek('-'))
            {
                _index++;
                return Unary("-", ParseUnary());
            }

            if (Peek('+'))
            {
                _index++;
                return ParseUnary();
            }

            return ParsePrimary();
        }

        private ExprNode ParsePrimary()
        {
            Skip();
            if (_index >= _text.Length)
            {
                Error ??= "表达式不完整";
                return new NumberNode(0);
            }

            if (Peek('('))
            {
                _index++;
                var inner = ParseOr();
                Skip();
                if (!Peek(')'))
                {
                    Error ??= "缺少右括号";
                }
                else
                {
                    _index++;
                }

                return inner;
            }

            if (Peek('"'))
            {
                return new TextNode(ReadString());
            }

            if (char.IsDigit(_text[_index]) || Peek('.'))
            {
                return new NumberNode(ReadNumber());
            }

            if (IsIdentStart(_text[_index]))
            {
                var name = ReadIdent();
                if (name.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return new BoolNode(true);
                }

                if (name.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return new BoolNode(false);
                }

                Skip();
                if (Peek('('))
                {
                    _index++;
                    var args = new List<ExprNode>();
                    Skip();
                    if (!Peek(')'))
                    {
                        while (Error is null)
                        {
                            args.Add(ParseOr());
                            Skip();
                            if (Peek(','))
                            {
                                _index++;
                                continue;
                            }

                            break;
                        }
                    }

                    if (!Peek(')'))
                    {
                        Error ??= "函数缺少右括号";
                    }
                    else
                    {
                        _index++;
                    }

                    Nodes++;
                    return new CallNode(name, args, _keys++);
                }

                Nodes++;
                return new PointNode(name);
            }

            Error ??= "无法识别的字符";
            _index++;
            return new NumberNode(0);
        }

        private ExprNode Binary(string op, ExprNode left, ExprNode right)
        {
            Nodes++;
            return new BinaryNode(op, left, right);
        }

        private ExprNode Unary(string op, ExprNode inner)
        {
            Nodes++;
            return new UnaryNode(op, inner);
        }

        private bool MatchWord(params string[] words)
        {
            Skip();
            foreach (var word in words)
            {
                if (word is "&&" or "||" or "!" or ">=" or "<=" or "==" or "!=")
                {
                    if (MatchAny(word) is not null)
                    {
                        return true;
                    }

                    continue;
                }

                if (_index + word.Length <= _text.Length
                    && _text.AsSpan(_index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)
                    && (_index + word.Length == _text.Length || !IsIdentPart(_text[_index + word.Length])))
                {
                    _index += word.Length;
                    return true;
                }
            }

            return false;
        }

        private string? MatchAny(params string[] ops)
        {
            Skip();
            foreach (var op in ops.OrderByDescending(item => item.Length))
            {
                if (_index + op.Length <= _text.Length && _text.AsSpan(_index, op.Length).SequenceEqual(op))
                {
                    if (op is ">" or "<" or "=" && _index + 1 < _text.Length && _text[_index + 1] == '=')
                    {
                        continue;
                    }

                    _index += op.Length;
                    return op;
                }
            }

            return null;
        }

        private double ReadNumber()
        {
            var start = _index;
            while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] == '.'))
            {
                _index++;
            }

            var token = _text[start.._index];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                Error ??= "数字无效";
                return 0;
            }

            Nodes++;
            return value;
        }

        private string ReadString()
        {
            _index++;
            var start = _index;
            while (_index < _text.Length && _text[_index] != '"')
            {
                if (_text[_index] == '\\')
                {
                    Error ??= "字符串不支持转义";
                }

                _index++;
            }

            if (_index >= _text.Length)
            {
                Error ??= "字符串没有结束引号";
                return "";
            }

            var value = _text[start.._index];
            _index++;
            Nodes++;
            return value;
        }

        private string ReadIdent()
        {
            var start = _index;
            _index++;
            while (_index < _text.Length && IsIdentPart(_text[_index]))
            {
                _index++;
            }

            return _text[start.._index];
        }

        private void Skip()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        private bool Peek(char value) => _index < _text.Length && _text[_index] == value;

        private static bool IsIdentStart(char value) => char.IsLetter(value) || value == '_' || value > 127;

        private static bool IsIdentPart(char value) => IsIdentStart(value) || char.IsDigit(value) || value == '.';
    }
}

public sealed class ExpressionProgram
{
    public ExprNode? Root { get; init; }

    public IReadOnlyList<string> References { get; init; } = [];

    public string? Error { get; init; }

    public bool Ok => Error is null && Root is not null;

    public static ExpressionProgram Fail(string error) => new() { Error = error };
}

public abstract record ExprNode;

public sealed record NumberNode(double Value) : ExprNode;

public sealed record BoolNode(bool Value) : ExprNode;

public sealed record TextNode(string Value) : ExprNode;

public sealed record PointNode(string Name) : ExprNode;

public sealed record UnaryNode(string Op, ExprNode Inner) : ExprNode;

public sealed record BinaryNode(string Op, ExprNode Left, ExprNode Right) : ExprNode;

public sealed record CallNode(string Name, IReadOnlyList<ExprNode> Args, int Key) : ExprNode;

public readonly struct EvalValue
{
    public bool Ok { get; init; }

    public double? Number { get; init; }

    public bool? Bool { get; init; }

    public string? Text { get; init; }

    public string? Error { get; init; }

    public bool Truthy => Bool ?? (Number.HasValue && Number.Value != 0);

    public static EvalValue FromNumber(double value) => new() { Ok = true, Number = value };

    public static EvalValue FromBool(bool value) => new() { Ok = true, Bool = value, Number = value ? 1 : 0 };

    public static EvalValue FromText(string value) => new() { Ok = true, Text = value };

    public static EvalValue Fail(string error) => new() { Error = error };
}

public sealed class EvalContext
{
    public required PointMemory Memory { get; init; }

    public required IReadOnlyDictionary<string, double?> Numbers { get; init; }

    public required IReadOnlyDictionary<string, string?> Texts { get; init; }

    public long NowMs { get; init; }

    public string DurationScope { get; init; } = "";

    public EvalValue Read(string name)
    {
        if (Numbers.TryGetValue(name, out var number) && number.HasValue)
        {
            return EvalValue.FromNumber(number.Value);
        }

        if (Texts.TryGetValue(name, out var text) && !string.IsNullOrEmpty(text))
        {
            return EvalValue.FromText(text);
        }

        return EvalValue.Fail("缺少点位 " + name);
    }
}
