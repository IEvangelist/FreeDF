using System.Text;
using PortableDocument.Core;

namespace FreeDF.Tests;

public class ObjectStreamFilterNameTests
{
    [Theory]
    [InlineData("true", "null", false)]
    [InlineData("42", "null", false)]
    [InlineData("(FlateDecode)", "null", false)]
    [InlineData("<< >>", "null", false)]
    [InlineData("[null]", "null", false)]
    [InlineData("[42]", "null", false)]
    [InlineData("[[]]", "null", false)]
    [InlineData("[<< >>]", "null", false)]
    [InlineData("[7 0 R]", "null", false)]
    [InlineData("5 0 R", "42", false)]
    [InlineData("5 0 R", "5 0 R", false)]
    [InlineData("5 0 R", "[true]", false)]
    [InlineData("[/ASCIIHexDecode 42]", "null", true)]
    [InlineData("[false /ASCIIHexDecode]", "null", true)]
    [InlineData("[/ASCIIHexDecode null]", "null", true)]
    public void Malformed_filter_names_cannot_publish_object_stream_members(string filter, string indirect, bool hex)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(filter, indirect, hex, repair));
            Assert.IsType<PdfNull>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.True(file.Repaired);
            Assert.False(file.Xref.ContainsKey(2));
            Assert.IsType<PdfNull>(file.GetObject(2));
        }
    }

    [Theory]
    [InlineData(null, "null", false)]
    [InlineData("null", "null", false)]
    [InlineData("7 0 R", "null", false)]
    [InlineData("5 0 R", "null", false)]
    [InlineData("[]", "null", false)]
    [InlineData("/ASCIIHexDecode", "null", true)]
    [InlineData("[/ASCIIHexDecode]", "null", true)]
    [InlineData("5 0 R", "/ASCIIHexDecode", true)]
    [InlineData("5 0 R", "[/ASCIIHexDecode]", true)]
    [InlineData("[5 0 R]", "/ASCIIHexDecode", true)]
    public void Valid_filter_forms_preserve_member_values_and_cached_identity(string? filter, string indirect, bool hex)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(filter, indirect, hex, repair));
            var target = Assert.IsType<PdfDictionary>(file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal("Target", target.Get("Type").AsName());
            Assert.Same(target, file.Resolve(file.Catalog.Get("Probe")));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData("42", "null", false)]
    [InlineData("[null]", "null", false)]
    [InlineData("[/ASCIIHexDecode 42]", "null", true)]
    public void Ordinary_stream_filter_recovery_is_unchanged(string filter, string indirect, bool hex)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build(filter, indirect, hex, repair, ordinary: true));
            var stream = Assert.IsType<PdfStream>(file.GetObject(3));
            Assert.Equal("2 0 << /Type /Target >>"u8.ToArray(), file.DecodeStream(stream));
            Assert.Equal(repair, file.Repaired);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    public void Filter_reference_chains_preserve_the_existing_32_reference_limit(int aliases)
    {
        foreach (var repair in new[] { false, true })
        {
            var file = PdfFile.Load(Build("5 0 R", "/ASCIIHexDecode", true, repair, filterAliases: aliases));
            var target = file.Resolve(file.Catalog.Get("Probe"));
            if (aliases < 32)
            {
                Assert.Equal("Target", Assert.IsType<PdfDictionary>(target).Get("Type").AsName());
                Assert.Equal(repair, file.Repaired);
            }
            else
            {
                Assert.IsType<PdfNull>(target);
                Assert.True(file.Repaired);
                Assert.False(file.Xref.ContainsKey(2));
            }
        }
    }

    internal static byte[] Build(string? filter, string indirect, bool hex, bool repair, bool ordinary = false,
        int filterAliases = 0, string? decodeParms = null, string? encodedOverride = null,
        string? externalFile = null, byte[]? encodedBytesOverride = null)
    {
        using var data = new MemoryStream();
        void Write(string text) => data.Write(Encoding.ASCII.GetBytes(text));
        Write("%PDF-1.7\n");
        var root = data.Position;
        Write("1 0 obj\n<< /Type /Catalog /Probe 2 0 R >>\nendobj\n");
        var container = data.Position;
        const string decoded = "2 0 << /Type /Target >>";
        var encoded = encodedOverride ?? (hex ? Convert.ToHexString(Encoding.ASCII.GetBytes(decoded)) + ">" : decoded);
        var payload = encodedBytesOverride ?? Encoding.ASCII.GetBytes(encoded);
        var entry = filter is null ? "" : "/Filter " + filter;
        var parms = decodeParms is null ? "" : "/DecodeParms " + decodeParms;
        var external = externalFile is null ? "" : "/F " + externalFile;
        Write($"3 0 obj\n<< /Type /{(ordinary ? "OtherX" : "ObjStm")} /N 1 /First 4 {entry} {parms} {external} /Length {payload.Length} >>\nstream\n");
        data.Write(payload);
        Write("\nendstream\nendobj\n");
        var indirectOffset = data.Position;
        Write($"5 0 obj\n{(filterAliases > 0 ? "6 0 R" : indirect)}\nendobj\n");
        var aliasOffsets = new List<long>();
        for (var i = 0; i < filterAliases; i++)
        {
            aliasOffsets.Add(data.Position);
            Write($"{6 + i} 0 obj\n{(i + 1 < filterAliases ? $"{7 + i} 0 R" : indirect)}\nendobj\n");
        }
        var xref = data.Position;
        using var rows = new MemoryStream();
        void Row(ulong kind, ulong offset, ulong index)
        {
            foreach (var field in new[] { kind, offset, index })
                for (var shift = 56; shift >= 0; shift -= 8) rows.WriteByte((byte)(field >> shift));
        }
        Row(1, (ulong)root, 0);
        Row(2, 3, 0);
        Row(1, (ulong)container, 0);
        Row(1, (ulong)xref, 0);
        Row(1, (ulong)indirectOffset, 0);
        foreach (var offset in aliasOffsets) Row(1, (ulong)offset, 0);
        Write($"4 0 obj\n<< /Type /XRef /Root 1 0 R /Size {6 + filterAliases} /Index [1 {5 + filterAliases}] /W [8 8 8] /Length {rows.Length} >>\nstream\n");
        data.Write(rows.ToArray());
        Write($"\nendstream\nendobj\nstartxref\n{(repair ? -1 : xref)}\n%%EOF\n");
        return data.ToArray();
    }
}
