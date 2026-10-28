using System.Text;

namespace Pdf.Core;

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
    public string Text => Decode(Bytes);

    public static string Decode(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        return Encoding.Latin1.GetString(b);
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
