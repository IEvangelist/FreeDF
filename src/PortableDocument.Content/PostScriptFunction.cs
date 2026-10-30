using System.Globalization;

namespace PortableDocument.Content;

/// <summary>Type 4 (PostScript calculator) function, ISO 32000-2 §7.10.5, with the integer / real / boolean operand types of Table 42.</summary>
sealed class PostScriptFunction : PdfFunction
{
    enum Op
    {
        Abs, Add, And, Atan, Bitshift, Ceiling, Copy, Cos, Cvi, Cvr, Div, Dup, Eq, Exch, Exp, False, Floor, Ge, Gt, Idiv, Index,
        Le, Ln, Log, Lt, Mod, Mul, Ne, Neg, Not, Or, Pop, Roll, Round, Sin, Sqrt, Sub, True, Truncate, Xor, If, IfElse, Unknown
    }

    const byte Int = 0, Real = 1, Bool = 2;
    const int MaxStack = 100; // Annex of 7.10.5: the operand stack holds at most 100 entries.

    readonly struct V(double d, byte t)
    {
        public readonly double D = d; public readonly byte T = t;
    }

    sealed class Block(List<object> items) { public readonly object[] Items = [.. items]; }

    sealed class Machine { public readonly V[] S = new V[MaxStack]; public int N; }

    [ThreadStatic] static Machine? s_machine;

    static readonly Dictionary<string, Op> Names = Enum.GetValues<Op>().Where(o => o is not (Op.IfElse or Op.Unknown))
        .ToDictionary(o => o.ToString().ToLowerInvariant(), o => o);

    readonly Block _prog;

    public PostScriptFunction(byte[] code)
    {
        var text = System.Text.Encoding.Latin1.GetString(code);
        var tokens = Tokenize(text);
        var i = 0;
        while (i < tokens.Count && tokens[i] != "{") i++;
        i++;
        _prog = ParseBlock(tokens, ref i);
    }

    static List<string> Tokenize(string text)
    {
        var l = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (c == '%') { while (i < text.Length && text[i] is not ('\n' or '\r')) i++; }
            else if (c is '{' or '}') { l.Add(c.ToString()); i++; }
            else if (c is ' ' or '\t' or '\n' or '\r' or '\f' or '\0') i++;
            else
            {
                var s = i;
                while (i < text.Length && text[i] is not (' ' or '\t' or '\n' or '\r' or '\f' or '\0' or '{' or '}' or '%')) i++;
                l.Add(text[s..i]);
            }
        }
        return l;
    }

    static Block ParseBlock(List<string> t, ref int i)
    {
        var l = new List<object>();
        while (i < t.Count)
        {
            var s = t[i++];
            if (s == "}") break;
            if (s == "{") l.Add(ParseBlock(t, ref i));
            else if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
                l.Add(n is >= int.MinValue and <= int.MaxValue ? new V(n, Int) : new V(n, Real));
            else if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) l.Add(new V(d, Real));
            else l.Add(s == "ifelse" ? Op.IfElse : Names.GetValueOrDefault(s, Op.Unknown));
        }
        return new Block(l);
    }

    public override double[] Evaluate(double[] input)
    {
        var m = s_machine ??= new Machine();
        m.N = 0;
        var x = ClipIn(input);
        try
        {
            foreach (var v in x) Push(m, new V(v, Real));
            Run(_prog, m);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentOutOfRangeException or IndexOutOfRangeException) { }
        var n = Outputs > 0 ? Outputs : m.N;
        var r = new double[n];
        // A conforming program leaves exactly n results; if there are fewer, the missing leading outputs are 0.
        var have = Math.Min(n, m.N);
        for (var i = 0; i < have; i++) r[n - have + i] = Finite(m.S[m.N - have + i].D);
        return ClipOut(r);
    }

    static double Finite(double v) => double.IsFinite(v) ? v : 0;

    static void Push(Machine m, V v)
    {
        if (m.N >= MaxStack) throw new InvalidOperationException("PostScript calculator stack overflow");
        m.S[m.N++] = v;
    }

    static V Pop(Machine m)
    {
        if (m.N == 0) throw new InvalidOperationException("PostScript calculator stack underflow");
        return m.S[--m.N];
    }

    static V Num(double d, bool integer) => integer && d is >= int.MinValue and <= int.MaxValue ? new V(d, Int) : new V(d, Real);

    static int ToInt(double d) => d >= int.MaxValue ? int.MaxValue : d <= int.MinValue ? int.MinValue : (int)Math.Truncate(d);

    static void Run(Block prog, Machine m)
    {
        var items = prog.Items;
        for (var pc = 0; pc < items.Length; pc++)
        {
            var o = items[pc];
            if (o is V lit) { Push(m, lit); continue; }
            if (o is Block blk)
            {
                if (pc + 1 < items.Length && items[pc + 1] is Op.If) { pc++; if (Pop(m).D != 0) Run(blk, m); }
                else if (pc + 2 < items.Length && items[pc + 1] is Block els && items[pc + 2] is Op.IfElse) { pc += 2; if (Pop(m).D != 0) Run(blk, m); else Run(els, m); }
                else throw new InvalidOperationException("procedure without if/ifelse");
                continue;
            }
            V a, b;
            switch ((Op)o)
            {
                case Op.Add: b = Pop(m); a = Pop(m); Push(m, Num(a.D + b.D, a.T == Int && b.T == Int)); break;
                case Op.Sub: b = Pop(m); a = Pop(m); Push(m, Num(a.D - b.D, a.T == Int && b.T == Int)); break;
                case Op.Mul: b = Pop(m); a = Pop(m); Push(m, Num(a.D * b.D, a.T == Int && b.T == Int)); break;
                case Op.Div: b = Pop(m); a = Pop(m); Push(m, new V(b.D == 0 ? 0 : a.D / b.D, Real)); break;
                case Op.Idiv: b = Pop(m); a = Pop(m); Push(m, new V(ToInt(b.D) == 0 ? 0 : Math.Truncate((double)ToInt(a.D) / ToInt(b.D)), Int)); break;
                case Op.Mod: b = Pop(m); a = Pop(m); Push(m, new V(ToInt(b.D) == 0 ? 0 : (long)ToInt(a.D) % ToInt(b.D), Int)); break;
                case Op.Neg: a = Pop(m); Push(m, Num(-a.D, a.T == Int)); break;
                case Op.Abs: a = Pop(m); Push(m, Num(Math.Abs(a.D), a.T == Int)); break;
                case Op.Ceiling: a = Pop(m); Push(m, new V(Math.Ceiling(a.D), a.T)); break;
                case Op.Floor: a = Pop(m); Push(m, new V(Math.Floor(a.D), a.T)); break;
                case Op.Round: a = Pop(m); Push(m, new V(Math.Floor(a.D + 0.5), a.T)); break;
                case Op.Truncate: a = Pop(m); Push(m, new V(Math.Truncate(a.D), a.T)); break;
                case Op.Sqrt: a = Pop(m); Push(m, new V(Math.Sqrt(a.D), Real)); break;
                case Op.Sin: a = Pop(m); Push(m, new V(Math.Sin(a.D * Math.PI / 180), Real)); break;
                case Op.Cos: a = Pop(m); Push(m, new V(Math.Cos(a.D * Math.PI / 180), Real)); break;
                case Op.Atan:
                    {
                        b = Pop(m); a = Pop(m);
                        var ang = Math.Atan2(a.D, b.D) * 180 / Math.PI;
                        Push(m, new V(ang < 0 ? ang + 360 : ang, Real));
                        break;
                    }
                case Op.Exp: b = Pop(m); a = Pop(m); Push(m, new V(Math.Pow(a.D, b.D), Real)); break;
                case Op.Ln: a = Pop(m); Push(m, new V(Math.Log(a.D), Real)); break;
                case Op.Log: a = Pop(m); Push(m, new V(Math.Log10(a.D), Real)); break;
                case Op.Cvi: a = Pop(m); Push(m, new V(ToInt(a.D), Int)); break;
                case Op.Cvr: a = Pop(m); Push(m, new V(a.D, Real)); break;
                case Op.Eq: b = Pop(m); a = Pop(m); Push(m, new V(a.D == b.D ? 1 : 0, Bool)); break;
                case Op.Ne: b = Pop(m); a = Pop(m); Push(m, new V(a.D != b.D ? 1 : 0, Bool)); break;
                case Op.Gt: b = Pop(m); a = Pop(m); Push(m, new V(a.D > b.D ? 1 : 0, Bool)); break;
                case Op.Ge: b = Pop(m); a = Pop(m); Push(m, new V(a.D >= b.D ? 1 : 0, Bool)); break;
                case Op.Lt: b = Pop(m); a = Pop(m); Push(m, new V(a.D < b.D ? 1 : 0, Bool)); break;
                case Op.Le: b = Pop(m); a = Pop(m); Push(m, new V(a.D <= b.D ? 1 : 0, Bool)); break;
                case Op.And: b = Pop(m); a = Pop(m); Push(m, Logic(a, b, (x, y) => x & y)); break;
                case Op.Or: b = Pop(m); a = Pop(m); Push(m, Logic(a, b, (x, y) => x | y)); break;
                case Op.Xor: b = Pop(m); a = Pop(m); Push(m, Logic(a, b, (x, y) => x ^ y)); break;
                case Op.Not: a = Pop(m); Push(m, a.T == Bool ? new V(a.D != 0 ? 0 : 1, Bool) : new V(~ToInt(a.D), Int)); break;
                case Op.Bitshift:
                    {
                        b = Pop(m); a = Pop(m);
                        var sh = ToInt(b.D); var v = ToInt(a.D);
                        Push(m, new V(sh >= 32 || sh <= -32 ? 0 : sh >= 0 ? v << sh : (int)((uint)v >> -sh), Int));
                        break;
                    }
                case Op.True: Push(m, new V(1, Bool)); break;
                case Op.False: Push(m, new V(0, Bool)); break;
                case Op.Pop: Pop(m); break;
                case Op.Exch: b = Pop(m); a = Pop(m); Push(m, b); Push(m, a); break;
                case Op.Dup: a = Pop(m); Push(m, a); Push(m, a); break;
                case Op.Copy:
                    {
                        var n = ToInt(Pop(m).D);
                        if (n < 0 || n > m.N) throw new InvalidOperationException("copy");
                        if (m.N + n > MaxStack) throw new InvalidOperationException("PostScript calculator stack overflow");
                        Array.Copy(m.S, m.N - n, m.S, m.N, n); m.N += n;
                        break;
                    }
                case Op.Index:
                    {
                        var n = ToInt(Pop(m).D);
                        if (n < 0 || n >= m.N) throw new InvalidOperationException("index");
                        Push(m, m.S[m.N - 1 - n]);
                        break;
                    }
                case Op.Roll:
                    {
                        var j = ToInt(Pop(m).D); var n = ToInt(Pop(m).D);
                        if (n < 0 || n > m.N) throw new InvalidOperationException("roll");
                        if (n == 0) break;
                        j = ((j % n) + n) % n;
                        if (j == 0) break;
                        var seg = new V[n]; Array.Copy(m.S, m.N - n, seg, 0, n);
                        for (var i = 0; i < n; i++) m.S[m.N - n + (i + j) % n] = seg[i];
                        break;
                    }
                default: throw new InvalidOperationException("unknown PostScript operator");
            }
        }
    }

    static V Logic(V a, V b, Func<int, int, int> f)
        => a.T == Bool && b.T == Bool ? new V(f(a.D != 0 ? 1 : 0, b.D != 0 ? 1 : 0), Bool) : new V(f(ToInt(a.D), ToInt(b.D)), Int);
}
