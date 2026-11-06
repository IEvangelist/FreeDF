using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class XrefStreamHeaderRangeTests
{
    public static IEnumerable<object[]> InvalidCases()
    {
        foreach (var flate in new[] { false, true })
            foreach (var pair in new[]
            {
                ("0", "0"), ("-3", "0"), ("2147483648", "0"), ("9223372036854775807", "0"),
                ("3", "-1"), ("3", "65536"), ("3", "99999"), ("3", "2147483647")
            })
                yield return [pair.Item1, pair.Item2, flate];
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Invalid_or_unsupported_header_identity_requires_explicit_recovery(string number, string generation, bool flate)
    {
        var file = PdfFile.Load(Build(number, generation, flate));
        Assert.True(file.Repaired);
        AssertTarget(file);
    }

    [Theory]
    [InlineData("3", "0", false)]
    [InlineData("3", "0", true)]
    [InlineData("+3", "+7", false)]
    [InlineData("+3", "+7", true)]
    [InlineData("0003", "00000", false)]
    [InlineData("0003", "00000", true)]
    [InlineData("2147483647", "0", false)]
    [InlineData("2147483647", "0", true)]
    [InlineData("3", "65535", false)]
    [InlineData("3", "65535", true)]
    public void Representable_header_and_matching_self_entry_keep_clean_loading(string number, string generation, bool flate)
    {
        var file = PdfFile.Load(Build(number, generation, flate));
        Assert.False(file.Repaired);
        AssertTarget(file);
        var reference = new PdfRef(int.Parse(number), int.Parse(generation));
        var stream = Assert.IsType<PdfStream>(file.GetObject(reference));
        Assert.Equal("XRef", stream.Dict.Get("Type").AsName());
        Assert.Same(stream, file.GetObject(reference));
        Assert.False(file.Repaired);
    }

    internal static byte[] Build(string number, string generation, bool flate)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var catalog = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var target = data.Position;
        Write("2 0 obj\n<< /Type /Target >>\nendobj\n");
        var xref = data.Position;
        var entryNumber = long.TryParse(number, out var n) && n is > 2 and <= int.MaxValue ? n : 3;
        var entryGeneration = int.TryParse(generation, out var g) && g is >= 0 and <= 65535 ? g : 0;
        using var rows = new MemoryStream();
        void Row(long offset, int gen)
        {
            rows.WriteByte(1);
            for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(offset >> shift));
            for (var shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(gen >> shift));
        }
        Row(catalog, 0);
        Row(target, 0);
        Row(xref, entryGeneration);
        var payload = flate ? PdfFilters.DeflateEncode(rows.ToArray()) : rows.ToArray();
        Write($"{number} {generation} obj\n<< /Type /XRef /Root 1 0 R /Size {entryNumber + 1} " +
            $"/Index [1 2 {entryNumber} 1] /W [1 4 4] {(flate ? "/Filter /FlateDecode" : "")} /Length {payload.Length} >>\nstream\n");
        data.Write(payload);
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return data.ToArray();
    }

    static void AssertTarget(PdfFile file)
    {
        var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
        Assert.Equal("Target", target.Get("Type").AsName());
        Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
    }
}
