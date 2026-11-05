using System.Collections.Frozen;
using System.Text;

namespace PortableDocument.Core;

public sealed record PdfKeyword(string Value) : PdfObject;

public class PdfParseException(string message) : Exception(message);

/// <summary>Tokenizer + object parser used for both file bodies and content streams (ISO 32000-2 §7.2-7.3).</summary>
public sealed class PdfLexer
{
    // Only fixed operators are shared; arbitrary input must not grow a process-wide cache.
    static readonly FrozenDictionary<int, PdfKeyword> ShortKeywords =
        ("q Q cm w J j M d ri i gs m l c v y h re S s f F f* B B* b b* n W W* " +
         "BT ET Tc Tw Tz TL Tf Tr Ts Td TD Tm T* Tj TJ ' \" d0 d1 " +
         "CS cs SC SCN sc scn G g RG rg K k sh Do BI ID EI MP DP BMC BDC EMC BX EX")
        .Split(' ').ToFrozenDictionary(s => ShortKeywordKey(Encoding.ASCII.GetBytes(s)), s => new PdfKeyword(s));

    static int ShortKeywordKey(ReadOnlySpan<byte> token) => token.Length switch
    {
        1 => token[0] | 1 << 24,
        2 => token[0] | token[1] << 8 | 2 << 24,
        3 => token[0] | token[1] << 8 | token[2] << 16 | 3 << 24,
        _ => 0
    };

    readonly byte[] _d;
    public int Pos;
    public PdfLexer(byte[] data, int pos = 0) { _d = data; Pos = pos; }
    public int Length => _d.Length;
    public byte[] Data => _d;

    public static bool IsWhite(int c) => c is 0 or 9 or 10 or 12 or 13 or 32;
    public static bool IsDelim(int c) => c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';
    static bool IsRegular(int c) => !IsWhite(c) && !IsDelim(c);

    public void SkipWhite()
    {
        while (Pos < _d.Length)
        {
            var c = _d[Pos];
            if (IsWhite(c)) Pos++;
            else if (c == '%') { while (Pos < _d.Length && _d[Pos] is not (10 or 13)) Pos++; }
            else break;
        }
    }

    /// <summary>Reads next object; returns null at EOF. Closers (']' '>>') are returned as keywords.</summary>
    int _nesting;
    const int MaxNesting = 200;

    // Bounds array/dictionary recursion so hostile input cannot overflow the stack. Exceptions abandon the lexer, so no unwind is needed.
    void Enter()
    {
        if (++_nesting > MaxNesting) throw new PdfParseException("object nesting too deep");
    }

    public PdfObject? ReadObject(bool resolveRefs = true)
    {
        SkipWhite();
        if (Pos >= _d.Length) return null;
        var c = _d[Pos];
        switch (c)
        {
            case (byte)'/': return ReadName();
            case (byte)'(': return ReadLiteralString();
            case (byte)'<':
                if (Pos + 1 < _d.Length && _d[Pos + 1] == '<') { Pos += 2; return ReadDictBody(resolveRefs); }
                return ReadHexString();
            case (byte)'>':
                if (Pos + 1 < _d.Length && _d[Pos + 1] == '>') { Pos += 2; return new PdfKeyword(">>"); }
                Pos++; return new PdfKeyword(">");
            case (byte)'[': Pos++; return ReadArrayBody(resolveRefs);
            case (byte)']': Pos++; return new PdfKeyword("]");
            case (byte)'{': Pos++; return new PdfKeyword("{");
            case (byte)'}': Pos++; return new PdfKeyword("}");
            case (byte)')': Pos++; return new PdfKeyword(")");
        }
        var start = Pos;
        while (Pos < _d.Length && IsRegular(_d[Pos])) Pos++;
        var token = _d.AsSpan(start, Pos - start);
        if (TryNumber(token, out var num))
        {
            if (resolveRefs && num is PdfInteger n1 && n1.Value >= 0) return TryRef(n1, start) ?? num;
            return num;
        }
        if (ShortKeywords.TryGetValue(ShortKeywordKey(token), out var keyword)) return keyword;
        var tok = Encoding.Latin1.GetString(_d, start, Pos - start);
        return tok switch
        {
            "true" => new PdfBoolean(true),
            "false" => new PdfBoolean(false),
            "null" => PdfNull.Instance,
            _ => new PdfKeyword(tok)
        };
    }

    PdfObject? TryRef(PdfInteger first, int startPos)
    {
        var save = Pos;
        SkipWhite();
        var s = Pos;
        while (Pos < _d.Length && _d[Pos] is >= (byte)'0' and <= (byte)'9') Pos++;
        if (Pos > s && Pos - s < 6)
        {
            var gen = 0;
            for (var i = s; i < Pos; i++) gen = gen * 10 + _d[i] - '0';
            SkipWhite();
            if (Pos < _d.Length && _d[Pos] == 'R' && (Pos + 1 >= _d.Length || !IsRegular(_d[Pos + 1])))
            {
                if (first.Value > int.MaxValue) throw new PdfParseException("indirect object number exceeds supported range");
                Pos++;
                return new PdfRef((int)first.Value, gen);
            }
        }
        Pos = save;
        return null;
    }

    static bool TryNumber(ReadOnlySpan<byte> t, out PdfObject o)
    {
        o = null!;
        if (t.Length == 0) return false;
        var c = t[0];
        if (!(c is >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'-' or (byte)'.')) return false;
        // Preserve repeated leading signs and trailing non-digits after a decimal point.
        var s = t;
        var neg = false;
        while (s.Length > 0 && s[0] is (byte)'+' or (byte)'-') { if (s[0] == '-') neg = true; s = s[1..]; }
        if (s.Length == 0) return false;
        var digits = 0;
        while (digits < s.Length && s[digits] is >= (byte)'0' and <= (byte)'9') digits++;
        if (digits == s.Length)
        {
            if (long.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var l)) { o = new PdfInteger(neg ? -l : l); return true; }
            if (double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var big)) { o = new PdfReal(neg ? -big : big); return true; }
            return false;
        }
        var dot = s.IndexOf((byte)'.');
        if (dot < 0) return false;
        var end = dot + 1;
        while (end < s.Length && s[end] is >= (byte)'0' and <= (byte)'9') end++;
        var cleaned = s[..end];
        if (cleaned.Length == 1) { o = new PdfReal(0); return true; }
        if (double.TryParse(cleaned, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
        { o = new PdfReal(neg ? -d : d); return true; }
        return false;
    }

    PdfName ReadName()
    {
        Pos++;
        var sb = new List<byte>();
        while (Pos < _d.Length && IsRegular(_d[Pos]))
        {
            var c = _d[Pos++];
            if (c == '#' && Pos + 1 < _d.Length && Hex(_d[Pos]) >= 0 && Hex(_d[Pos + 1]) >= 0)
            {
                sb.Add((byte)(Hex(_d[Pos]) * 16 + Hex(_d[Pos + 1])));
                Pos += 2;
            }
            else sb.Add(c);
        }
        var arr = sb.ToArray();
        string s;
        try { s = new UTF8Encoding(false, true).GetString(arr); } catch { s = Encoding.Latin1.GetString(arr); }
        return new PdfName(s);
    }

    static int Hex(int c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    PdfString ReadHexString()
    {
        Pos++;
        var bytes = new List<byte>();
        var hi = -1;
        while (Pos < _d.Length && _d[Pos] != '>')
        {
            var h = Hex(_d[Pos++]);
            if (h < 0) continue;
            if (hi < 0) hi = h; else { bytes.Add((byte)(hi * 16 + h)); hi = -1; }
        }
        if (hi >= 0) bytes.Add((byte)(hi * 16));
        Pos++;
        return new PdfString(bytes.ToArray(), true);
    }

    PdfString ReadLiteralString()
    {
        Pos++;
        var depth = 1;
        var b = new List<byte>();
        while (Pos < _d.Length)
        {
            var c = _d[Pos++];
            if (c == '\\' && Pos < _d.Length)
            {
                var e = _d[Pos++];
                switch (e)
                {
                    case (byte)'n': b.Add(10); break;
                    case (byte)'r': b.Add(13); break;
                    case (byte)'t': b.Add(9); break;
                    case (byte)'b': b.Add(8); break;
                    case (byte)'f': b.Add(12); break;
                    case 13: if (Pos < _d.Length && _d[Pos] == 10) Pos++; break;
                    case 10: break;
                    case >= (byte)'0' and <= (byte)'7':
                        var v = e - '0';
                        for (var i = 0; i < 2 && Pos < _d.Length && _d[Pos] is >= (byte)'0' and <= (byte)'7'; i++) v = v * 8 + (_d[Pos++] - '0');
                        b.Add((byte)v);
                        break;
                    default: b.Add(e); break;
                }
            }
            else if (c == '(') { depth++; b.Add(c); }
            else if (c == ')') { if (--depth == 0) break; b.Add(c); }
            else if (c == 13) { b.Add(10); if (Pos < _d.Length && _d[Pos] == 10) Pos++; }
            else b.Add(c);
        }
        return new PdfString(b.ToArray());
    }

    PdfArray ReadArrayBody(bool resolveRefs)
    {
        Enter();
        var items = new List<PdfObject>();
        while (true)
        {
            var o = ReadObject(resolveRefs);
            if (o is null || o is PdfKeyword { Value: "]" }) break;
            items.Add(o);
        }
        _nesting--;
        return new PdfArray(items);
    }

    PdfDictionary ReadDictBody(bool resolveRefs)
    {
        Enter();
        var d = new Dictionary<string, PdfObject>();
        while (true)
        {
            var k = ReadObject(resolveRefs);
            if (k is null || k is PdfKeyword { Value: ">>" }) break;
            if (k is not PdfName name) continue;
            var v = ReadObject(resolveRefs);
            if (v is null) break;
            if (v is PdfKeyword { Value: ">>" }) { d[name.Value] = PdfNull.Instance; break; }
            d[name.Value] = v;
        }
        _nesting--;
        return new PdfDictionary(d);
    }
}
