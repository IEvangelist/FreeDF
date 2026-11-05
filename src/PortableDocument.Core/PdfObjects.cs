using System.Text;

namespace PortableDocument.Core;

public abstract record PdfObject;

public sealed record PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();
}

public sealed record PdfBoolean(bool Value) : PdfObject;

public sealed record PdfInteger(long Value) : PdfObject;

public sealed record PdfReal(double Value) : PdfObject;

public sealed record PdfString(byte[] Bytes, bool IsHex = false) : PdfObject
{
    /// <summary>Decodes a text string using its Unicode BOM or PDFDocEncoding; use Bytes for raw font or binary strings.</summary>
    public string Text => Decode(Bytes);

    public static string Decode(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        return string.Create(b.Length, b, static (chars, bytes) =>
        {
            const string accents = "\u02D8\u02C7\u02C6\u02D9\u02DD\u02DB\u02DA\u02DC";
            const string symbols = "\u2022\u2020\u2021\u2026\u2014\u2013\u0192\u2044\u2039\u203A\u2212\u2030\u201E\u201C\u201D\u2018\u2019\u201A\u2122\uFB01\uFB02\u0141\u0152\u0160\u0178\u017D\u0131\u0142\u0153\u0161\u017E";
            for (int i = 0; i < bytes.Length; i++)
                chars[i] = bytes[i] switch
                {
                    >= 0x18 and <= 0x1F => accents[bytes[i] - 0x18],
                    >= 0x80 and <= 0x9E => symbols[bytes[i] - 0x80],
                    0xA0 => '\u20AC',
                    _ => (char)bytes[i],
                };
        });
    }
}

public sealed record PdfName(string Value) : PdfObject
{
    public override string ToString() => "/" + Value;
}

public sealed record PdfRef(int Number, int Generation) : PdfObject;

public sealed record PdfArray(List<PdfObject> Items) : PdfObject
{
    public int Count => Items.Count;
    public PdfObject this[int i] => Items[i];
}

public record PdfDictionary(Dictionary<string, PdfObject> Entries) : PdfObject
{
    public PdfDictionary() : this(new Dictionary<string, PdfObject>()) { }
    public bool TryGet(string key, out PdfObject value) => Entries.TryGetValue(key, out value!);
    public PdfObject? Get(string key) => Entries.GetValueOrDefault(key);
}

public sealed record PdfStream(PdfDictionary Dict, byte[] RawData) : PdfObject;

public static class PdfObjectExtensions
{
    public static double? AsNumber(this PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => null
    };

    public static string? AsName(this PdfObject? o) => (o as PdfName)?.Value;
}
