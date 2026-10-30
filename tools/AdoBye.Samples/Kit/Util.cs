using System.Globalization;
using System.Text;

namespace AdoBye.Samples.Kit;

/// <summary>A colour given as "#RRGGBB" or as 0..1 components.</summary>
public readonly record struct Color(double R, double G, double B)
{
    public static implicit operator Color(string hex)
    {
        var h = hex.TrimStart('#');
        return new Color(Convert.ToInt32(h[..2], 16) / 255.0, Convert.ToInt32(h[2..4], 16) / 255.0, Convert.ToInt32(h[4..6], 16) / 255.0);
    }

    public static implicit operator Color((double R, double G, double B) t) => new(t.R, t.G, t.B);

    public string Ops => $"{K.N(R)} {K.N(G)} {K.N(B)}";
}

/// <summary>Number/string formatting shared by every generator.</summary>
public static class K
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly Dictionary<char, byte> Cp1252High = new()
    {
        ['\u20AC'] = 0x80, ['\u201A'] = 0x82, ['\u0192'] = 0x83, ['\u201E'] = 0x84, ['\u2026'] = 0x85, ['\u2020'] = 0x86, ['\u2021'] = 0x87,
        ['\u02C6'] = 0x88, ['\u2030'] = 0x89, ['\u0160'] = 0x8A, ['\u2039'] = 0x8B, ['\u0152'] = 0x8C, ['\u017D'] = 0x8E, ['\u2018'] = 0x91,
        ['\u2019'] = 0x92, ['\u201C'] = 0x93, ['\u201D'] = 0x94, ['\u2022'] = 0x95, ['\u2013'] = 0x96, ['\u2014'] = 0x97, ['\u02DC'] = 0x98,
        ['\u2122'] = 0x99, ['\u0161'] = 0x9A, ['\u203A'] = 0x9B, ['\u0153'] = 0x9C, ['\u017E'] = 0x9E, ['\u0178'] = 0x9F,
    };

    /// <summary>Shortest fixed-point form with up to four decimals ("0" for -0).</summary>
    public static string N(double x)
    {
        var s = x.ToString("F4", Inv);
        if (s.Contains('.')) s = s.TrimEnd('0').TrimEnd('.');
        return s is "" or "-0" ? "0" : s;
    }

    public static byte[] Cp1252(string s)
    {
        var o = new byte[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            o[i] = c < 0x80 || (c >= 0xA0 && c <= 0xFF) ? (byte)c : Cp1252High.TryGetValue(c, out var b) ? b : (byte)'?';
        }
        return o;
    }

    /// <summary>PDF literal string from WinAnsi text.</summary>
    public static string PStr(string s) => PStr(Cp1252(s));

    public static string PStr(byte[] b)
    {
        var sb = new StringBuilder("(");
        foreach (var x in b)
        {
            switch (x)
            {
                case (byte)'\\': sb.Append("\\\\"); break;
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\r': sb.Append("\\r"); break;
                default: sb.Append((char)x); break;
            }
        }
        return sb.Append(')').ToString();
    }

    /// <summary>Text string in UTF-16BE with BOM as a hex string.</summary>
    public static string Utf16(string s) => "<" + Convert.ToHexString(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(s)).ToArray()) + ">";

    public static string Join(IEnumerable<double> v) => string.Join(" ", v.Select(N));

    public static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    public static string Latin1(byte[] b) => Encoding.Latin1.GetString(b);
}
